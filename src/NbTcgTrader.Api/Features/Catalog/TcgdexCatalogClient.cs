namespace NbTcgTrader.Api.Features.Catalog;

/// <summary>
/// Documented fallback provider behind <see cref="ICardCatalogClient"/> (CLAUDE.md §8).
/// TCGdex is open-source, keyless, and multilingual, which makes it a good resilience
/// option when pokemontcg.io is unavailable or rate-limited.
/// <para>
/// This is a deliberate stub: it proves the interface is provider-agnostic and marks
/// where a real implementation would slot in. To activate it, implement the two methods
/// against the TCGdex API (https://api.tcgdex.net/v2/en/cards) — mapping its
/// <c>id</c>/<c>name</c>/<c>localId</c>/<c>image</c> and set fields onto
/// <see cref="CatalogCard"/>/<see cref="CatalogSet"/> the same way
/// <see cref="PokemonTcgCatalogClient"/> does — and register it in
/// <c>CardCatalogExtensions</c> (e.g. as the typed client, or chained as a fallback).
/// It is intentionally not wired into DI yet (BACKLOG #8 ships pokemontcg.io only).
/// </para>
/// </summary>
public sealed class TcgdexCatalogClient : ICardCatalogClient
{
    private const string NotImplementedReason =
        "TCGdex is the documented fallback provider but is not implemented yet (BACKLOG #8 " +
        "ships pokemontcg.io only). See TcgdexCatalogClient remarks to activate it.";

    public Task<CatalogPage<CatalogCard>> SearchCardsAsync(
        CatalogSearchQuery query,
        CancellationToken cancellationToken) =>
        throw new NotImplementedException(NotImplementedReason);

    public Task<CatalogCard?> GetCardAsync(string externalId, CancellationToken cancellationToken) =>
        throw new NotImplementedException(NotImplementedReason);
}
