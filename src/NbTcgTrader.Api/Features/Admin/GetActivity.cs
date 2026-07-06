using Microsoft.EntityFrameworkCore;
using NbTcgTrader.Api.Common.Domain;
using NbTcgTrader.Api.Common.Persistence;

namespace NbTcgTrader.Api.Features.Admin;

/// <summary>
/// Operational activity snapshot for the admin hub, derived entirely from state
/// the app already keeps (activity timestamps, Identity's failed-attempt counter,
/// import job statuses). Deliberately no per-request tracking, no IP addresses,
/// and no new personal data collection (privacy-conscious by design).
/// </summary>
public sealed class GetActivityHandler(AppDbContext db)
{
    private const int SectionSize = 20;

    public async Task<IResult> HandleAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        var recentSignIns = await db.Users
            .AsNoTracking()
            .Where(u => u.LastLoginAt != null)
            .OrderByDescending(u => u.LastLoginAt)
            .Take(SectionSize)
            .Select(u => new AdminActivityUserResponse(u.Id, u.DisplayName, u.LastLoginAt))
            .ToListAsync(cancellationToken);

        var recentRegistrations = await db.Users
            .AsNoTracking()
            .OrderByDescending(u => u.CreatedAt)
            .Take(SectionSize)
            .Select(u => new AdminActivityUserResponse(
                u.Id, u.DisplayName, u.CreatedAt))
            .ToListAsync(cancellationToken);

        // Identity's counter resets on successful login, so a non-zero count means
        // failures since the user last signed in — enough to spot brute-forcing
        // without storing per-attempt records.
        var failedSignIns = await db.Users
            .AsNoTracking()
            .Where(u => u.AccessFailedCount > 0)
            .OrderByDescending(u => u.AccessFailedCount)
            .Take(SectionSize)
            .Select(u => new AdminFailedSignInResponse(
                u.Id,
                u.DisplayName,
                u.AccessFailedCount,
                u.LockoutEnabled && u.LockoutEnd != null && u.LockoutEnd > now))
            .ToListAsync(cancellationToken);

        var recentFailedImports = await db.ImportJobs
            .AsNoTracking()
            .Where(j => j.Status == ImportStatus.Failed)
            .OrderByDescending(j => j.CreatedAt)
            .Take(SectionSize)
            .Select(j => new AdminFailedImportResponse(
                j.Id,
                j.UserId,
                j.User!.DisplayName,
                j.FileName,
                j.CreatedAt))
            .ToListAsync(cancellationToken);

        return Results.Ok(new AdminActivityResponse(
            recentSignIns,
            recentRegistrations,
            failedSignIns,
            recentFailedImports));
    }
}
