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

// End-to-end coverage of the /collection endpoints (BACKLOG #10/#11/#12) against a
// real Postgres via Testcontainers, with the provider client swapped for a fake so no
// network is touched. Covers the ACs: authorized (401 without a token), owner-only
// (someone else's item id behaves like a missing one), persists, validates, and maps
// failures to ProblemDetails. Skips cleanly when Docker is unavailable. Each test gets
// its own host (own rate-limit bucket) and user.
[Collection(IntegrationTestCollection.Name)]
[Trait("Category", "Integration")]
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

        (await SendPutAsync(client, token, created.Id, new
            {
                quantity = 1,
                condition = "NM",
                isForSale = false,
                currency = "CAD",
                isPrivate = true,
            }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

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
    public async Task Update_item_persists_changes_and_returns_the_updated_dto()
    {
        var factory = CreateFactory(KnownCatalog());
        var client = factory.CreateClient();
        var token = await RegisterAsync(client);

        var created = await (await SendAddAsync(client, token,
                new { cardExternalId = "base1-4", quantity = 1, condition = "NM" }))
            .Content.ReadFromJsonAsync<ItemDto>(Json);
        created.ShouldNotBeNull();

        var response = await SendPutAsync(client, token, created.Id, new
        {
            quantity = 3,
            condition = "MP",
            isForSale = true,
            price = 49.99,
            currency = "BRL",
            isPrivate = false,
            notes = "Shadowless",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = await response.Content.ReadFromJsonAsync<ItemDto>(Json);
        updated.ShouldNotBeNull();
        updated.Id.ShouldBe(created.Id);
        updated.Quantity.ShouldBe(3);
        updated.Condition.ShouldBe("MP");
        updated.IsForSale.ShouldBeTrue();
        updated.Price.ShouldBe(49.99m);
        updated.Currency.ShouldBe("BRL");
        updated.Notes.ShouldBe("Shadowless");
        // The card the row points at never changes on update.
        updated.Card.ExternalId.ShouldBe("base1-4");

        // Durability: the changes are in the database.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var saved = await db.CollectionItems.SingleAsync(i => i.Id == created.Id);
        saved.Quantity.ShouldBe(3);
        saved.IsForSale.ShouldBeTrue();
        saved.Price.ShouldBe(49.99m);
        saved.UpdatedAt.ShouldBeGreaterThan(saved.CreatedAt);
    }

    [SkippableFact]
    public async Task Update_for_sale_without_price_returns_400_problem_details()
    {
        var factory = CreateFactory(KnownCatalog());
        var client = factory.CreateClient();
        var token = await RegisterAsync(client);

        var created = await (await SendAddAsync(client, token,
                new { cardExternalId = "base1-4", quantity = 1, condition = "NM" }))
            .Content.ReadFromJsonAsync<ItemDto>(Json);
        created.ShouldNotBeNull();

        var response = await SendPutAsync(client, token, created.Id, new
        {
            quantity = 1,
            condition = "NM",
            isForSale = true,
            currency = "CAD",
            isPrivate = false,
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    [SkippableFact]
    public async Task Update_someone_elses_item_returns_404_and_changes_nothing()
    {
        var factory = CreateFactory(KnownCatalog());
        var client = factory.CreateClient();
        var owner = await RegisterAsync(client);
        var intruder = await RegisterAsync(client);

        var created = await (await SendAddAsync(client, owner,
                new { cardExternalId = "base1-4", quantity = 2, condition = "LP" }))
            .Content.ReadFromJsonAsync<ItemDto>(Json);
        created.ShouldNotBeNull();

        var response = await SendPutAsync(client, intruder, created.Id, new
        {
            quantity = 1,
            condition = "NM",
            isForSale = false,
            currency = "CAD",
            isPrivate = false,
        });

        // Owner-only: someone else's item is indistinguishable from a missing one.
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var saved = await db.CollectionItems.SingleAsync(i => i.Id == created.Id);
        saved.Quantity.ShouldBe(2);
    }

    [SkippableFact]
    public async Task Update_without_a_token_returns_401()
    {
        var factory = CreateFactory(KnownCatalog());
        var client = factory.CreateClient();

        var response = await client.PutAsJsonAsync("/collection/items/1", new
        {
            quantity = 1,
            condition = "NM",
            isForSale = false,
            currency = "CAD",
            isPrivate = false,
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [SkippableFact]
    public async Task Delete_item_returns_204_and_removes_the_row()
    {
        var factory = CreateFactory(KnownCatalog());
        var client = factory.CreateClient();
        var token = await RegisterAsync(client);

        var created = await (await SendAddAsync(client, token,
                new { cardExternalId = "base1-4", quantity = 1, condition = "NM" }))
            .Content.ReadFromJsonAsync<ItemDto>(Json);
        created.ShouldNotBeNull();

        var response = await SendDeleteAsync(client, token, created.Id);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.CollectionItems.AnyAsync(i => i.Id == created.Id)).ShouldBeFalse();

        // Deleting the same item again is a 404, not a silent no-op.
        (await SendDeleteAsync(client, token, created.Id))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [SkippableFact]
    public async Task Delete_someone_elses_item_returns_404_and_keeps_the_row()
    {
        var factory = CreateFactory(KnownCatalog());
        var client = factory.CreateClient();
        var owner = await RegisterAsync(client);
        var intruder = await RegisterAsync(client);

        var created = await (await SendAddAsync(client, owner,
                new { cardExternalId = "base1-4", quantity = 1, condition = "NM" }))
            .Content.ReadFromJsonAsync<ItemDto>(Json);
        created.ShouldNotBeNull();

        var response = await SendDeleteAsync(client, intruder, created.Id);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.CollectionItems.AnyAsync(i => i.Id == created.Id)).ShouldBeTrue();
    }

    [SkippableFact]
    public async Task Delete_without_a_token_returns_401()
    {
        var factory = CreateFactory(KnownCatalog());
        var client = factory.CreateClient();

        var response = await client.DeleteAsync("/collection/items/1");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
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

    private static Task<HttpResponseMessage> SendPutAsync(
        HttpClient client, string token, int id, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, $"/collection/items/{id}")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> SendDeleteAsync(
        HttpClient client, string token, int id)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, $"/collection/items/{id}");
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

            // Registering a user now queues a verification email (#69). Tests do not
            // exercise it, so pin the kill switch off: nothing is rendered, nothing
            // is written to a drop directory, and a developer's user-secrets cannot
            // point a test run at a real provider.
            builder.UseSetting("Email:Enabled", "false");
            builder.UseSetting("Catalog:PlaceholderImageUrl", Placeholder);

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ICardCatalogClient>();
                services.AddSingleton<ICardCatalogClient>(catalog);
            });
        }
    }
}
