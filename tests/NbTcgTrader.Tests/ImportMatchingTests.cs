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

// End-to-end coverage of import auto-matching (BACKLOG #15) through POST /import/jobs,
// with the catalog fake serving query-aware results. Covers the AC: set+number exact
// match, single-name auto-match, ambiguous rows staying Unmatched, and job counts +
// status (Completed / NeedsReview). Skips cleanly when Docker is unavailable. Each
// test gets its own host (own rate-limit bucket) and user.
[Collection(IntegrationTestCollection.Name)]
public sealed class ImportMatchingTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly List<MatchingApiFactory> _factories = [];
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

    // "Charizard" exists in two sets, so a name-only search for it is ambiguous;
    // set+number pins it. "Machamp" is unique by name.
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
    public async Task Set_and_number_match_auto_matches_and_completes_the_job()
    {
        var catalog = KnownCatalog();
        var factory = CreateFactory(catalog);
        var client = factory.CreateClient();
        var token = await RegisterAsync(client);

        // "Base Set 2" also has a number 4, so this only matches exactly when the
        // set narrows it — the name alone would be ambiguous.
        var response = await SendCsvAsync(client, token,
            "card_name,set,card_number\r\nCharizard,Base,4\r\n");

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var job = await response.Content.ReadFromJsonAsync<JobDto>(Json);
        job.ShouldNotBeNull();
        job.Status.ShouldBe("Completed");
        job.RowsMatched.ShouldBe(1);
        job.RowsUnmatched.ShouldBe(0);

        // The exact-match query carried set+number (not the name).
        var query = catalog.SearchQueries.ShouldHaveSingleItem();
        query.Set.ShouldBe("Base");
        query.Number.ShouldBe("4");
        query.Name.ShouldBeNull();

        // The row points at the right card, persisted into the local catalog.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.ImportRows.Include(r => r.MatchedCard)
            .SingleAsync(r => r.ImportJobId == job.Id);
        row.MatchStatus.ShouldBe(MatchStatus.AutoMatched);
        row.MatchedCard.ShouldNotBeNull().ExternalId.ShouldBe("base1-4");
    }

    [SkippableFact]
    public async Task Unique_name_match_auto_matches()
    {
        var factory = CreateFactory(KnownCatalog());
        var client = factory.CreateClient();
        var token = await RegisterAsync(client);

        var response = await SendCsvAsync(client, token, "card_name\r\nMachamp\r\n");

        var job = await response.Content.ReadFromJsonAsync<JobDto>(Json);
        job.ShouldNotBeNull();
        job.Status.ShouldBe("Completed");
        job.RowsMatched.ShouldBe(1);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.ImportRows.Include(r => r.MatchedCard)
            .SingleAsync(r => r.ImportJobId == job.Id);
        row.MatchedCard.ShouldNotBeNull().ExternalId.ShouldBe("base1-8");
    }

    [SkippableFact]
    public async Task Ambiguous_name_stays_unmatched_and_job_needs_review()
    {
        var factory = CreateFactory(KnownCatalog());
        var client = factory.CreateClient();
        var token = await RegisterAsync(client);

        // Two Charizards exist; a name-only row must not be guessed.
        var response = await SendCsvAsync(client, token, "card_name\r\nCharizard\r\n");

        var job = await response.Content.ReadFromJsonAsync<JobDto>(Json);
        job.ShouldNotBeNull();
        job.Status.ShouldBe("NeedsReview");
        job.RowsMatched.ShouldBe(0);
        job.RowsUnmatched.ShouldBe(1);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.ImportRows.SingleAsync(r => r.ImportJobId == job.Id);
        row.MatchStatus.ShouldBe(MatchStatus.Unmatched);
        row.MatchedCardId.ShouldBeNull();
        // No card was persisted for a non-match.
        (await db.Cards.AnyAsync()).ShouldBeFalse();
    }

    [SkippableFact]
    public async Task Mixed_file_updates_counts_and_needs_review()
    {
        var factory = CreateFactory(KnownCatalog());
        var client = factory.CreateClient();
        var token = await RegisterAsync(client);

        var response = await SendCsvAsync(client, token,
            "card_name,set,card_number\r\n" +
            "Machamp,,\r\n" +        // unique name -> AutoMatched
            "Charizard,,\r\n" +      // ambiguous name -> Unmatched
            "Pikachu,,\r\n");        // unknown -> Unmatched

        var job = await response.Content.ReadFromJsonAsync<JobDto>(Json);
        job.ShouldNotBeNull();
        job.Status.ShouldBe("NeedsReview");
        job.RowsTotal.ShouldBe(3);
        job.RowsMatched.ShouldBe(1);
        job.RowsUnmatched.ShouldBe(2);
    }

    [SkippableFact]
    public async Task Duplicate_lookups_hit_the_catalog_once_and_share_the_staged_rows()
    {
        var catalog = KnownCatalog();
        var factory = CreateFactory(catalog);
        var client = factory.CreateClient();
        var token = await RegisterAsync(client);

        // Two identical Machamp rows + a set+number Charizard: both matched cards
        // belong to the same (new) "Base" set, staged once.
        var response = await SendCsvAsync(client, token,
            "card_name,set,card_number\r\n" +
            "Machamp,,\r\n" +
            "Machamp,,\r\n" +
            "Charizard,Base,4\r\n");

        var job = await response.Content.ReadFromJsonAsync<JobDto>(Json);
        job.ShouldNotBeNull();
        job.Status.ShouldBe("Completed");
        job.RowsMatched.ShouldBe(3);

        // The two identical name rows resolved through one provider search.
        catalog.SearchQueries.Count.ShouldBe(2);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Cards.CountAsync()).ShouldBe(2);
        (await db.CardSets.CountAsync()).ShouldBe(1);
        (await db.Games.CountAsync()).ShouldBe(1);
    }

    [SkippableFact]
    public async Task Provider_failure_leaves_rows_unmatched_for_review()
    {
        var catalog = KnownCatalog();
        catalog.SearchFailure = new HttpRequestException("provider down");
        var factory = CreateFactory(catalog);
        var client = factory.CreateClient();
        var token = await RegisterAsync(client);

        var response = await SendCsvAsync(client, token, "card_name\r\nMachamp\r\n");

        // The upload still succeeds — matching degrades to manual review (#16).
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var job = await response.Content.ReadFromJsonAsync<JobDto>(Json);
        job.ShouldNotBeNull();
        job.Status.ShouldBe("NeedsReview");
        job.RowsMatched.ShouldBe(0);
        job.RowsUnmatched.ShouldBe(1);
    }

    private MatchingApiFactory CreateFactory(FakeCardCatalogClient catalog)
    {
        Skip.If(_dockerUnavailableReason is not null, _dockerUnavailableReason);

        var factory = new MatchingApiFactory(_postgres!.GetConnectionString(), catalog);
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

    private static async Task<HttpResponseMessage> SendCsvAsync(
        HttpClient client, string token, string csv)
    {
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        file.Headers.ContentType = MediaTypeHeaderValue.Parse("text/csv");
        using var content = new MultipartFormDataContent { { file, "file", "cards.csv" } };

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

    // Boots the real app against the Testcontainers Postgres (migrations on) with the
    // provider client replaced by the query-aware fake.
    private sealed class MatchingApiFactory(
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
