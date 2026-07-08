using Microsoft.EntityFrameworkCore;
using NbTcgTrader.Api.Common.Domain;
using NbTcgTrader.Api.Common.Persistence;

namespace NbTcgTrader.Api.Features.Catalog.Seeding;

/// <summary>Counts from a seeding run, logged and returned to the CLI.</summary>
public sealed record CatalogSeedResult(
    int SetsInserted,
    int SetsUpdated,
    int CardsInserted,
    int CardsUpdated)
{
    public int SetsTotal => SetsInserted + SetsUpdated;

    public int CardsTotal => CardsInserted + CardsUpdated;
}

/// <summary>
/// Ingests the pokemon-tcg-data dataset into <c>CardSet</c>/<c>Card</c> (BACKLOG #72).
/// Upserts are keyed on <c>ExternalId</c> so re-running produces zero duplicates and
/// applies changed fields to existing rows. Cards are processed one set at a time —
/// each set's cards are saved in a single batch and the change tracker cleared before
/// the next set — so the full ~20k-card seed completes in minutes without holding the
/// whole dataset in memory.
/// </summary>
public sealed class CatalogSeeder(
    AppDbContext db,
    ICardDataSource source,
    ILogger<CatalogSeeder> logger)
{
    // The dataset is Pokémon (CLAUDE.md §8); the schema stays TCG-agnostic. A second
    // game would ship its own source + game identity rather than reuse these.
    private const string GameSlug = "pokemon";
    private const string GameName = "Pokémon";

    public async Task<CatalogSeedResult> SeedAsync(CancellationToken cancellationToken)
    {
        var game = await EnsureGameAsync(cancellationToken);

        var incomingSets = await source.LoadSetsAsync(cancellationToken);
        var (setsInserted, setsUpdated, setIdsByExternalId) =
            await UpsertSetsAsync(game.Id, incomingSets, cancellationToken);

        logger.LogInformation(
            "Seeded sets: {Inserted} inserted, {Updated} updated ({Total} total).",
            setsInserted, setsUpdated, incomingSets.Count);

        var cardsInserted = 0;
        var cardsUpdated = 0;

        foreach (var set in incomingSets)
        {
            var setId = setIdsByExternalId[set.Id];
            var cards = await source.LoadCardsAsync(set.Id, cancellationToken);
            if (cards.Count == 0)
            {
                continue;
            }

            var (inserted, updated) =
                await UpsertCardsAsync(game.Id, setId, cards, cancellationToken);
            cardsInserted += inserted;
            cardsUpdated += updated;

            logger.LogDebug(
                "Seeded set {SetId}: {Inserted} cards inserted, {Updated} updated.",
                set.Id, inserted, updated);

            // Release this set's tracked entities before the next set so memory stays
            // flat across the whole dataset. The captured ids remain valid POCO values.
            db.ChangeTracker.Clear();
        }

        var result = new CatalogSeedResult(setsInserted, setsUpdated, cardsInserted, cardsUpdated);
        logger.LogInformation(
            "Catalog seed complete: {SetsTotal} sets, {CardsTotal} cards " +
            "({CardsInserted} inserted, {CardsUpdated} updated).",
            result.SetsTotal, result.CardsTotal, result.CardsInserted, result.CardsUpdated);

        return result;
    }

    private async Task<(int Inserted, int Updated, Dictionary<string, int> SetIds)> UpsertSetsAsync(
        int gameId, IReadOnlyList<PokemonSetData> incoming, CancellationToken cancellationToken)
    {
        // A dirty database can already hold duplicate ExternalIds (concurrent first-adds
        // via CatalogCardStore are allowed to race for the MVP). Keep the lowest-Id row
        // per ExternalId so the seeder upserts deterministically instead of throwing.
        var existing = (await db.CardSets
                .Where(s => s.GameId == gameId && s.ExternalId != null)
                .ToListAsync(cancellationToken))
            .GroupBy(s => s.ExternalId!)
            .ToDictionary(group => group.Key, group => group.MinBy(s => s.Id)!);

        var inserted = 0;
        var updated = 0;

        foreach (var incomingSet in incoming)
        {
            if (existing.TryGetValue(incomingSet.Id, out var target))
            {
                incomingSet.ApplyTo(target, gameId);
                updated++;
            }
            else
            {
                target = incomingSet.ToCardSet(gameId);
                db.CardSets.Add(target);
                existing[incomingSet.Id] = target;
                inserted++;
            }
        }

        // Persist so every set has its database id before cards reference it.
        await db.SaveChangesAsync(cancellationToken);

        var setIds = existing.ToDictionary(pair => pair.Key, pair => pair.Value.Id);
        db.ChangeTracker.Clear();
        return (inserted, updated, setIds);
    }

    private async Task<(int Inserted, int Updated)> UpsertCardsAsync(
        int gameId, int setId, IReadOnlyList<PokemonCardData> cards, CancellationToken cancellationToken)
    {
        // As with sets, tolerate pre-existing duplicate ExternalIds by keeping the
        // lowest-Id row per key rather than throwing on a dirty database.
        var existing = (await db.Cards
                .Where(c => c.CardSetId == setId)
                .ToListAsync(cancellationToken))
            .GroupBy(c => c.ExternalId)
            .ToDictionary(group => group.Key, group => group.MinBy(c => c.Id)!);

        var inserted = 0;
        var updated = 0;

        foreach (var incomingCard in cards)
        {
            if (existing.TryGetValue(incomingCard.Id, out var target))
            {
                incomingCard.ApplyTo(target, gameId, setId);
                updated++;
            }
            else
            {
                db.Cards.Add(incomingCard.ToCard(gameId, setId));
                inserted++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return (inserted, updated);
    }

    private async Task<Game> EnsureGameAsync(CancellationToken cancellationToken)
    {
        var game = await db.Games
            .FirstOrDefaultAsync(g => g.Slug == GameSlug, cancellationToken);

        if (game is null)
        {
            game = new Game { Name = GameName, Slug = GameSlug };
            db.Games.Add(game);
            await db.SaveChangesAsync(cancellationToken);
        }

        return game;
    }
}
