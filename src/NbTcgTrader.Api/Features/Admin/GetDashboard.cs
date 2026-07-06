using Microsoft.EntityFrameworkCore;
using NbTcgTrader.Api.Common.Domain;
using NbTcgTrader.Api.Common.Persistence;

namespace NbTcgTrader.Api.Features.Admin;

/// <summary>
/// Aggregate counts for the admin dashboard: user totals and growth, lockouts,
/// active public listings, and recent import failures. Counts only — no personal
/// data leaves this endpoint.
/// </summary>
public sealed class GetDashboardHandler(AppDbContext db)
{
    public async Task<IResult> HandleAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var sevenDaysAgo = now.AddDays(-7);
        var thirtyDaysAgo = now.AddDays(-30);

        var totalUsers = await db.Users.CountAsync(cancellationToken);
        var newUsers7 = await db.Users
            .CountAsync(u => u.CreatedAt >= sevenDaysAgo, cancellationToken);
        var newUsers30 = await db.Users
            .CountAsync(u => u.CreatedAt >= thirtyDaysAgo, cancellationToken);
        var lockedUsers = await db.Users
            .CountAsync(u => u.LockoutEnabled && u.LockoutEnd != null && u.LockoutEnd > now,
                cancellationToken);
        var activeListings = await db.CollectionItems
            .CountAsync(i => i.IsForSale && !i.IsPrivate, cancellationToken);
        var failedImports7 = await db.ImportJobs
            .CountAsync(j => j.Status == ImportStatus.Failed && j.CreatedAt >= sevenDaysAgo,
                cancellationToken);

        return Results.Ok(new AdminDashboardResponse(
            totalUsers,
            newUsers7,
            newUsers30,
            lockedUsers,
            activeListings,
            failedImports7));
    }
}
