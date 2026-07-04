using Microsoft.EntityFrameworkCore;
using NbTcgTrader.Api.Common.Domain;
using NbTcgTrader.Api.Common.Persistence;
using Npgsql;

namespace NbTcgTrader.Api.Features.Catalog;

/// <summary>
/// Shared persistence for provider results (AGENTS.md §6 shared logic): turns a
/// <see cref="CatalogCard"/> into a stored <see cref="Card"/> (with its
/// <see cref="CardSet"/>/<see cref="Game"/>) exactly once. Used by the add-to-binder
/// slice (#10) and import matching (#15), so both persist catalog rows through the
/// same rules. Lookups consult the change tracker before the database, so several
/// stagings in one unit of work (an import job's rows) share the same new card/set
/// instead of inserting duplicates.
/// </summary>
public sealed class CatalogCardStore(AppDbContext db)
{
    // The MVP catalog provider serves Pokémon only (AGENTS.md §8); the schema stays
    // TCG-agnostic. When a second game ships, the provider must declare its game
    // instead of this store assuming it.
    private const string GameSlug = "pokemon";
    private const string GameName = "Pokémon";

    private Game? _game;

    /// <summary>Finds a stored — or already staged in this unit of work — card by provider id.</summary>
    public async Task<Card?> FindCardAsync(string externalId, CancellationToken cancellationToken)
    {
        var staged = db.Cards.Local.FirstOrDefault(c => c.ExternalId == externalId);
        if (staged is not null)
        {
            return staged;
        }

        return await db.Cards
            .Include(c => c.CardSet)
            .FirstOrDefaultAsync(c => c.ExternalId == externalId, cancellationToken);
    }

    /// <summary>
    /// Stages the card (and, when present and unseen, its set) for insert. Navigations
    /// are used instead of FK ids because the game/set may themselves be new this call;
    /// everything is written by the caller's SaveChanges. Concurrent first-adds of the
    /// same card can race and insert duplicate catalog rows — accepted for the MVP.
    /// </summary>
    public async Task<Card> StageCardAsync(
        CatalogCard catalogCard, CancellationToken cancellationToken)
    {
        var game = await ResolveGameAsync(cancellationToken);

        CardSet? set = null;
        if (catalogCard.Set is { } catalogSet)
        {
            set = db.CardSets.Local.FirstOrDefault(s => s.ExternalId == catalogSet.ExternalId)
                  ?? await db.CardSets.FirstOrDefaultAsync(
                      s => s.ExternalId == catalogSet.ExternalId, cancellationToken);

            if (set is null)
            {
                set = catalogSet.ToCardSet(game.Id);
                set.Game = game;
                db.CardSets.Add(set);
            }
        }

        var card = catalogCard.ToCard(game.Id, set?.Id);
        card.Game = game;
        card.CardSet = set;
        db.Cards.Add(card);

        return card;
    }

    /// <summary>
    /// Finds or creates the provider's game, saving it immediately so a concurrent
    /// first-creation surfaces as a unique violation that resolves to the winner's row.
    /// </summary>
    private async Task<Game> ResolveGameAsync(CancellationToken cancellationToken)
    {
        if (_game is not null)
        {
            return _game;
        }

        var game = await db.Games
            .FirstOrDefaultAsync(g => g.Slug == GameSlug, cancellationToken);

        if (game is null)
        {
            game = new Game { Name = GameName, Slug = GameSlug };
            db.Games.Add(game);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                db.Entry(game).State = EntityState.Detached;

                game = await db.Games
                    .FirstOrDefaultAsync(g => g.Slug == GameSlug, cancellationToken);

                if (game is null)
                {
                    throw;
                }
            }
        }

        _game = game;
        return game;
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
        };
}
