using System.Security.Claims;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using NbTcgTrader.Api.Common.Domain;
using NbTcgTrader.Api.Common.Persistence;
using NbTcgTrader.Api.Features.Catalog;

namespace NbTcgTrader.Api.Features.Import;

/// <summary>
/// Resolves an unmatched import row by hand-picking the catalog card it refers to
/// (BACKLOG #16). The card is identified by its provider <c>externalId</c> — the id the
/// catalog search returns — exactly like the add-to-binder flow.
/// </summary>
public sealed record ResolveImportRowRequest(string CardExternalId);

public sealed class ResolveImportRowValidator : AbstractValidator<ResolveImportRowRequest>
{
    public ResolveImportRowValidator()
    {
        // Max length matches the Card.ExternalId column, same as AddCard.
        RuleFor(x => x.CardExternalId)
            .NotEmpty()
            .Must(value => !string.IsNullOrWhiteSpace(value))
            .WithMessage("'Card External Id' must not be empty.")
            .MaximumLength(100);
    }
}

/// <summary>
/// Picks the chosen catalog card for an unmatched row, creates the binder
/// <see cref="CollectionItem"/> from the row's parsed values, marks the row
/// <see cref="MatchStatus.ManuallyMatched"/>, and recomputes the job — completing it once
/// nothing is left unmatched (#16 AC). Ownership is enforced on the job's user id, so
/// another user's job/row is an indistinguishable 404 (AGENTS.md §15). A row that is not
/// currently Unmatched (already resolved, skipped, or auto-matched) is a 409 Conflict.
/// An <c>externalId</c> the provider does not know is a 404 — the same contract as
/// add-to-binder — and the chosen card is staged locally through the shared
/// <see cref="CatalogCardStore"/> on first use.
/// </summary>
public sealed class ResolveImportRowHandler(
    AppDbContext db,
    ICardCatalogClient catalog,
    CatalogCardStore cardStore,
    ILogger<ResolveImportRowHandler> logger)
{
    public async Task<IResult> HandleAsync(
        int jobId,
        int rowId,
        ResolveImportRowRequest request,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var userId = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (userId is null)
        {
            return Results.Unauthorized();
        }

        // Load the whole job (with rows) so the progress recompute sees every row, and so
        // ownership is checked in the same query.
        var job = await db.ImportJobs
            .Include(j => j.Rows)
            .FirstOrDefaultAsync(j => j.Id == jobId && j.UserId == userId, cancellationToken);

        if (job is null)
        {
            return ListImportRowsHandler.NotFound(jobId);
        }

        var row = job.Rows.FirstOrDefault(r => r.Id == rowId);
        if (row is null)
        {
            return RowNotFound(jobId, rowId);
        }

        if (row.MatchStatus != MatchStatus.Unmatched)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Row already reconciled",
                detail: $"Import row {rowId} is {row.MatchStatus}, not Unmatched.");
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

        var item = ImportReconciliation.ToBinderItem(
            row, card, userId, DateTimeOffset.UtcNow);
        db.CollectionItems.Add(item);

        row.MatchedCard = card;
        row.MatchStatus = MatchStatus.ManuallyMatched;
        ImportReconciliation.RecomputeProgress(job);

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "User {UserId} resolved import row {RowId} of job {JobId} to card {CardExternalId}; " +
            "job is now {JobStatus} ({RowsUnmatched} unmatched)",
            userId, rowId, jobId, externalId, job.Status, job.RowsUnmatched);

        return Results.Ok(new ResolveImportRowResponse(
            ImportJobProgressResponse.From(job),
            row.Id,
            row.MatchStatus,
            item.Id));
    }

    internal static IResult RowNotFound(int jobId, int rowId) => Results.Problem(
        statusCode: StatusCodes.Status404NotFound,
        title: "Import row not found",
        detail: $"No row {rowId} exists in import job {jobId}.");
}
