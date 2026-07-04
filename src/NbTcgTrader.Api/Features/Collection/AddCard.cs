using System.Security.Claims;
using FluentValidation;
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
/// <see cref="CardSet"/>/<see cref="Game"/>) from the catalog provider on first use via
/// the shared <see cref="CatalogCardStore"/> — then creates the
/// <see cref="CollectionItem"/>. An id the provider doesn't know is rejected with 404.
/// </summary>
public sealed class AddCardHandler(
    AppDbContext db,
    ICardCatalogClient catalog,
    CatalogCardStore cardStore,
    IOptions<CatalogOptions> catalogOptions,
    ILogger<AddCardHandler> logger)
{
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

        var card = await cardStore.FindCardAsync(externalId, cancellationToken);
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

            card = await cardStore.StageCardAsync(catalogCard, cancellationToken);
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
}
