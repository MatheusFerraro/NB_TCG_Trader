using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NbTcgTrader.Api.Features.Catalog;
using Shouldly;

namespace NbTcgTrader.Tests;

// Over-the-wire coverage of GET /catalog/cards (BACKLOG #9). The real provider client is
// swapped for a fake, so no network/Docker/DB is needed — the endpoint is anonymous and
// touches no database. Asserts the query-string binding, the JSON contract, the never-null
// image guarantee, and validation.
public sealed class CatalogEndpointsTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const string Placeholder = "https://api.example.com/assets/card-placeholder.svg";

    [Fact]
    public async Task Search_returns_200_with_paged_results_and_binds_query_params()
    {
        var catalog = new FakeCardCatalogClient
        {
            NextPage = new CatalogPage<CatalogCard>(
                new[] { new CatalogCard("base1-4", "Charizard", "4", "Rare Holo",
                    "https://img/large.png", null,
                    new CatalogSet("base1", "Base", "BS", new DateOnly(1999, 1, 9))) },
                Page: 1, PageSize: 25, TotalCount: 1),
        };
        using var factory = CreateFactory(catalog);
        var client = factory.CreateClient();

        var response = await client.GetAsync("/catalog/cards?query=Charizard&set=base1&number=4");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        // The public "query" param must feed the client's name filter.
        var query = catalog.LastQuery.ShouldNotBeNull();
        query.Name.ShouldBe("Charizard");
        query.Set.ShouldBe("base1");
        query.Number.ShouldBe("4");

        var page = await response.Content.ReadFromJsonAsync<PageDto>(Json);
        page.ShouldNotBeNull();
        page.TotalCount.ShouldBe(1);
        var card = page.Items.ShouldHaveSingleItem();
        card.ExternalId.ShouldBe("base1-4");
        card.ImageUrl.ShouldBe("https://img/large.png");
        card.HasImage.ShouldBeTrue();
        card.Set.ShouldNotBeNull();
        card.Set.Code.ShouldBe("BS");
    }

    [Fact]
    public async Task Search_never_returns_null_image_uses_placeholder()
    {
        var catalog = new FakeCardCatalogClient
        {
            NextPage = new CatalogPage<CatalogCard>(
                new[] { new CatalogCard("base1-8", "Machamp", "8", "Rare Holo",
                    ImageUrl: null, Metadata: null, Set: null) },
                Page: 1, PageSize: 25, TotalCount: 1),
        };
        using var factory = CreateFactory(catalog);
        var client = factory.CreateClient();

        var response = await client.GetAsync("/catalog/cards?query=Machamp");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var card = (await response.Content.ReadFromJsonAsync<PageDto>(Json))!.Items.ShouldHaveSingleItem();
        card.ImageUrl.ShouldBe(Placeholder);
        card.HasImage.ShouldBeFalse();
    }

    [Fact]
    public async Task Search_with_invalid_page_returns_400_problem_details()
    {
        using var factory = CreateFactory(new FakeCardCatalogClient());
        var client = factory.CreateClient();

        var response = await client.GetAsync("/catalog/cards?page=0");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    private static WebApplicationFactory<Program> CreateFactory(FakeCardCatalogClient catalog) =>
        new CatalogApiFactory(catalog);

    private sealed record PageDto(IReadOnlyList<CardDto> Items, int Page, int PageSize, int TotalCount);

    private sealed record CardDto(
        string ExternalId, string Name, string? Number, string? Rarity,
        string ImageUrl, bool HasImage, SetDto? Set);

    private sealed record SetDto(string ExternalId, string Name, string Code, DateOnly? ReleaseDate);

    // Boots the real app with the provider client replaced by the fake. No database is
    // opened (the endpoint is anonymous), so migrations stay off and a throwaway
    // connection string satisfies the fail-fast startup guard.
    private sealed class CatalogApiFactory(FakeCardCatalogClient catalog) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Database:ApplyMigrationsOnStartup", "false");
            builder.UseSetting(
                "ConnectionStrings:Default",
                "Host=localhost;Port=5432;Database=nbtcg_test_unused;Username=test;Password=test");
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
