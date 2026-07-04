using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NbTcgTrader.Api.Common.Persistence;
using Shouldly;
using Testcontainers.PostgreSql;

namespace NbTcgTrader.Tests;

// End-to-end coverage of POST /import/jobs (BACKLOG #14) against a real Postgres via
// Testcontainers. Covers the AC: bad files rejected with clear ProblemDetails errors
// (extension, content type, row values), oversized files and row counts guarded, and
// the row count recorded on the persisted job. Skips cleanly when Docker is
// unavailable. Each test gets its own host (own rate-limit bucket) and user.
[Collection(IntegrationTestCollection.Name)]
public sealed class ImportUploadTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private const string ValidCsv =
        "card_name,set,card_number,quantity,condition,price,for_sale\r\n" +
        "Charizard,Base,4,2,LP,49.99,true\r\n" +
        "Machamp,Base,8,1,NM,,false\r\n";

    private readonly List<ImportApiFactory> _factories = [];
    private PostgreSqlContainer? _postgres;
    private string? _dockerUnavailableReason;

    public async Task InitializeAsync()
    {
        try
        {
            _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
            await _postgres.StartAsync();
        }
        catch (Exception ex)
        {
            _postgres = null;
            _dockerUnavailableReason = $"Docker is not available: {ex.Message}";
        }
    }

    public async Task DisposeAsync()
    {
        foreach (var factory in _factories)
        {
            await factory.DisposeAsync();
        }

        if (_postgres is not null)
        {
            await _postgres.DisposeAsync();
        }
    }

    [SkippableFact]
    public async Task Upload_valid_csv_returns_201_and_persists_job_with_rows()
    {
        var factory = CreateFactory();
        var client = factory.CreateClient();
        var token = await RegisterAsync(client);

        var response = await SendUploadAsync(
            client, token, Encoding.UTF8.GetBytes(ValidCsv), "my cards.csv", "text/csv");

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        response.Headers.Location.ShouldNotBeNull();

        var job = await response.Content.ReadFromJsonAsync<JobDto>(Json);
        job.ShouldNotBeNull();
        job.Id.ShouldBeGreaterThan(0);
        job.FileName.ShouldBe("my cards.csv");
        job.Status.ShouldBe("Pending");
        job.RowsTotal.ShouldBe(2);
        job.RowsMatched.ShouldBe(0);
        job.RowsUnmatched.ShouldBe(2);

        // Durability: the job and its parsed rows are in the database, raw values kept.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var saved = await db.ImportJobs.Include(j => j.Rows).SingleAsync(j => j.Id == job.Id);
        saved.RowsTotal.ShouldBe(2);
        saved.Rows.Count.ShouldBe(2);

        var charizard = saved.Rows.Single(r => r.RawName == "Charizard");
        charizard.RawSet.ShouldBe("Base");
        charizard.RawNumber.ShouldBe("4");
        charizard.Quantity.ShouldBe(2);
        charizard.Price.ShouldBe(49.99m);
        charizard.IsForSale.ShouldBeTrue();
    }

    [SkippableFact]
    public async Task Upload_valid_xlsx_returns_201_and_persists_job()
    {
        var factory = CreateFactory();
        var client = factory.CreateClient();
        var token = await RegisterAsync(client);

        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Cards");
        sheet.Cell(1, 1).Value = "card_name";
        sheet.Cell(1, 2).Value = "quantity";
        sheet.Cell(2, 1).Value = "Charizard";
        sheet.Cell(2, 2).Value = 3;
        using var buffer = new MemoryStream();
        workbook.SaveAs(buffer);

        var response = await SendUploadAsync(
            client, token, buffer.ToArray(), "cards.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var job = await response.Content.ReadFromJsonAsync<JobDto>(Json);
        job.ShouldNotBeNull();
        job.RowsTotal.ShouldBe(1);
    }

    [SkippableFact]
    public async Task Upload_with_disallowed_extension_returns_400()
    {
        var factory = CreateFactory();
        var client = factory.CreateClient();
        var token = await RegisterAsync(client);

        var response = await SendUploadAsync(
            client, token, Encoding.UTF8.GetBytes(ValidCsv), "cards.txt", "text/plain");

        await ShouldBeValidationProblemAsync(response, "Only .csv and .xlsx");
    }

    [SkippableFact]
    public async Task Upload_with_mismatched_content_type_returns_400()
    {
        var factory = CreateFactory();
        var client = factory.CreateClient();
        var token = await RegisterAsync(client);

        var response = await SendUploadAsync(
            client, token, Encoding.UTF8.GetBytes(ValidCsv), "cards.csv", "application/pdf");

        await ShouldBeValidationProblemAsync(response, "content type");
    }

    [SkippableFact]
    public async Task Upload_over_the_size_cap_returns_400()
    {
        var factory = CreateFactory(("Import:MaxFileBytes", "64"));
        var client = factory.CreateClient();
        var token = await RegisterAsync(client);

        var response = await SendUploadAsync(
            client, token, Encoding.UTF8.GetBytes(ValidCsv), "cards.csv", "text/csv");

        await ShouldBeValidationProblemAsync(response, "maximum");
    }

    [SkippableFact]
    public async Task Upload_over_the_row_cap_returns_400()
    {
        var factory = CreateFactory(("Import:MaxRows", "1"));
        var client = factory.CreateClient();
        var token = await RegisterAsync(client);

        var response = await SendUploadAsync(
            client, token, Encoding.UTF8.GetBytes(ValidCsv), "cards.csv", "text/csv");

        await ShouldBeValidationProblemAsync(response, "more than 1 data rows");
    }

    [SkippableFact]
    public async Task Upload_with_invalid_rows_returns_400_naming_the_rows()
    {
        var factory = CreateFactory();
        var client = factory.CreateClient();
        var token = await RegisterAsync(client);

        var csv = "card_name,quantity\r\nCharizard,1\r\nMachamp,zero\r\n";
        var response = await SendUploadAsync(
            client, token, Encoding.UTF8.GetBytes(csv), "cards.csv", "text/csv");

        await ShouldBeValidationProblemAsync(response, "Row 3");

        // Nothing was persisted: a job only ever exists fully parsed.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.ImportJobs.AnyAsync()).ShouldBeFalse();
    }

    [SkippableFact]
    public async Task Upload_without_a_token_returns_401()
    {
        var factory = CreateFactory();
        var client = factory.CreateClient();

        using var content = BuildUpload(
            Encoding.UTF8.GetBytes(ValidCsv), "cards.csv", "text/csv");
        var response = await client.PostAsync("/import/jobs", content);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [SkippableFact]
    public async Task Upload_is_rate_limited_after_five_in_a_minute()
    {
        var factory = CreateFactory();
        var client = factory.CreateClient();
        var token = await RegisterAsync(client);

        var bytes = Encoding.UTF8.GetBytes(ValidCsv);
        HttpResponseMessage? last = null;
        for (var i = 0; i < 5; i++)
        {
            last = await SendUploadAsync(client, token, bytes, "cards.csv", "text/csv");
        }

        last!.StatusCode.ShouldBe(HttpStatusCode.Created);

        var overLimit = await SendUploadAsync(client, token, bytes, "cards.csv", "text/csv");

        overLimit.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    private static async Task ShouldBeValidationProblemAsync(
        HttpResponseMessage response, string expectedFragment)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");

        // The AC wants clear errors: the message names the problem, not just a 400.
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain(expectedFragment);
    }

    private ImportApiFactory CreateFactory(params (string Key, string Value)[] settings)
    {
        Skip.If(_dockerUnavailableReason is not null, _dockerUnavailableReason);

        var factory = new ImportApiFactory(_postgres!.GetConnectionString(), settings);
        _factories.Add(factory);
        return factory;
    }

    private static async Task<string> RegisterAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/auth/register", new
        {
            email = $"user-{Guid.NewGuid():N}@example.com",
            password = "Sup3rSecret!Pwd",
            displayName = "Ash",
            city = "Moncton",
            country = "Canada",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var auth = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        return auth.GetProperty("accessToken").GetString()!;
    }

    private static MultipartFormDataContent BuildUpload(
        byte[] bytes, string fileName, string contentType)
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        return new MultipartFormDataContent { { file, "file", fileName } };
    }

    private static async Task<HttpResponseMessage> SendUploadAsync(
        HttpClient client, string token, byte[] bytes, string fileName, string contentType)
    {
        using var content = BuildUpload(bytes, fileName, contentType);
        var request = new HttpRequestMessage(HttpMethod.Post, "/import/jobs")
        {
            Content = content,
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    // Local DTO asserts the over-the-wire JSON contract (enums as strings).
    private sealed record JobDto(
        int Id, string FileName, string Status, int RowsTotal, int RowsMatched,
        int RowsUnmatched);

    // Boots the real app against the Testcontainers Postgres (migrations on) with a
    // throwaway Jwt/placeholder config; per-test Import:* settings tighten the caps.
    private sealed class ImportApiFactory(
        string connectionString,
        (string Key, string Value)[] settings) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Database:ApplyMigrationsOnStartup", "true");
            builder.UseSetting("ConnectionStrings:Default", connectionString);
            builder.UseSetting("Jwt:SigningKey", TestJwt.SigningKey);
            builder.UseSetting("Jwt:Issuer", TestJwt.Issuer);
            builder.UseSetting("Jwt:Audience", TestJwt.Audience);
            builder.UseSetting(
                "Catalog:PlaceholderImageUrl",
                "https://api.example.com/assets/card-placeholder.svg");

            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }
        }
    }
}
