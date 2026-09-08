using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using NbTcgTrader.Api.Common.Extensions;
using NbTcgTrader.Api.Common.Persistence;
using NbTcgTrader.Api.Features.Catalog;
using NbTcgTrader.Api.Features.Catalog.Seeding;
using Shouldly;
using Testcontainers.PostgreSql;

namespace NbTcgTrader.Tests;

// End-to-end coverage of import-assist candidate suggestions (#66 follow-up) against a real
// Postgres via Testcontainers. A small catalog is seeded into Card/CardSet; the provider
// client is a fake that matches nothing, so uploaded rows all land Unmatched. The candidates
// endpoint then ranks the LOCAL catalog by pg_trgm similarity. Covers the ACs: suggestions
// are real catalog cards, ranked closest-first, with typo tolerance the exact matcher lacks;
// plus owner-scoping (404) and auth (401). Skips cleanly when Docker is unavailable.
[Collection(IntegrationTestCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ImportRowCandidatesTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const string Placeholder = "https://api.example.com/assets/card-placeholder.svg";

    private readonly List<CandidatesApiFactory> _factories = [];
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
            return;
        }

        // Boot once to apply migrations (creates the pg_trgm index) and seed the shared,
        // read-only catalog into the container's database. Per-test factories reuse it.
        var bootstrap = new CandidatesApiFactory(_postgres.GetConnectionString());
        _factories.Add(bootstrap);
        _ = bootstrap.Services; // force host build + migrations
        await SeedCatalogAsync(bootstrap);
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
    public async Task Candidates_are_real_catalog_cards_ranked_closest_first()
    {
        var client = Client();
        var token = await RegisterAsync(client);
        var rowId = await UploadUnmatchedAsync(client, token, "raichu");

        var result = await CandidatesAsync(client, token, rowId);

        // The endpoint echoes the raw name it ranked against.
        result.RawName.ShouldBe("raichu");

        // Both Raichu printings match "raichu"; the exact name ranks above "Alolan Raichu"
        // (an alphabetical sort would have inverted this).
        result.Candidates.Count.ShouldBe(2);
        result.Candidates[0].Name.ShouldBe("Raichu");
        result.Candidates[0].ExternalId.ShouldBe("base1-14");
        result.Candidates[0].HasImage.ShouldBeTrue();
        result.Candidates[0].ImageUrl.ShouldBe("https://img/raichu-large.png");
        result.Candidates[0].SetCode.ShouldBe("BS");
        result.Candidates[1].Name.ShouldBe("Alolan Raichu");

        // Every suggestion is a real catalog id — nothing is invented (#66 guardrail).
        result.Candidates.Select(c => c.ExternalId).ShouldBe(["base1-14", "base1-20"]);
    }

    [SkippableFact]
    public async Task Candidates_tolerate_a_typo_the_exact_matcher_misses()
    {
        var client = Client();
        var token = await RegisterAsync(client);

        // "Charizrd" is a misspelling of "Charizard": no exact/substring hit, but pg_trgm's
        // `%` operator is close enough to still surface it as the top suggestion.
        var rowId = await UploadUnmatchedAsync(client, token, "Charizrd");

        var result = await CandidatesAsync(client, token, rowId);

        result.Candidates.ShouldNotBeEmpty();
        result.Candidates[0].Name.ShouldBe("Charizard");
        result.Candidates[0].ExternalId.ShouldBe("base1-4");
    }

    [SkippableFact]
    public async Task Suggesting_for_someone_elses_row_is_an_indistinguishable_404()
    {
        var client = Client();
        var owner = await RegisterAsync(client);
        var intruder = await RegisterAsync(client);
        var rowId = await UploadUnmatchedAsync(client, owner, "raichu");

        var response = await SendCandidatesAsync(client, intruder, JobIdFor(rowId), rowId);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    [SkippableFact]
    public async Task Candidates_require_a_token()
    {
        var client = Client();

        (await client.GetAsync("/import/jobs/1/rows/1/candidates"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // The upload's job id is the row id's job; tests only ever have one job per user, so we
    // fetch it lazily from the row. Kept simple: the upload helper stashes the last job id.
    private int _lastJobId;

    private int JobIdFor(int rowId)
    {
        _ = rowId;
        return _lastJobId;
    }

    private async Task<int> UploadUnmatchedAsync(HttpClient client, string token, string cardName)
    {
        var job = await UploadAsync(client, token, $"card_name\r\n{cardName}\r\n");
        _lastJobId = job.Id;
        // The fake provider matches nothing, so the single row is Unmatched and listed.
        var list = await ListAsync(client, token, job.Id);
        return list.Rows.Single().Id;
    }

    private static FakeCardDataSource Catalog()
    {
        var source = new FakeCardDataSource();
        source.Sets.Add(new PokemonSetData("base1", "Base", "BS", "1999/01/09"));
        source.CardsBySetId["base1"] =
        [
            Card("base1-4", "Charizard", "4", "https://img/charizard-large.png"),
            Card("base1-5", "Charmeleon", "5", "https://img/charmeleon-large.png"),
            Card("base1-46", "Charmander", "46", "https://img/charmander-large.png"),
            Card("base1-14", "Raichu", "14", "https://img/raichu-large.png"),
            Card("base1-20", "Alolan Raichu", "20", "https://img/alolan-raichu-large.png"),
        ];
        return source;
    }

    private static PokemonCardData Card(string id, string name, string number, string image) =>
        new(id, name, number, "Rare Holo",
            Supertype: "Pokémon", Subtypes: ["Basic"], Hp: "80", Types: ["Fire"],
            Images: new PokemonImageData(image, image));

    private async Task SeedCatalogAsync(CandidatesApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var seeder = new CatalogSeeder(db, Catalog(), NullLogger<CatalogSeeder>.Instance);
        await seeder.SeedAsync(CancellationToken.None);
    }

    private HttpClient Client()
    {
        Skip.If(_dockerUnavailableReason is not null, _dockerUnavailableReason);
        // A fresh host per test gives an isolated rate-limit bucket for the upload endpoint.
        var factory = new CandidatesApiFactory(_postgres!.GetConnectionString());
        _factories.Add(factory);
        return factory.CreateClient();
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
        var request = new HttpRequestMessage(HttpMethod.Get, $"/import/jobs/{jobId}/rows");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<UnmatchedRowsDto>(Json))!;
    }

    private async Task<CandidatesDto> CandidatesAsync(HttpClient client, string token, int rowId)
    {
        var response = await SendCandidatesAsync(client, token, _lastJobId, rowId);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<CandidatesDto>(Json))!;
    }

    private static Task<HttpResponseMessage> SendCandidatesAsync(
        HttpClient client, string token, int jobId, int rowId)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get, $"/import/jobs/{jobId}/rows/{rowId}/candidates");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client.SendAsync(request);
    }

    private sealed record JobDto(int Id, string Status, int RowsTotal, int RowsMatched, int RowsUnmatched);

    private sealed record UnmatchedRowsDto(JsonElement Job, IReadOnlyList<RowDto> Rows);

    private sealed record RowDto(int Id, string RawName);

    private sealed record CandidatesDto(int RowId, string RawName, IReadOnlyList<CandidateDto> Candidates);

    private sealed record CandidateDto(
        string ExternalId, string Name, string? Number, string? Rarity,
        string ImageUrl, bool HasImage, string? SetName, string? SetCode);

    // Boots the real app against the Testcontainers Postgres (migrations on), with the
    // provider client replaced by a fake that matches nothing so every row stays Unmatched.
    private sealed class CandidatesApiFactory(string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Database:ApplyMigrationsOnStartup", "true");
            builder.UseSetting("ConnectionStrings:Default", connectionString);
            builder.UseSetting("Jwt:SigningKey", TestJwt.SigningKey);
            builder.UseSetting("Jwt:Issuer", TestJwt.Issuer);
            builder.UseSetting("Jwt:Audience", TestJwt.Audience);

            // Registering a user now queues a verification email (#69). Tests do not
            // exercise it, so pin the kill switch off: nothing is rendered, nothing
            // is written to a drop directory, and a developer's user-secrets cannot
            // point a test run at a real provider.
            builder.UseSetting("Email:Enabled", "false");
            builder.UseSetting("Catalog:PlaceholderImageUrl", Placeholder);
            builder.UseSetting(AdminSeedExtensions.SeedEmailsKey, "");

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ICardCatalogClient>();
                services.AddSingleton<ICardCatalogClient>(new FakeCardCatalogClient());
            });
        }
    }
}
