using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using NbTcgTrader.Api.Features.Catalog;
using Shouldly;

namespace NbTcgTrader.Tests;

// The handler maps the request onto the client's query, preserves the provider's paging
// metadata, and projects results (with placeholder fallback) into display DTOs (BACKLOG #9).
public class SearchCardsHandlerTests
{
    private const string Placeholder = "https://api.example.com/assets/card-placeholder.svg";

    private static SearchCardsHandler CreateHandler(FakeCardCatalogClient catalog) =>
        new(catalog, Options.Create(new CatalogOptions { PlaceholderImageUrl = Placeholder }));

    private static CatalogPage<CatalogCardResponse> Body(IResult result)
    {
        var ok = result.ShouldBeOfType<Ok<CatalogPage<CatalogCardResponse>>>();
        return ok.Value.ShouldNotBeNull();
    }

    [Fact]
    public async Task Maps_request_filters_onto_catalog_query()
    {
        var catalog = new FakeCardCatalogClient();
        var handler = CreateHandler(catalog);

        await handler.HandleAsync(
            new CatalogSearchRequest(Query: "Charizard", Set: "base1", Number: "4", Page: 2, PageSize: 10),
            CancellationToken.None);

        var query = catalog.LastQuery.ShouldNotBeNull();
        query.Name.ShouldBe("Charizard"); // the public "query" param feeds the name filter
        query.Set.ShouldBe("base1");
        query.Number.ShouldBe("4");
        query.Page.ShouldBe(2);
        query.PageSize.ShouldBe(10);
    }

    [Fact]
    public async Task Preserves_provider_paging_metadata()
    {
        var catalog = new FakeCardCatalogClient
        {
            NextPage = new CatalogPage<CatalogCard>(
                new[] { new CatalogCard("base1-4", "Charizard", "4", "Rare Holo",
                    "https://img/large.png", null, null) },
                Page: 3, PageSize: 15, TotalCount: 42),
        };
        var handler = CreateHandler(catalog);

        var result = await handler.HandleAsync(
            new CatalogSearchRequest(Query: "Charizard", Page: 3, PageSize: 15), CancellationToken.None);

        var page = Body(result);
        page.Page.ShouldBe(3);
        page.PageSize.ShouldBe(15);
        page.TotalCount.ShouldBe(42);
        var card = page.Items.ShouldHaveSingleItem();
        card.Name.ShouldBe("Charizard");
        card.HasImage.ShouldBeTrue();
    }

    [Fact]
    public async Task Substitutes_placeholder_for_cards_without_an_image()
    {
        var catalog = new FakeCardCatalogClient
        {
            NextPage = new CatalogPage<CatalogCard>(
                new[] { new CatalogCard("base1-8", "Machamp", "8", "Rare Holo",
                    ImageUrl: null, Metadata: null, Set: null) },
                Page: 1, PageSize: 25, TotalCount: 1),
        };
        var handler = CreateHandler(catalog);

        var result = await handler.HandleAsync(
            new CatalogSearchRequest(Query: "Machamp"), CancellationToken.None);

        var card = Body(result).Items.ShouldHaveSingleItem();
        card.ImageUrl.ShouldBe(Placeholder);
        card.HasImage.ShouldBeFalse();
    }
}
