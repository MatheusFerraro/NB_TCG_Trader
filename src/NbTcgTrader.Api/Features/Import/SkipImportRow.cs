using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using NbTcgTrader.Api.Common.Domain;
using NbTcgTrader.Api.Common.Persistence;

namespace NbTcgTrader.Api.Features.Import;

/// <summary>
/// Skips an unmatched import row (BACKLOG #16): the user decides the row has no catalog
/// match worth keeping, so it is marked <see cref="MatchStatus.Skipped"/> and creates no
/// binder item (the AC's "skipped rows excluded"). The job is then recomputed and
/// completes once nothing is left unmatched. Ownership is enforced on the job's user id,
/// so another user's job/row is an indistinguishable 404 (CLAUDE.md §15); a row that is
/// not currently Unmatched is a 409 Conflict.
/// </summary>
public sealed class SkipImportRowHandler(
    AppDbContext db,
    ILogger<SkipImportRowHandler> logger)
{
    public async Task<IResult> HandleAsync(
        int jobId,
        int rowId,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var userId = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (userId is null)
        {
            return Results.Unauthorized();
        }

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
            return ResolveImportRowHandler.RowNotFound(jobId, rowId);
        }

        if (row.MatchStatus != MatchStatus.Unmatched)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Row already reconciled",
                detail: $"Import row {rowId} is {row.MatchStatus}, not Unmatched.");
        }

        row.MatchStatus = MatchStatus.Skipped;
        ImportReconciliation.RecomputeProgress(job);

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "User {UserId} skipped import row {RowId} of job {JobId}; " +
            "job is now {JobStatus} ({RowsUnmatched} unmatched)",
            userId, rowId, jobId, job.Status, job.RowsUnmatched);

        return Results.Ok(new SkipImportRowResponse(
            ImportJobProgressResponse.From(job),
            row.Id,
            row.MatchStatus));
    }
}
