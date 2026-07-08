using System.Text.Json;
using NbTcgTrader.Api.Features.Catalog.Seeding;
using Shouldly;

namespace NbTcgTrader.Tests;

// Pure mapping coverage for the catalog seeder (BACKLOG #72): no database, no network.
// Asserts the domain projection, the set-code fallback, the large-over-small image
// preference, and the trimmed metadata contract.
public sealed class CatalogSeedMappingTests
{
    [Fact]
    public void Set_maps_fields_and_parses_release_date()
    {
        var source = new PokemonSetData("base1", "Base", "BS", "1999/01/09");

        var set = source.ToCardSet(gameId: 7);

        set.GameId.ShouldBe(7);
        set.ExternalId.ShouldBe("base1");
        set.Name.ShouldBe("Base");
        set.Code.ShouldBe("BS");
        set.ReleaseDate.ShouldBe(new DateOnly(1999, 1, 9));
    }

    [Fact]
    public void Set_code_falls_back_to_id_when_ptcgo_code_absent()
    {
        var source = new PokemonSetData("swsh1", "Sword & Shield", PtcgoCode: null, ReleaseDate: null);

        var set = source.ToCardSet(gameId: 1);

        set.Code.ShouldBe("swsh1");
        set.ReleaseDate.ShouldBeNull();
    }

    [Fact]
    public void Card_maps_fields_and_prefers_large_image()
    {
        var source = new PokemonCardData(
            "base1-4", "Charizard", "4", "Rare Holo",
            Supertype: "Pokémon", Subtypes: ["Stage 2"], Hp: "120", Types: ["Fire"],
            Images: new PokemonImageData("https://img/small.png", "https://img/large.png"));

        var card = source.ToCard(gameId: 7, cardSetId: 42);

        card.GameId.ShouldBe(7);
        card.CardSetId.ShouldBe(42);
        card.ExternalId.ShouldBe("base1-4");
        card.Name.ShouldBe("Charizard");
        card.Number.ShouldBe("4");
        card.Rarity.ShouldBe("Rare Holo");
        card.ImageUrl.ShouldBe("https://img/large.png");
    }

    [Fact]
    public void Card_image_falls_back_to_small_when_large_absent()
    {
        var source = new PokemonCardData(
            "base1-4", "Charizard", "4", "Rare Holo",
            Supertype: null, Subtypes: null, Hp: null, Types: null,
            Images: new PokemonImageData("https://img/small.png", Large: null));

        source.ToCard(gameId: 1, cardSetId: 1).ImageUrl.ShouldBe("https://img/small.png");
    }

    [Fact]
    public void Card_metadata_is_trimmed_to_the_used_attributes()
    {
        var source = new PokemonCardData(
            "base1-4", "Charizard", "4", "Rare Holo",
            Supertype: "Pokémon", Subtypes: ["Stage 2"], Hp: "120", Types: ["Fire"],
            Images: null);

        var metadata = source.ToCard(gameId: 1, cardSetId: 1).Metadata.ShouldNotBeNull();

        using var doc = JsonDocument.Parse(metadata);
        var root = doc.RootElement;
        root.GetProperty("supertype").GetString().ShouldBe("Pokémon");
        root.GetProperty("hp").GetString().ShouldBe("120");
        root.GetProperty("subtypes").EnumerateArray().Single().GetString().ShouldBe("Stage 2");
        root.GetProperty("types").EnumerateArray().Single().GetString().ShouldBe("Fire");
    }

    [Fact]
    public void Card_metadata_is_null_when_no_attributes_present()
    {
        var source = new PokemonCardData(
            "base1-99", "Energy", null, null,
            Supertype: null, Subtypes: [], Hp: null, Types: [], Images: null);

        source.ToCard(gameId: 1, cardSetId: 1).Metadata.ShouldBeNull();
    }
}
