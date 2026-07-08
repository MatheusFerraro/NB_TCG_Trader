using System.Globalization;
using System.Text.Json;
using NbTcgTrader.Api.Common.Domain;

namespace NbTcgTrader.Api.Features.Catalog.Seeding;

/// <summary>
/// Maps pokemon-tcg-data records onto the domain entities (BACKLOG #72, CLAUDE.md §7).
/// The <c>ApplyTo</c> overloads write every mutable field onto an entity, so they serve
/// both create (a fresh entity) and update (an already-tracked row) — the seeder's
/// idempotent upsert. <c>Metadata</c> is trimmed to the attributes the app uses
/// (supertype, subtypes, hp, types), not the full provider payload.
/// </summary>
public static class CatalogSeedMapping
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public static CardSet ToCardSet(this PokemonSetData source, int gameId)
    {
        var set = new CardSet { ExternalId = source.Id, Name = source.Name, Code = ResolveCode(source) };
        source.ApplyTo(set, gameId);
        return set;
    }

    public static void ApplyTo(this PokemonSetData source, CardSet target, int gameId)
    {
        target.GameId = gameId;
        target.ExternalId = source.Id;
        target.Name = source.Name;
        target.Code = ResolveCode(source);
        target.ReleaseDate = ParseReleaseDate(source.ReleaseDate);
    }

    public static Card ToCard(this PokemonCardData source, int gameId, int? cardSetId)
    {
        var card = new Card { ExternalId = source.Id, Name = source.Name };
        source.ApplyTo(card, gameId, cardSetId);
        return card;
    }

    public static void ApplyTo(this PokemonCardData source, Card target, int gameId, int? cardSetId)
    {
        target.GameId = gameId;
        target.CardSetId = cardSetId;
        target.ExternalId = source.Id;
        target.Name = source.Name;
        target.Number = source.Number;
        target.Rarity = source.Rarity;
        target.ImageUrl = source.Images?.Large ?? source.Images?.Small;
        target.Metadata = BuildMetadata(source);
    }

    /// <summary>Prefers the provider's PTCGO code; falls back to the set id when absent.</summary>
    private static string ResolveCode(PokemonSetData source) =>
        string.IsNullOrWhiteSpace(source.PtcgoCode) ? source.Id : source.PtcgoCode;

    private static DateOnly? ParseReleaseDate(string? value) =>
        DateOnly.TryParseExact(value, "yyyy/MM/dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var date)
            ? date
            : null;

    /// <summary>
    /// Captures the variable per-TCG attributes as a small JSON object for the domain
    /// <c>Card.Metadata</c> (jsonb) column. Returns <c>null</c> when nothing is present.
    /// </summary>
    private static string? BuildMetadata(PokemonCardData source)
    {
        var metadata = new Dictionary<string, object>();

        if (!string.IsNullOrWhiteSpace(source.Supertype))
        {
            metadata["supertype"] = source.Supertype;
        }

        if (source.Subtypes is { Count: > 0 })
        {
            metadata["subtypes"] = source.Subtypes;
        }

        if (!string.IsNullOrWhiteSpace(source.Hp))
        {
            metadata["hp"] = source.Hp;
        }

        if (source.Types is { Count: > 0 })
        {
            metadata["types"] = source.Types;
        }

        return metadata.Count == 0 ? null : JsonSerializer.Serialize(metadata, JsonOptions);
    }
}
