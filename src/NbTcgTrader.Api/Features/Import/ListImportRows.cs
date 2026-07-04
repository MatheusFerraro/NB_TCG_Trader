using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using NbTcgTrader.Api.Common.Domain;
using NbTcgTrader.Api.Common.Persistence;

namespace NbTcgTrader.Api.Features.Import;

/// <summary>
/// Lists the rows of an import job that still need a decision (BACKLOG #16): those left
/// <see cref="MatchStatus.Unmatched"/> by auto-matching (#15). Owner-scoped — the job is
/// filtered by the JWT's user id, so another user's job (or a missing one) is an
/// indistinguishable 404 (CLAUDE.md §15). The job's progress travels with the list so the
/// review screen can show how many rows remain and whether the job is already done.
/// </summary>
public sealed class ListImportRowsHandler(AppDbContext db)
{
    public async Task<IResult> HandleAsync(
        int jobId,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var userId = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (userId is null)
        {
            return Results.Unauthorized();
        }

        var job = await db.ImportJobs
            .AsNoTracking()
            .Include(j => j.Rows)
            .FirstOrDefaultAsync(j => j.Id == jobId && j.UserId == userId, cancellationToken);

        if (job is null)
        {
            return NotFound(jobId);
        }

        var rows = job.Rows
            .Where(r => r.MatchStatus == MatchStatus.Unmatched)
            .OrderBy(r => r.Id)
            .Select(UnmatchedRowResponse.From)
            .ToList();

        return Results.Ok(new UnmatchedRowsResponse(
            ImportJobProgressResponse.From(job), rows));
    }

    internal static IResult NotFound(int jobId) => Results.Problem(
        statusCode: StatusCodes.Status404NotFound,
        title: "Import job not found",
        detail: $"No import job {jobId} exists for you.");
}
