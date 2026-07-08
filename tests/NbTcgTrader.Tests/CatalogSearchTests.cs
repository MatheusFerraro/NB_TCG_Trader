using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NbTcgTrader.Api.Common.Extensions;
using NbTcgTrader.Api.Common.Persistence;
using NbTcgTrader.Api.Features.Catalog.Seeding;
using Shouldly;
using Testcontainers.PostgreSql;

namespace NbTcgTrader.Tests;

// End-to-end coverage of the local catalog (BACKLOG #72) against a real Postgres via
// Testcontainers: the seeder ingests a small dataset, then GET /catalog/cards is served
// from Postgres over the wire. Covers the ACs — idempotent re-seed (zero duplicates +
// updates applied), fuzzy/partial name search over the pg_trgm index, never-null image,
// and correct paging/totalCount. Skips cleanly when Docker is unavailable.
[Collection(IntegrationTestCollection.Name)]
[Trait("Category", "Integration")]
public sealed class CatalogSearchTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const string Placeholder = "https://api.example.com/assets/card-placeholder.svg";

    private PostgreSqlContainer? _postgres;
    private string? _dockerUnavailableReason;
    private CatalogApiFactory? _factory;

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

        // Boot the app (migrations on: creates the schema + the pg_trgm index), then
        // seed the canonical dataset once so the read-only search tests share it.
        _factory = new CatalogApiFactory(_postgres.GetConnectionString());
        await SeedAsync(Dataset());
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        if (_postgres is not null)
        {
            await _postgres.DisposeAsync();
        }
    }

    [SkippableFact]
    public async Task Partial_name_search_finds_card_via_trigram_index()
    {
        var client = Client();

        var page = await SearchAsync(client, "?query=chariz");

        // "chariz" is a partial of "Charizard" only (not Charmander/Charmeleon).
        var card = page.Items.ShouldHaveSingleItem();
        card.Name.ShouldBe("Charizard");
        card.ExternalId.ShouldBe("base1-4");
        card.HasImage.ShouldBeTrue();
        card.ImageUrl.ShouldBe("https://img/charizard-large.png");
        card.Set.ShouldNotBeNull();
        card.Set.Name.ShouldBe("Base");
        card.Set.Code.ShouldBe("BS");
    }

    [SkippableFact]
    public async Task Missing_image_returns_placeholder_never_null()
    {
        var client = Client();

        var card = (await SearchAsync(client, "?query=machamp")).Items.ShouldHaveSingleItem();

        card.Name.ShouldBe("Machamp");
        card.ImageUrl.ShouldBe(Placeholder);
        card.HasImage.ShouldBeFalse();
    }

    [SkippableFact]
    public async Task Broad_partial_pages_deterministically_with_correct_total_count()
    {
        var client = Client();

        // "char" matches Charizard, Charmeleon, Charmander (3), ordered by name.
        var first = await SearchAsync(client, "?query=char&page=1&pageSize=2");
        first.TotalCount.ShouldBe(3);
        first.Items.Count.ShouldBe(2);
        first.Items[0].Name.ShouldBe("Charizard");
        first.Items[1].Name.ShouldBe("Charmander");

        var second = await SearchAsync(client, "?query=char&page=2&pageSize=2");
        second.TotalCount.ShouldBe(3);
        var last = second.Items.ShouldHaveSingleItem();
        last.Name.ShouldBe("Charmeleon");

        // No card repeats across pages.
        first.Items.Select(i => i.ExternalId).ShouldNotContain(last.ExternalId);
    }

    [SkippableFact]
    public async Task Reseeding_produces_no_duplicates_and_applies_updates()
    {
        // Re-run the seeder with one card's rarity changed. Idempotent upsert keyed on
        // ExternalId: same row count, and the changed field is applied.
        var mutated = Dataset();
        var cards = mutated.CardsBySetId["base1"];
        var index = cards.FindIndex(c => c.Id == "base1-46");
        cards[index] = cards[index] with { Rarity = "Ultra Rare" };

        var before = await CardCountAsync();
        var result = await SeedAsync(mutated);
        var after = await CardCountAsync();

        after.ShouldBe(before); // zero duplicates
        result.CardsInserted.ShouldBe(0); // every card already existed
        result.CardsUpdated.ShouldBe(before);
        result.SetsInserted.ShouldBe(0);

        var card = (await SearchAsync(Client(), "?query=charmander")).Items.ShouldHaveSingleItem();
        card.Rarity.ShouldBe("Ultra Rare");
    }

    private static FakeCardDataSource Dataset()
    {
        var source = new FakeCardDataSource();
        source.Sets.Add(new PokemonSetData("base1", "Base", "BS", "1999/01/09"));
        source.CardsBySetId["base1"] =
        [
            Card("base1-4", "Charizard", "4", "https://img/charizard-large.png"),
            Card("base1-5", "Charmeleon", "5", "https://img/charmeleon-large.png"),
            Card("base1-46", "Charmander", "46", "https://img/charmander-large.png"),
            Card("base1-8", "Machamp", "8", image: null),
        ];
        return source;
    }

    private static PokemonCardData Card(string id, string name, string number, string? image) =>
        new(id, name, number, "Rare Holo",
            Supertype: "Pokémon", Subtypes: ["Basic"], Hp: "80", Types: ["Fire"],
            Images: image is null ? null : new PokemonImageData(image, image));

    private async Task<CatalogSeedResult> SeedAsync(FakeCardDataSource source)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var seeder = new CatalogSeeder(db, source, NullLogger<CatalogSeeder>.Instance);
        return await seeder.SeedAsync(CancellationToken.None);
    }

    private async Task<int> CardCountAsync()
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Cards.CountAsync();
    }

    private HttpClient Client()
    {
        Skip.If(_dockerUnavailableReason is not null, _dockerUnavailableReason);
        return _factory!.CreateClient();
    }

    private static async Task<PageDto> SearchAsync(HttpClient client, string queryString)
    {
        var response = await client.GetAsync($"/catalog/cards{queryString}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<PageDto>(Json)).ShouldNotBeNull();
    }

    private sealed record PageDto(IReadOnlyList<CardDto> Items, int Page, int PageSize, int TotalCount);

    private sealed record CardDto(
        string ExternalId, string Name, string? Number, string? Rarity,
        string ImageUrl, bool HasImage, SetDto? Set);

    private sealed record SetDto(string ExternalId, string Name, string Code, DateOnly? ReleaseDate);

    // Boots the real app against the Testcontainers Postgres (migrations on) with a
    // throwaway Jwt/placeholder config. Search reads only from the database.
    private sealed class CatalogApiFactory(string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Database:ApplyMigrationsOnStartup", "true");
            builder.UseSetting("ConnectionStrings:Default", connectionString);
            builder.UseSetting("Jwt:SigningKey", TestJwt.SigningKey);
            builder.UseSetting("Jwt:Issuer", TestJwt.Issuer);
            builder.UseSetting("Jwt:Audience", TestJwt.Audience);
            builder.UseSetting("Catalog:PlaceholderImageUrl", Placeholder);
            builder.UseSetting(AdminSeedExtensions.SeedEmailsKey, "");
        }
    }
}
