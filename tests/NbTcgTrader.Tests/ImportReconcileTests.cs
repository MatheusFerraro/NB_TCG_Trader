using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NbTcgTrader.Api.Common.Domain;
using NbTcgTrader.Api.Common.Persistence;
using NbTcgTrader.Api.Features.Catalog;
using Shouldly;
using Testcontainers.PostgreSql;

namespace NbTcgTrader.Tests;

// End-to-end coverage of import reconciliation (BACKLOG #16) through the /import endpoints
// against a real Postgres via Testcontainers, with the catalog fake serving query-aware
// results. Covers the AC: auto-matched rows materialize into binder items on upload;
// listing surfaces the unmatched rows; resolving an unmatched row creates the binder item
// and marks it ManuallyMatched; skipping excludes it; the job completes once none remain.
// Also covers owner-scoping (404) and re-reconciling a decided row (409). Skips cleanly
// when Docker is unavailable. Each test gets its own host (own rate-limit bucket) and user.
[Collection(IntegrationTestCollection.Name)]
public sealed class ImportReconcileTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly List<ReconcileApiFactory> _factories = [];
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

    // "Charizard" exists in two sets (ambiguous by name); "Machamp" is unique by name.
    private static FakeCardCatalogClient KnownCatalog()
    {
        var baseSet = new CatalogSet("base1", "Base", "BS", new DateOnly(1999, 1, 9));
        var baseSet2 = new CatalogSet("base2", "Base Set 2", "B2", new DateOnly(2000, 2, 24));

        var catalog = new FakeCardCatalogClient { SearchFromCards = true };
        catalog.Cards["base1-4"] = new CatalogCard(
            "base1-4", "Charizard", "4", "Rare Holo",
            "https://img/charizard-large.png", null, baseSet);
        catalog.Cards["base2-4"] = new CatalogCard(
            "base2-4", "Charizard", "4", "Rare Holo",
            "https://img/charizard-b2-large.png", null, baseSet2);
        catalog.Cards["base1-8"] = new CatalogCard(
            "base1-8", "Machamp", "8", "Rare Holo",
            "https://img/machamp-large.png", null, baseSet);
        return catalog;
    }

    [SkippableFact]
    public async Task Auto_matched_rows_become_binder_items_on_upload()
    {
        var factory = CreateFactory(KnownCatalog());
        var client = factory.CreateClient();
        var token = await RegisterAsync(client);

        // Unique name auto-matches (#15); the row should now be a binder item (#16).
        var job = await UploadAsync(client, token,
            "card_name,quantity,condition,price,for_sale\r\nMachamp,3,LP,12.50,true\r\n");
        job.Status.ShouldBe("Completed");

        var binder = await GetBinderAsync(client, token);
        var item = binder.Items.ShouldHaveSingleItem();
        item.Card.ExternalId.ShouldBe("base1-8");
        item.Quantity.ShouldBe(3);
        item.Condition.ShouldBe("LP");
        item.IsForSale.ShouldBeTrue();
        item.Price.ShouldBe(12.50m);
    }

    [SkippableFact]
    public async Task List_returns_only_unmatched_rows_with_raw_values()
    {
        var factory = CreateFactory(KnownCatalog());
        var client = factory.CreateClient();
        var token = await RegisterAsync(client);

        var job = await UploadAsync(client, token,
            "card_name,set,card_number,quantity\r\n" +
            "Machamp,,,1\r\n" +     // unique name -> AutoMatched (excluded from the list)
            "Charizard,,,2\r\n" +  // ambiguous -> Unmatched
            "Pikachu,,,4\r\n");    // unknown -> Unmatched
        job.RowsUnmatched.ShouldBe(2);

        var list = await ListAsync(client, token, job.Id);
        list.Job.RowsUnmatched.ShouldBe(2);
        list.Rows.Count.ShouldBe(2);
        list.Rows.Select(r => r.RawName).ShouldBe(["Charizard", "Pikachu"], ignoreOrder: true);
        list.Rows.Single(r => r.RawName == "Charizard").Quantity.ShouldBe(2);
    }

    [SkippableFact]
    public async Task Resolving_a_row_creates_the_binder_item_and_marks_it_manually_matched()
    {
        var factory = CreateFactory(KnownCatalog());
        var client = factory.CreateClient();
        var token = await RegisterAsync(client);

        var job = await UploadAsync(client, token,
            "card_name,quantity,condition\r\nCharizard,2,MP\r\n"); // ambiguous -> Unmatched
        var rowId = (await ListAsync(client, token, job.Id)).Rows.Single().Id;

        var response = await ResolveAsync(client, token, job.Id, rowId, "base1-4");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var resolved = await response.Content.ReadFromJsonAsync<ResolveDto>(Json);
        resolved.ShouldNotBeNull();
        resolved.MatchStatus.ShouldBe("ManuallyMatched");
        resolved.CreatedItemId.ShouldBeGreaterThan(0);
        // Only that one row was unmatched, so resolving it completes the job.
        resolved.Job.Status.ShouldBe("Completed");
        resolved.Job.RowsUnmatched.ShouldBe(0);
        resolved.Job.RowsMatched.ShouldBe(1);

        // The chosen card is now a binder item carrying the row's parsed values.
        var item = (await GetBinderAsync(client, token)).Items.ShouldHaveSingleItem();
        item.Card.ExternalId.ShouldBe("base1-4");
        item.Quantity.ShouldBe(2);
        item.Condition.ShouldBe("MP");

        // The row itself is now ManuallyMatched and no longer appears in the review list.
        (await ListAsync(client, token, job.Id)).Rows.ShouldBeEmpty();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.ImportRows.SingleAsync(r => r.Id == rowId);
        row.MatchStatus.ShouldBe(MatchStatus.ManuallyMatched);
    }

    [SkippableFact]
    public async Task Skipping_a_row_excludes_it_and_completes_the_job()
    {
        var factory = CreateFactory(KnownCatalog());
        var client = factory.CreateClient();
        var token = await RegisterAsync(client);

        var job = await UploadAsync(client, token, "card_name\r\nPikachu\r\n"); // unknown
        var rowId = (await ListAsync(client, token, job.Id)).Rows.Single().Id;

        var response = await SkipAsync(client, token, job.Id, rowId);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var skipped = await response.Content.ReadFromJsonAsync<SkipDto>(Json);
        skipped.ShouldNotBeNull();
        skipped.MatchStatus.ShouldBe("Skipped");
        skipped.Job.Status.ShouldBe("Completed");
        skipped.Job.RowsUnmatched.ShouldBe(0);
        // Skipped rows are excluded from the matched count and create no binder item.
        skipped.Job.RowsMatched.ShouldBe(0);
        (await GetBinderAsync(client, token)).Items.ShouldBeEmpty();
    }

    [SkippableFact]
    public async Task Resolving_an_already_reconciled_row_is_a_409()
    {
        var factory = CreateFactory(KnownCatalog());
        var client = factory.CreateClient();
        var token = await RegisterAsync(client);

        var job = await UploadAsync(client, token, "card_name\r\nPikachu\r\n");
        var rowId = (await ListAsync(client, token, job.Id)).Rows.Single().Id;

        (await SkipAsync(client, token, job.Id, rowId)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // The row is Skipped now, so a second decision on it conflicts.
        var response = await ResolveAsync(client, token, job.Id, rowId, "base1-4");
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    [SkippableFact]
    public async Task Resolving_with_an_unknown_card_is_a_404()
    {
        var factory = CreateFactory(KnownCatalog());
        var client = factory.CreateClient();
        var token = await RegisterAsync(client);

        var job = await UploadAsync(client, token, "card_name\r\nPikachu\r\n");
        var rowId = (await ListAsync(client, token, job.Id)).Rows.Single().Id;

        var response = await ResolveAsync(client, token, job.Id, rowId, "no-such-card");
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    [SkippableFact]
    public async Task Reconciling_someone_elses_job_is_an_indistinguishable_404()
    {
        var factory = CreateFactory(KnownCatalog());
        var client = factory.CreateClient();
        var owner = await RegisterAsync(client);
        var intruder = await RegisterAsync(client);

        var job = await UploadAsync(client, owner, "card_name\r\nCharizard\r\n");
        var rowId = (await ListAsync(client, owner, job.Id)).Rows.Single().Id;

        // The intruder can neither list nor resolve nor skip the owner's job.
        (await SendGetAsync(client, intruder, $"/import/jobs/{job.Id}/rows"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ResolveAsync(client, intruder, job.Id, rowId, "base1-4"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await SkipAsync(client, intruder, job.Id, rowId))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // And nothing about the owner's job changed.
        var list = await ListAsync(client, owner, job.Id);
        list.Rows.ShouldHaveSingleItem();
        list.Job.Status.ShouldBe("NeedsReview");
    }

    [SkippableFact]
    public async Task Reconcile_endpoints_require_a_token()
    {
        var factory = CreateFactory(KnownCatalog());
        var client = factory.CreateClient();

        (await client.GetAsync("/import/jobs/1/rows"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await client.PostAsJsonAsync("/import/jobs/1/rows/1/resolve",
                new { cardExternalId = "base1-4" }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await client.PostAsync("/import/jobs/1/rows/1/skip", null))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private ReconcileApiFactory CreateFactory(FakeCardCatalogClient catalog)
    {
        Skip.If(_dockerUnavailableReason is not null, _dockerUnavailableReason);

        var factory = new ReconcileApiFactory(_postgres!.GetConnectionString(), catalog);
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

    private static async Task<JobDto> UploadAsync(HttpClient client, string token, string csv)
    {
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        file.Headers.ContentType = MediaTypeHeaderValue.Parse("text/csv");
        using var content = new MultipartFormDataContent { { file, "file", "cards.csv" } };

        var request = new HttpRequestMessage(HttpMethod.Post, "/import/jobs") { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JobDto>(Json))!;
    }

    private static async Task<UnmatchedRowsDto> ListAsync(HttpClient client, string token, int jobId)
    {
        var response = await SendGetAsync(client, token, $"/import/jobs/{jobId}/rows");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<UnmatchedRowsDto>(Json))!;
    }

    private static Task<HttpResponseMessage> ResolveAsync(
        HttpClient client, string token, int jobId, int rowId, string cardExternalId)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post, $"/import/jobs/{jobId}/rows/{rowId}/resolve")
        {
            Content = JsonContent.Create(new { cardExternalId }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> SkipAsync(
        HttpClient client, string token, int jobId, int rowId)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post, $"/import/jobs/{jobId}/rows/{rowId}/skip");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client.SendAsync(request);
    }

    private static async Task<BinderDto> GetBinderAsync(HttpClient client, string token)
    {
        var response = await SendGetAsync(client, token, "/collection/me");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<BinderDto>(Json))!;
    }

    private static Task<HttpResponseMessage> SendGetAsync(
        HttpClient client, string token, string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client.SendAsync(request);
    }

    // Local DTOs assert the over-the-wire JSON contract (enums as strings).
    private sealed record JobDto(int Id, string Status, int RowsTotal, int RowsMatched, int RowsUnmatched);

    private sealed record ProgressDto(int JobId, string Status, int RowsTotal, int RowsMatched, int RowsUnmatched);

    private sealed record RowDto(
        int Id, string RawName, string? RawSet, string? RawNumber, int Quantity,
        decimal? Price, string? Condition, bool IsForSale);

    private sealed record UnmatchedRowsDto(ProgressDto Job, IReadOnlyList<RowDto> Rows);

    private sealed record ResolveDto(ProgressDto Job, int RowId, string MatchStatus, int CreatedItemId);

    private sealed record SkipDto(ProgressDto Job, int RowId, string MatchStatus);

    private sealed record BinderDto(IReadOnlyList<BinderItemDto> Items, int Page, int PageSize, int TotalCount);

    private sealed record BinderItemDto(
        int Id, BinderCardDto Card, int Quantity, string Condition, bool IsForSale,
        decimal? Price, string Currency, bool IsPrivate, string? Notes);

    private sealed record BinderCardDto(int Id, string ExternalId, string Name);

    // Boots the real app against the Testcontainers Postgres (migrations on) with the
    // provider client replaced by the query-aware fake.
    private sealed class ReconcileApiFactory(
        string connectionString, FakeCardCatalogClient catalog) : WebApplicationFactory<Program>
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

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ICardCatalogClient>();
                services.AddSingleton<ICardCatalogClient>(catalog);
            });
        }
    }
}
