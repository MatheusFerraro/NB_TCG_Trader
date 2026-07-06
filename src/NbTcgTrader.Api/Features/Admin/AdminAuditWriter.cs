using System.Diagnostics;
using NbTcgTrader.Api.Common.Domain;
using NbTcgTrader.Api.Common.Persistence;

namespace NbTcgTrader.Api.Features.Admin;

/// <summary>
/// Appends rows to the immutable admin audit trail. Every sensitive admin action
/// goes through here so the shape (who, whom, what, why, correlation id) stays
/// uniform. The correlation id matches the <c>traceId</c> that ProblemDetails and
/// the request logs carry, so an audit row can be tied back to its request.
/// </summary>
public sealed class AdminAuditWriter(AppDbContext db, IHttpContextAccessor httpContext)
{
    /// <summary>Stages an audit row; the caller's SaveChanges commits it atomically.</summary>
    public void Stage(string adminUserId, string? targetUserId, AdminAction action, string? reason)
    {
        db.AdminAuditLogs.Add(new AdminAuditLog
        {
            AdminUserId = adminUserId,
            TargetUserId = targetUserId,
            Action = action,
            Reason = reason,
            CorrelationId = Activity.Current?.Id
                            ?? httpContext.HttpContext?.TraceIdentifier,
            CreatedAt = DateTimeOffset.UtcNow,
        });
    }

    /// <summary>Stages and immediately persists an audit row.</summary>
    public async Task WriteAsync(
        string adminUserId,
        string? targetUserId,
        AdminAction action,
        string? reason,
        CancellationToken cancellationToken)
    {
        Stage(adminUserId, targetUserId, action, reason);
        await db.SaveChangesAsync(cancellationToken);
    }
}
