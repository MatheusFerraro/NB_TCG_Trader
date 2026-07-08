using NbTcgTrader.Api.Features.Catalog.Seeding;

namespace NbTcgTrader.Tests;

/// <summary>
/// In-memory <see cref="ICardDataSource"/> double for seeder tests: sets and their
/// cards are supplied directly, so the seeder's upsert logic can be exercised without a
/// filesystem checkout or network. Unknown set ids yield no cards, mirroring the
/// filesystem source's "missing card file → empty" behavior.
/// </summary>
internal sealed class FakeCardDataSource : ICardDataSource
{
    public List<PokemonSetData> Sets { get; } = [];

    public Dictionary<string, List<PokemonCardData>> CardsBySetId { get; } = [];

    public Task<IReadOnlyList<PokemonSetData>> LoadSetsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PokemonSetData>>(Sets);

    public Task<IReadOnlyList<PokemonCardData>> LoadCardsAsync(
        string setId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PokemonCardData>>(
            CardsBySetId.TryGetValue(setId, out var cards) ? cards : []);
}
