using System.Security.Claims;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using NbTcgTrader.Api.Common.Domain;
using NbTcgTrader.Api.Common.Persistence;
using NbTcgTrader.Api.Features.Catalog;

namespace NbTcgTrader.Api.Features.Collection;

/// <summary>
/// Adds a catalog card to the caller's collection (BACKLOG #10). The card is identified
/// by its provider id — the <c>externalId</c> the catalog search endpoint returns — so
/// the "browse and add" flow needs no local card id. <see cref="Condition"/> defaults
/// to Near Mint when omitted.
/// </summary>
public sealed record AddCardRequest(
    string CardExternalId,
    int Quantity,
    CardCondition Condition = CardCondition.NM);

public sealed class AddCardValidator : AbstractValidator<AddCardRequest>
{
    public AddCardValidator()
    {
        // Max length matches the Card.ExternalId column.
        RuleFor(x => x.CardExternalId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Quantity).InclusiveBetween(1, 999);
        RuleFor(x => x.Condition).IsInEnum();
    }
}

/// <summary>
/// Resolves the external id to a local <see cref="Card"/> — persisting the card (and its
/// <see cref="CardSet"/>/<see cref="Game"/>) from the catalog provider on first use, per
/// the <c>CatalogMapping</c> contract — then creates the <see cref="CollectionItem"/>.
/// An id the provider doesn't know is rejected with 404.
/// </summary>
public sealed class AddCardHandler(
    AppDbContext db,
    ICardCatalogClient catalog,
    IOptions<CatalogOptions> catalogOptions,
    ILogger<AddCardHandler> logger)
{
    // The MVP catalog provider serves Pokémon only (CLAUDE.md §8); the schema stays
    // TCG-agnostic. When a second game ships, the provider must declare its game
    // instead of the slice assuming it.
    private const string GameSlug = "pokemon";
    private const string GameName = "Pokémon";

    public async Task<IResult> HandleAsync(
        AddCardRequest request,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        // Owner comes from the validated JWT's `sub` claim, never from the body
        // (CLAUDE.md §15).
        var userId = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (userId is null)
        {
            return Results.Unauthorized();
        }

        var externalId = request.CardExternalId.Trim();

        var card = await db.Cards
            .Include(c => c.CardSet)
            .FirstOrDefaultAsync(c => c.ExternalId == externalId, cancellationToken);

        if (card is null)
        {
            var catalogCard = await catalog.GetCardAsync(externalId, cancellationToken);
            if (catalogCard is null)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Unknown card",
                    detail: $"No catalog card exists with id '{externalId}'.");
            }

            var game = await ResolveGameAsync(cancellationToken);
            card = await StageNewCardAsync(catalogCard, game, cancellationToken);
        }

        var now = DateTimeOffset.UtcNow;
        var item = new CollectionItem
        {
            UserId = userId,
            Card = card,
            Quantity = request.Quantity,
            Condition = request.Condition,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.CollectionItems.Add(item);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "User {UserId} added card {CardExternalId} x{Quantity} to their collection",
            userId, externalId, request.Quantity);

        var response = CollectionItemResponse.From(
            item, catalogOptions.Value.PlaceholderImageUrl);

        return Results.Created($"/collection/items/{item.Id}", response);
    }

    private async Task<Game> ResolveGameAsync(CancellationToken cancellationToken)
    {
        var game = await db.Games
            .FirstOrDefaultAsync(g => g.Slug == GameSlug, cancellationToken);

        if (game is null)
        {
            game = new Game { Name = GameName, Slug = GameSlug };
            db.Games.Add(game);
        }

        return game;
    }

    /// <summary>
    /// Stages the card (and, when present and unseen, its set) for insert. Navigations
    /// are used instead of FK ids because the game/set may themselves be new this call;
    /// everything is written by the single SaveChanges. Concurrent first-adds of the
    /// same card can race and insert duplicate catalog rows — accepted for the MVP.
    /// </summary>
    private async Task<Card> StageNewCardAsync(
        CatalogCard catalogCard, Game game, CancellationToken cancellationToken)
    {
        CardSet? set = null;
        if (catalogCard.Set is { } catalogSet)
        {
            set = await db.CardSets.FirstOrDefaultAsync(
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
}
