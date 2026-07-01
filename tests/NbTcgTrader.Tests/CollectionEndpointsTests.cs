using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NbTcgTrader.Api.Common.Persistence;
using NbTcgTrader.Api.Features.Catalog;
using NbTcgTrader.Api.Features.Collection;
using Shouldly;
using Testcontainers.PostgreSql;

namespace NbTcgTrader.Tests;

// End-to-end coverage of POST /collection/items (BACKLOG #10) against a real Postgres
// via Testcontainers, with the provider client swapped for a fake so no network is
// touched. Covers the AC: authorized (401 without a token), persists (row + catalog
// card land in the database), rejects unknown card (404), returns the created item
// (201 + DTO). Skips cleanly when Docker is unavailable. Each test gets its own host
// (own rate-limit bucket) and user.
[Collection(IntegrationTestCollection.Name)]
public sealed class CollectionEndpointsTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const string Placeholder = "https://api.example.com/assets/card-placeholder.svg";

    private readonly List<CollectionApiFactory> _factories = [];
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
    public async Task Add_card_returns_201_with_item_and_persists_it()
    {
        var catalog = KnownCatalog();
        var factory = CreateFactory(catalog);
        var client = factory.CreateClient();
        var token = await RegisterAsync(client);

        var response = await SendAddAsync(client, token,
            new { cardExternalId = "base1-4", quantity = 2, condition = "LP" });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        response.Headers.Location.ShouldNotBeNull();

        var item = await response.Content.ReadFromJsonAsync<ItemDto>(Json);
        item.ShouldNotBeNull();
        item.Id.ShouldBeGreaterThan(0);
        item.Quantity.ShouldBe(2);
        item.Condition.ShouldBe("LP");
        item.IsForSale.ShouldBeFalse();
        item.Card.ExternalId.ShouldBe("base1-4");
        item.Card.Name.ShouldBe("Charizard");
        item.Card.SetName.ShouldBe("Base");
        item.Card.ImageUrl.ShouldBe("https://img/charizard-large.png");
        item.Card.HasImage.ShouldBeTrue();

        // Durability: the row (and the catalog card it created) are in the database.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var saved = await db.CollectionItems.Include(i => i.Card)
            .SingleAsync(i => i.Id == item.Id);
        saved.Quantity.ShouldBe(2);
        saved.Card.ShouldNotBeNull().ExternalId.ShouldBe("base1-4");
    }

    [SkippableFact]
    public async Task Adding_the_same_card_twice_reuses_the_stored_catalog_card()
    {
        var catalog = KnownCatalog();
        var factory = CreateFactory(catalog);
        var client = factory.CreateClient();
        var token = await RegisterAsync(client);

        var body = new { cardExternalId = "base1-4", quantity = 1, condition = "NM" };
        (await SendAddAsync(client, token, body)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await SendAddAsync(client, token, body)).StatusCode.ShouldBe(HttpStatusCode.Created);

        // The second add resolves the card locally instead of re-fetching the provider,
        // and no duplicate catalog row is created.
        catalog.GetCardRequests.Count.ShouldBe(1);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Cards.CountAsync(c => c.ExternalId == "base1-4")).ShouldBe(1);
        (await db.CollectionItems.CountAsync()).ShouldBe(2);
    }

    [SkippableFact]
    public async Task Add_unknown_card_returns_404_problem_details()
    {
        var factory = CreateFactory(new FakeCardCatalogClient());
        var client = factory.CreateClient();
        var token = await RegisterAsync(client);

        var response = await SendAddAsync(client, token,
            new { cardExternalId = "no-such-card", quantity = 1, condition = "NM" });

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    [SkippableFact]
    public async Task Add_with_invalid_quantity_returns_400_problem_details()
    {
        var factory = CreateFactory(KnownCatalog());
        var client = factory.CreateClient();
        var token = await RegisterAsync(client);

        var response = await SendAddAsync(client, token,
            new { cardExternalId = "base1-4", quantity = 0, condition = "NM" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    [SkippableFact]
    public async Task Add_without_a_token_returns_401()
    {
        var factory = CreateFactory(KnownCatalog());
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/collection/items",
            new { cardExternalId = "base1-4", quantity = 1, condition = "NM" });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [SkippableFact]
    public async Task Binder_returns_only_the_callers_items_with_card_display_data()
    {
        var factory = CreateFactory(KnownCatalog());
        var client = factory.CreateClient();
        var owner = await RegisterAsync(client);
        var other = await RegisterAsync(client);

        (await SendAddAsync(client, owner,
                new { cardExternalId = "base1-4", quantity = 2, condition = "LP" }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        (await SendAddAsync(client, other,
                new { cardExternalId = "base1-8", quantity = 1, condition = "NM" }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        var response = await SendGetAsync(client, owner, "/collection/me");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var page = await response.Content.ReadFromJsonAsync<PageDto>(Json);
        page.ShouldNotBeNull();
        page.TotalCount.ShouldBe(1);

        // Grid-ready: the row carries the card display data, no client-side join.
        var item = page.Items.ShouldHaveSingleItem();
        item.Quantity.ShouldBe(2);
        item.Condition.ShouldBe("LP");
        item.Card.ExternalId.ShouldBe("base1-4");
        item.Card.Name.ShouldBe("Charizard");
        item.Card.SetName.ShouldBe("Base");
        item.Card.ImageUrl.ShouldBe("https://img/charizard-large.png");
    }

    [SkippableFact]
    public async Task Binder_includes_private_items_by_default_and_excludes_on_request()
    {
        var factory = CreateFactory(KnownCatalog());
        var client = factory.CreateClient();
        var token = await RegisterAsync(client);

        (await SendAddAsync(client, token,
                new { cardExternalId = "base1-4", quantity = 1, condition = "NM" }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = await (await SendAddAsync(client, token,
                new { cardExternalId = "base1-8", quantity = 1, condition = "NM" }))
            .Content.ReadFromJsonAsync<ItemDto>(Json);
        created.ShouldNotBeNull();

        // Items can't be flagged private through the API until #12 ships, so mark
        // one directly in the database.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var item = await db.CollectionItems.SingleAsync(i => i.Id == created.Id);
            item.IsPrivate = true;
            await db.SaveChangesAsync();
        }

        var all = await (await SendGetAsync(client, token, "/collection/me"))
            .Content.ReadFromJsonAsync<PageDto>(Json);
        all!.TotalCount.ShouldBe(2);

        var publicOnly = await (await SendGetAsync(
                client, token, "/collection/me?includePrivate=false"))
            .Content.ReadFromJsonAsync<PageDto>(Json);
        publicOnly!.TotalCount.ShouldBe(1);
        publicOnly.Items.ShouldHaveSingleItem().Card.ExternalId.ShouldBe("base1-4");
    }

    [SkippableFact]
    public async Task Binder_pages_results_deterministically()
    {
        var factory = CreateFactory(KnownCatalog());
        var client = factory.CreateClient();
        var token = await RegisterAsync(client);

        var body = new { cardExternalId = "base1-4", quantity = 1, condition = "NM" };
        for (var i = 0; i < 3; i++)
        {
            (await SendAddAsync(client, token, body))
                .StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        var first = await (await SendGetAsync(
                client, token, "/collection/me?page=1&pageSize=2"))
            .Content.ReadFromJsonAsync<PageDto>(Json);
        first!.TotalCount.ShouldBe(3);
        first.Items.Count.ShouldBe(2);

        var second = await (await SendGetAsync(
                client, token, "/collection/me?page=2&pageSize=2"))
            .Content.ReadFromJsonAsync<PageDto>(Json);
        second!.TotalCount.ShouldBe(3);
        var lastItem = second.Items.ShouldHaveSingleItem();

        // No row is repeated or dropped across pages.
        first.Items.Select(i => i.Id).ShouldNotContain(lastItem.Id);
    }

    [SkippableFact]
    public async Task Binder_without_a_token_returns_401()
    {
        var factory = CreateFactory(KnownCatalog());
        var client = factory.CreateClient();

        var response = await client.GetAsync("/collection/me");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [SkippableFact]
    public async Task Binder_with_invalid_page_returns_400_problem_details()
    {
        var factory = CreateFactory(KnownCatalog());
        var client = factory.CreateClient();
        var token = await RegisterAsync(client);

        var response = await SendGetAsync(client, token, "/collection/me?page=0");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    [SkippableFact]
    public async Task Binder_with_too_large_page_returns_400_problem_details()
    {
        var factory = CreateFactory(KnownCatalog());
        var client = factory.CreateClient();
        var token = await RegisterAsync(client);

        var response = await SendGetAsync(client, token,
            $"/collection/me?page={BinderRequestValidator.MaxPage + 1}");

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
        return catalog;
    }

    private CollectionApiFactory CreateFactory(FakeCardCatalogClient catalog)
    {
        Skip.If(_dockerUnavailableReason is not null, _dockerUnavailableReason);

        var factory = new CollectionApiFactory(_postgres!.GetConnectionString(), catalog);
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

    private static Task<HttpResponseMessage> SendAddAsync(
        HttpClient client, string token, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/collection/items")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> SendGetAsync(
        HttpClient client, string token, string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client.SendAsync(request);
    }

    // Local DTOs assert the over-the-wire JSON contract (enums as strings),
    // independent of the API's internal record shapes.
    private sealed record PageDto(
        IReadOnlyList<ItemDto> Items, int Page, int PageSize, int TotalCount);

    private sealed record ItemDto(
        int Id, ItemCardDto Card, int Quantity, string Condition, bool IsForSale,
        decimal? Price, string Currency, bool IsPrivate, string? Notes);

    private sealed record ItemCardDto(
        int Id, string ExternalId, string Name, string? Number, string? Rarity,
        string ImageUrl, bool HasImage, string? SetName);

    // Boots the real app against the Testcontainers Postgres (migrations on), with the
    // provider client replaced by the fake and a throwaway Jwt/placeholder config.
    private sealed class CollectionApiFactory(
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
