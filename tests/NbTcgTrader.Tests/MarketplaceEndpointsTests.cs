using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NbTcgTrader.Api.Features.Catalog;
using NbTcgTrader.Api.Features.Marketplace;
using Shouldly;
using Testcontainers.PostgreSql;

namespace NbTcgTrader.Tests;

// End-to-end coverage of GET /marketplace (BACKLOG #17) against a real Postgres via
// Testcontainers, with the provider client swapped for a fake so no network is touched.
// Covers the ACs: returns only IsForSale && !IsPrivate, filters combine correctly, and
// results page. Browsing is anonymous. Skips cleanly when Docker is unavailable. Each
// test gets its own host (own rate-limit bucket) and users.
[Collection(IntegrationTestCollection.Name)]
public sealed class MarketplaceEndpointsTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const string Placeholder = "https://api.example.com/assets/card-placeholder.svg";

    private readonly List<MarketplaceApiFactory> _factories = [];
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
    public async Task Browse_is_anonymous_and_returns_only_public_for_sale_listings()
    {
        var factory = CreateFactory(KnownCatalog());
        var client = factory.CreateClient();
        var seller = await RegisterAsync(client, city: "Moncton", country: "Canada");

        // A public listing (surfaces), a private-but-for-sale item (hidden), and a
        // not-for-sale item (hidden).
        await ListAsync(client, seller, "base1-4", price: 50m, currency: "CAD");
        await ListAsync(client, seller, "base1-8", price: 30m, currency: "CAD",
            isPrivate: true);
        await AddOnlyAsync(client, seller, "base1-2");

        // No auth header at all: browsing is public.
        var response = await client.GetAsync("/marketplace/");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var page = await response.Content.ReadFromJsonAsync<PageDto>(Json);
        page.ShouldNotBeNull();
        page.TotalCount.ShouldBe(1);

        var listing = page.Items.ShouldHaveSingleItem();
        listing.Price.ShouldBe(50m);
        listing.Currency.ShouldBe("CAD");
        listing.Card.ExternalId.ShouldBe("base1-4");
        listing.Card.Name.ShouldBe("Charizard");
        listing.Card.SetName.ShouldBe("Base");
        listing.Card.GameName.ShouldBe("Pokémon");
        // The browse card carries the seller's public location, not their contact channels.
        listing.Seller.DisplayName.ShouldBe("Ash");
        listing.Seller.City.ShouldBe("Moncton");
        listing.Seller.Country.ShouldBe("Canada");
    }

    [SkippableFact]
    public async Task Browse_never_exposes_seller_contact_channels()
    {
        var factory = CreateFactory(KnownCatalog());
        var client = factory.CreateClient();
        var seller = await RegisterAsync(client, city: "Moncton", country: "Canada");

        // Give the seller contact channels, then confirm browse does not leak them (#18
        // owns the contact reveal, not #17).
        (await SendPutAsync(client, seller, "/auth/me", new
            {
                displayName = "Ash",
                city = "Moncton",
                country = "Canada",
                contactEmail = "ash@example.com",
                discordHandle = "ash#1234",
                instagramHandle = "ash_ketchum",
            }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        await ListAsync(client, seller, "base1-4", price: 50m, currency: "CAD");

        var body = await (await client.GetAsync("/marketplace/")).Content.ReadAsStringAsync();

        body.ShouldNotContain("ash@example.com");
        body.ShouldNotContain("ash#1234");
        body.ShouldNotContain("ash_ketchum");
        body.ShouldNotContain("contactEmail");
        body.ShouldNotContain("discordHandle");
    }

    [SkippableFact]
    public async Task Filters_combine_by_name_price_and_city()
    {
        var factory = CreateFactory(KnownCatalog());
        var client = factory.CreateClient();
        var moncton = await RegisterAsync(client, city: "Moncton", country: "Canada");
        var ipaussu = await RegisterAsync(client, city: "Ipaussu", country: "Brazil");

        await ListAsync(client, moncton, "base1-4", price: 50m, currency: "CAD"); // Charizard
        await ListAsync(client, moncton, "base1-8", price: 200m, currency: "CAD"); // Machamp
        await ListAsync(client, ipaussu, "base1-4", price: 40m, currency: "BRL"); // Charizard

        // Name + city + price range, all AND-combined.
        var page = await BrowseAsync(client,
            "?name=char&city=Moncton&currency=CAD&minPrice=10&maxPrice=100");

        var listing = page.Items.ShouldHaveSingleItem();
        listing.Card.ExternalId.ShouldBe("base1-4");
        listing.Seller.City.ShouldBe("Moncton");
        listing.Price.ShouldBe(50m);
    }

    [SkippableFact]
    public async Task Set_filter_matches_by_name_and_country_filter_narrows_sellers()
    {
        var factory = CreateFactory(KnownCatalog());
        var client = factory.CreateClient();
        var moncton = await RegisterAsync(client, city: "Moncton", country: "Canada");
        var ipaussu = await RegisterAsync(client, city: "Ipaussu", country: "Brazil");

        await ListAsync(client, moncton, "base1-4", price: 50m, currency: "CAD");
        await ListAsync(client, ipaussu, "base1-8", price: 30m, currency: "BRL");

        var brazilOnly = await BrowseAsync(client, "?set=base&country=Brazil");

        var listing = brazilOnly.Items.ShouldHaveSingleItem();
        listing.Card.ExternalId.ShouldBe("base1-8");
        listing.Seller.Country.ShouldBe("Brazil");
    }

    [SkippableFact]
    public async Task Game_filter_matches_slug_case_insensitively_and_excludes_others()
    {
        var factory = CreateFactory(KnownCatalog());
        var client = factory.CreateClient();
        var seller = await RegisterAsync(client, city: "Moncton", country: "Canada");
        await ListAsync(client, seller, "base1-4", price: 50m, currency: "CAD");

        (await BrowseAsync(client, "?game=Pokemon")).TotalCount.ShouldBe(1);
        (await BrowseAsync(client, "?game=magic")).TotalCount.ShouldBe(0);
    }

    [SkippableFact]
    public async Task Results_page_deterministically()
    {
        var factory = CreateFactory(KnownCatalog());
        var client = factory.CreateClient();
        var seller = await RegisterAsync(client, city: "Moncton", country: "Canada");

        await ListAsync(client, seller, "base1-4", price: 10m, currency: "CAD");
        await ListAsync(client, seller, "base1-8", price: 20m, currency: "CAD");
        await ListAsync(client, seller, "base1-2", price: 30m, currency: "CAD");

        var first = await BrowseAsync(client, "?page=1&pageSize=2");
        first.TotalCount.ShouldBe(3);
        first.Items.Count.ShouldBe(2);

        var second = await BrowseAsync(client, "?page=2&pageSize=2");
        second.TotalCount.ShouldBe(3);
        var lastItem = second.Items.ShouldHaveSingleItem();

        // No listing is repeated or dropped across pages.
        first.Items.Select(i => i.Id).ShouldNotContain(lastItem.Id);
    }

    [SkippableFact]
    public async Task Invalid_page_returns_400_problem_details()
    {
        var factory = CreateFactory(KnownCatalog());
        var client = factory.CreateClient();

        var response = await client.GetAsync("/marketplace/?page=0");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    [SkippableFact]
    public async Task Max_price_below_min_price_returns_400_problem_details()
    {
        var factory = CreateFactory(KnownCatalog());
        var client = factory.CreateClient();

        var response = await client.GetAsync("/marketplace/?minPrice=100&maxPrice=10");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    private static FakeCardCatalogClient KnownCatalog()
    {
        var baseSet = new CatalogSet("base1", "Base", "BS", new DateOnly(1999, 1, 9));
        var catalog = new FakeCardCatalogClient();
        catalog.Cards["base1-4"] = new CatalogCard(
            "base1-4", "Charizard", "4", "Rare Holo",
            "https://img/charizard-large.png", null, baseSet);
        catalog.Cards["base1-8"] = new CatalogCard(
            "base1-8", "Machamp", "8", "Rare Holo",
            "https://img/machamp-large.png", null, baseSet);
        catalog.Cards["base1-2"] = new CatalogCard(
            "base1-2", "Blastoise", "2", "Rare Holo",
            "https://img/blastoise-large.png", null, baseSet);
        return catalog;
    }

    private MarketplaceApiFactory CreateFactory(FakeCardCatalogClient catalog)
    {
        Skip.If(_dockerUnavailableReason is not null, _dockerUnavailableReason);

        var factory = new MarketplaceApiFactory(_postgres!.GetConnectionString(), catalog);
        _factories.Add(factory);
        return factory;
    }

    private static async Task<string> RegisterAsync(
        HttpClient client, string city, string country)
    {
        var response = await client.PostAsJsonAsync("/auth/register", new
        {
            email = $"user-{Guid.NewGuid():N}@example.com",
            password = "Sup3rSecret!Pwd",
            displayName = "Ash",
            city,
            country,
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var auth = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        return auth.GetProperty("accessToken").GetString()!;
    }

    // Adds a card to the caller's binder without listing it for sale, returning its id.
    private static async Task<int> AddOnlyAsync(
        HttpClient client, string token, string cardExternalId)
    {
        var response = await SendAsync(client, token, HttpMethod.Post, "/collection/items",
            new { cardExternalId, quantity = 1, condition = "NM" });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var item = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        return item.GetProperty("id").GetInt32();
    }

    // Adds a card and flags it for sale, so it becomes a marketplace listing.
    private static async Task ListAsync(
        HttpClient client, string token, string cardExternalId,
        decimal price, string currency, bool isPrivate = false)
    {
        var id = await AddOnlyAsync(client, token, cardExternalId);
        var response = await SendAsync(client, token, HttpMethod.Put,
            $"/collection/items/{id}", new
            {
                quantity = 1,
                condition = "NM",
                isForSale = true,
                price,
                currency,
                isPrivate,
            });
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private async Task<PageDto> BrowseAsync(HttpClient client, string queryString)
    {
        var response = await client.GetAsync($"/marketplace/{queryString}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var page = await response.Content.ReadFromJsonAsync<PageDto>(Json);
        return page.ShouldNotBeNull();
    }

    private static Task<HttpResponseMessage> SendPutAsync(
        HttpClient client, string token, string url, object body) =>
        SendAsync(client, token, HttpMethod.Put, url, body);

    private static Task<HttpResponseMessage> SendAsync(
        HttpClient client, string token, HttpMethod method, string url, object body)
    {
        var request = new HttpRequestMessage(method, url)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client.SendAsync(request);
    }

    // Local DTOs assert the over-the-wire JSON contract (enums as strings),
    // independent of the API's internal record shapes.
    private sealed record PageDto(
        IReadOnlyList<ListingDto> Items, int Page, int PageSize, int TotalCount);

    private sealed record ListingDto(
        int Id, ListingCardDto Card, int Quantity, string Condition,
        decimal? Price, string Currency, string? Notes, SellerDto Seller);

    private sealed record ListingCardDto(
        int Id, string ExternalId, string Name, string? Number, string? Rarity,
        string ImageUrl, bool HasImage, string? SetName, string GameName);

    private sealed record SellerDto(string DisplayName, string? City, string? Country);

    // Boots the real app against the Testcontainers Postgres (migrations on), with the
    // provider client replaced by the fake and a throwaway Jwt/placeholder config.
    private sealed class MarketplaceApiFactory(
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
            builder.UseSetting("Catalog:PlaceholderImageUrl", Placeholder);

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ICardCatalogClient>();
                services.AddSingleton<ICardCatalogClient>(catalog);
            });
        }
    }
}
