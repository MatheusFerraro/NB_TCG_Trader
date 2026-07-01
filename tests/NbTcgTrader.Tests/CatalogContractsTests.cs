using NbTcgTrader.Api.Features.Catalog;
using Shouldly;

namespace NbTcgTrader.Tests;

// The AC-critical mapping (BACKLOG #9): the response contract must never expose a null
// image. When the provider has an image it passes through unchanged (HasImage=true);
// when it doesn't, the configured placeholder is substituted (HasImage=false). The card
// is never dropped. Fast, no DI/network.
public class CatalogContractsTests
{
    private const string Placeholder = "https://api.example.com/assets/card-placeholder.svg";

    private static CatalogCard CardWith(string? imageUrl, CatalogSet? set = null) =>
        new("base1-4", "Charizard", "4", "Rare Holo", imageUrl, Metadata: null, set);

    [Fact]
    public void From_keeps_provider_image_and_flags_has_image()
    {
        var card = CardWith("https://images.pokemontcg.io/base1/4_hires.png");

        var response = CatalogCardResponse.From(card, Placeholder);

        response.ImageUrl.ShouldBe("https://images.pokemontcg.io/base1/4_hires.png");
        response.HasImage.ShouldBeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void From_substitutes_placeholder_when_image_missing(string? imageUrl)
    {
        var card = CardWith(imageUrl);

        var response = CatalogCardResponse.From(card, Placeholder);

        response.ImageUrl.ShouldBe(Placeholder);
        response.HasImage.ShouldBeFalse();
        // The rest of the card is still mapped — the card is never dropped.
        response.ExternalId.ShouldBe("base1-4");
        response.Name.ShouldBe("Charizard");
    }

    [Fact]
    public void From_maps_set_when_present_and_null_when_absent()
    {
        var set = new CatalogSet("base1", "Base", "BS", new DateOnly(1999, 1, 9));

        var withSet = CatalogCardResponse.From(CardWith("https://img/large.png", set), Placeholder);
        var mapped = withSet.Set.ShouldNotBeNull();
        mapped.ExternalId.ShouldBe("base1");
        mapped.Name.ShouldBe("Base");
        mapped.Code.ShouldBe("BS");
        mapped.ReleaseDate.ShouldBe(new DateOnly(1999, 1, 9));

        CatalogCardResponse.From(CardWith("https://img/large.png"), Placeholder)
            .Set.ShouldBeNull();
    }
}
