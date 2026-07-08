namespace NbTcgTrader.Api.Features.Catalog.Seeding;

/// <summary>
/// A source of the pokemon-tcg-data dataset (BACKLOG #72): the sets, and the cards
/// belonging to a given set. Kept behind an interface so the seeder is testable with
/// an in-memory source (no filesystem/network) and the on-disk layout can change
/// without touching <see cref="CatalogSeeder"/>. Cards are yielded per set so the
/// seeder can upsert and release one set's cards at a time instead of holding the
/// whole ~20k-card dataset in memory.
/// </summary>
public interface ICardDataSource
{
    /// <summary>All sets in the dataset.</summary>
    Task<IReadOnlyList<PokemonSetData>> LoadSetsAsync(CancellationToken cancellationToken);

    /// <summary>The cards belonging to <paramref name="setId"/> (the set's provider id).</summary>
    Task<IReadOnlyList<PokemonCardData>> LoadCardsAsync(
        string setId, CancellationToken cancellationToken);
}

/// <summary>
/// A set as published in pokemon-tcg-data (<c>sets/en.json</c>). Only the fields the
/// seeder maps onto the domain <c>CardSet</c> are declared; the rest of the payload is
/// ignored on deserialization.
/// </summary>
public sealed record PokemonSetData(
    string Id,
    string Name,
    string? PtcgoCode,
    string? ReleaseDate);

/// <summary>
/// A card as published in pokemon-tcg-data (<c>cards/en/{setId}.json</c>). The set is
/// implied by the file, so a card carries no embedded set object. Only the fields the
/// seeder maps (identity, display, and the trimmed metadata attributes) are declared.
/// </summary>
public sealed record PokemonCardData(
    string Id,
    string Name,
    string? Number,
    string? Rarity,
    string? Supertype,
    IReadOnlyList<string>? Subtypes,
    string? Hp,
    IReadOnlyList<string>? Types,
    PokemonImageData? Images);

/// <summary>Card image URLs; the large image is preferred, falling back to the small.</summary>
public sealed record PokemonImageData(string? Small, string? Large);
