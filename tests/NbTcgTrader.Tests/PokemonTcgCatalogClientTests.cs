using System.Net;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NbTcgTrader.Api.Common.Extensions;
using NbTcgTrader.Api.Features.Catalog;
using Shouldly;

namespace NbTcgTrader.Tests;

// Unit tests for the pokemontcg.io catalog client (CLAUDE.md §8/§11, BACKLOG #8).
// No network or database: a FakeHttpMessageHandler serves canned JSON and the client
// is constructed directly, so these stay fast and isolated.
public class PokemonTcgCatalogClientTests
{
    private const string BaseUrl = "https://api.pokemontcg.io/v2/";

    // A representative pokemontcg.io /cards page with one fully-populated card.
    private const string CharizardPage = """
        {
          "data": [
            {
              "id": "base1-4",
              "name": "Charizard",
              "number": "4",
              "rarity": "Rare Holo",
              "supertype": "Pokémon",
              "subtypes": ["Stage 2"],
              "hp": "120",
              "types": ["Fire"],
              "set": { "id": "base1", "name": "Base", "ptcgoCode": "BS", "releaseDate": "1999/01/09" },
              "images": { "small": "https://images.pokemontcg.io/base1/4.png",
                          "large": "https://images.pokemontcg.io/base1/4_hires.png" }
            }
          ],
          "page": 1,
          "pageSize": 25,
          "count": 1,
          "totalCount": 1
        }
        """;

    private const string EmptyPage = """
        { "data": [], "page": 1, "pageSize": 25, "count": 0, "totalCount": 0 }
        """;

    private static PokemonTcgCatalogClient CreateClient(
        FakeHttpMessageHandler handler,
        CardCatalogOptions? options = null)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri(BaseUrl) };
        var cache = new MemoryCache(new MemoryCacheOptions());
        return new PokemonTcgCatalogClient(
            http,
            cache,
            Options.Create(options ?? new CardCatalogOptions()),
            NullLogger<PokemonTcgCatalogClient>.Instance);
    }

    [Fact]
    public async Task SearchCards_maps_provider_fields_to_result_records()
    {
        var client = CreateClient(FakeHttpMessageHandler.Json(CharizardPage));

        var page = await client.SearchCardsAsync(new CatalogSearchQuery(Name: "Charizard"), default);

        page.TotalCount.ShouldBe(1);
        var card = page.Items.ShouldHaveSingleItem();
        card.ExternalId.ShouldBe("base1-4");
        card.Name.ShouldBe("Charizard");
        card.Number.ShouldBe("4");
        card.Rarity.ShouldBe("Rare Holo");
        // Prefers the high-res image over the small one.
        card.ImageUrl.ShouldBe("https://images.pokemontcg.io/base1/4_hires.png");
        card.Metadata.ShouldNotBeNull();
        card.Metadata.ShouldContain("supertype");
        card.Metadata.ShouldContain("Fire");

        var set = card.Set.ShouldNotBeNull();
        set.ExternalId.ShouldBe("base1");
        set.Name.ShouldBe("Base");
        set.Code.ShouldBe("BS"); // ptcgoCode preferred over the raw id
        set.ReleaseDate.ShouldBe(new DateOnly(1999, 1, 9));
    }

    [Fact]
    public async Task SearchCards_builds_lucene_query_from_filters()
    {
        var handler = FakeHttpMessageHandler.Json(EmptyPage);
        var client = CreateClient(handler);

        await client.SearchCardsAsync(
            new CatalogSearchQuery(Name: "Charizard", Set: "base1", Number: "4", Page: 2, PageSize: 10),
            default);

        var query = Uri.UnescapeDataString(handler.LastRequestUri!.Query);
        query.ShouldContain("name:\"Charizard\"");
        query.ShouldContain("set.id:\"base1\"");
        query.ShouldContain("set.name:\"base1\"");
        query.ShouldContain("number:\"4\"");
        query.ShouldContain("page=2");
        query.ShouldContain("pageSize=10");
    }

    [Fact]
    public async Task SearchCards_caches_identical_queries()
    {
        var handler = FakeHttpMessageHandler.Json(CharizardPage);
        var client = CreateClient(handler);
        var query = new CatalogSearchQuery(Name: "Charizard");

        await client.SearchCardsAsync(query, default);
        await client.SearchCardsAsync(query, default);

        handler.CallCount.ShouldBe(1);
    }

    [Fact]
    public async Task SearchCards_empty_result_returns_empty_page()
    {
        var client = CreateClient(FakeHttpMessageHandler.Json(EmptyPage));

        var page = await client.SearchCardsAsync(new CatalogSearchQuery(Name: "Nonexistent"), default);

        page.Items.ShouldBeEmpty();
        page.TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task GetCard_returns_null_when_provider_responds_404()
    {
        var client = CreateClient(FakeHttpMessageHandler.Json("{}", HttpStatusCode.NotFound));

        var card = await client.GetCardAsync("does-not-exist", default);

        card.ShouldBeNull();
    }

    [Fact]
    public async Task GetCard_maps_single_card_envelope()
    {
        const string singleCard = """
            { "data": { "id": "base1-4", "name": "Charizard", "number": "4",
                        "set": { "id": "base1", "name": "Base" } } }
            """;
        var client = CreateClient(FakeHttpMessageHandler.Json(singleCard));

        var card = await client.GetCardAsync("base1-4", default);

        card.ShouldNotBeNull();
        card.ExternalId.ShouldBe("base1-4");
        card.Set!.Code.ShouldBe("base1"); // falls back to the id when no ptcgoCode
    }

    [Fact]
    public void Mapping_projects_result_records_onto_domain_entities()
    {
        var set = new CatalogSet("base1", "Base", "BS", new DateOnly(1999, 1, 9));
        var card = new CatalogCard("base1-4", "Charizard", "4", "Rare Holo",
            "https://img/large.png", "{\"hp\":\"120\"}", set);

        var entity = card.ToCard(gameId: 7, cardSetId: 3);
        entity.GameId.ShouldBe(7);
        entity.CardSetId.ShouldBe(3);
        entity.ExternalId.ShouldBe("base1-4");
        entity.Name.ShouldBe("Charizard");
        entity.ImageUrl.ShouldBe("https://img/large.png");
        entity.Metadata.ShouldBe("{\"hp\":\"120\"}");

        var setEntity = set.ToCardSet(gameId: 7);
        setEntity.GameId.ShouldBe(7);
        setEntity.Code.ShouldBe("BS");
        setEntity.ExternalId.ShouldBe("base1");
        setEntity.ReleaseDate.ShouldBe(new DateOnly(1999, 1, 9));
    }

    [Theory]
    [InlineData("secret-key", true)]
    [InlineData(null, false)]
    public async Task Registration_sends_api_key_header_only_when_configured(string? key, bool expectHeader)
    {
        var handler = FakeHttpMessageHandler.Json(EmptyPage);

        var settings = new Dictionary<string, string?>
        {
            ["CardApi:BaseUrl"] = "https://example.test/v2/",
        };
        if (key is not null)
        {
            settings["CardApi:Key"] = key;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApiCardCatalog(configuration);
        // Swap the network for the fake handler while keeping the real DI wiring
        // (base address, API-key header, resilience) under test.
        services.AddHttpClient<ICardCatalogClient, PokemonTcgCatalogClient>()
            .ConfigurePrimaryHttpMessageHandler(() => handler);

        await using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<ICardCatalogClient>();

        await client.SearchCardsAsync(new CatalogSearchQuery(Name: "Pikachu"), default);

        handler.Requests[0].Headers.Contains("X-Api-Key").ShouldBe(expectHeader);
    }
}
