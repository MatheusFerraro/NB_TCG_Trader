using FluentValidation;
using Microsoft.EntityFrameworkCore;
using NbTcgTrader.Api.Common.Persistence;

namespace NbTcgTrader.Api.Features.Admin;

public sealed record ListAuditLogRequest(int Page = 1, int PageSize = 25);

public sealed class ListAuditLogRequestValidator : AbstractValidator<ListAuditLogRequest>
{
    public ListAuditLogRequestValidator()
    {
        RuleFor(x => x.Page).InclusiveBetween(1, 10_000);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}

/// <summary>
/// One page of the immutable admin audit trail, newest first. Display names are
/// resolved best-effort (left join): a deleted account leaves the id and a null
/// name, never a broken row.
/// </summary>
public sealed class ListAuditLogHandler(AppDbContext db)
{
    public async Task<IResult> HandleAsync(
        ListAuditLogRequest request,
        CancellationToken cancellationToken)
    {
        var query = db.AdminAuditLogs.AsNoTracking();

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(a => a.CreatedAt)
            .ThenByDescending(a => a.Id) // tie-break so paging is deterministic
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(a => new AdminAuditEntryResponse(
                a.Id,
                a.AdminUserId,
                db.Users.Where(u => u.Id == a.AdminUserId)
                    .Select(u => u.DisplayName).FirstOrDefault(),
                a.TargetUserId,
                db.Users.Where(u => u.Id == a.TargetUserId)
                    .Select(u => u.DisplayName).FirstOrDefault(),
                a.Action,
                a.Reason,
                a.CorrelationId,
                a.CreatedAt))
            .ToListAsync(cancellationToken);

        return Results.Ok(new AdminAuditLogResponse(
            items, request.Page, request.PageSize, totalCount));
    }
}
