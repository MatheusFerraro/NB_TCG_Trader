using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NbTcgTrader.Api.Common.Domain;
using NbTcgTrader.Api.Common.Persistence;

namespace NbTcgTrader.Api.Features.Admin;

/// <summary>
/// Single-user operational view for the admin hub: profile basics, lockout facts,
/// roles, collection/import counts, and the user's recent audit trail. Viewing it
/// is itself audited (<see cref="AdminAction.UserDetailViewed"/>) because it
/// exposes a user's email and activity. Contact handles stay present/absent flags.
/// </summary>
public sealed class GetUserDetailHandler(
    AppDbContext db,
    UserManager<AppUser> users,
    AdminAuditWriter audit)
{
    public async Task<IResult> HandleAsync(
        string userId,
        string adminUserId,
        CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(userId);
        if (user is null)
        {
            return Results.Problem(
                detail: "No user exists with the supplied id.",
                statusCode: StatusCodes.Status404NotFound,
                title: "User not found");
        }

        var now = DateTimeOffset.UtcNow;
        var roles = await users.GetRolesAsync(user);

        var collectionItemCount = await db.CollectionItems
            .CountAsync(i => i.UserId == userId, cancellationToken);
        var activeListingCount = await db.CollectionItems
            .CountAsync(i => i.UserId == userId && i.IsForSale && !i.IsPrivate,
                cancellationToken);

        var importCounts = await db.ImportJobs
            .Where(j => j.UserId == userId)
            .GroupBy(j => j.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var importJobs = new AdminImportJobCounts(
            importCounts.Sum(c => c.Count),
            importCounts.Where(c => c.Status == ImportStatus.Failed).Sum(c => c.Count),
            importCounts.Where(c => c.Status == ImportStatus.NeedsReview).Sum(c => c.Count),
            importCounts.Where(c => c.Status == ImportStatus.Completed).Sum(c => c.Count));

        var recentAudit = await db.AdminAuditLogs
            .AsNoTracking()
            .Where(a => a.TargetUserId == userId)
            .OrderByDescending(a => a.CreatedAt)
            .ThenByDescending(a => a.Id)
            .Take(10)
            .Select(a => new AdminAuditEntryResponse(
                a.Id,
                a.AdminUserId,
                db.Users.Where(u => u.Id == a.AdminUserId)
                    .Select(u => u.DisplayName).FirstOrDefault(),
                a.TargetUserId,
                user.DisplayName,
                a.Action,
                a.Reason,
                a.CorrelationId,
                a.CreatedAt))
            .ToListAsync(cancellationToken);

        // Viewing a user's operational detail is a sensitive action — record it.
        await audit.WriteAsync(
            adminUserId, userId, AdminAction.UserDetailViewed, null, cancellationToken);

        var isLockedOut = user.LockoutEnabled && user.LockoutEnd is { } end && end > now;

        return Results.Ok(new AdminUserDetailResponse(
            user.Id,
            user.DisplayName,
            user.Email,
            user.City,
            user.Country,
            user.ContactEmail != null,
            user.DiscordHandle != null,
            user.InstagramHandle != null,
            roles.ToArray(),
            isLockedOut,
            user.LockoutEnd,
            user.AccessFailedCount,
            user.CreatedAt,
            user.LastLoginAt,
            user.LastSeenAt,
            collectionItemCount,
            activeListingCount,
            importJobs,
            recentAudit));
    }
}
