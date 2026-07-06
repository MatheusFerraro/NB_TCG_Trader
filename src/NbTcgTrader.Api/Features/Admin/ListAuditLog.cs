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

        var items = await (
                from audit in query
                join adminUser in db.Users.AsNoTracking()
                    on audit.AdminUserId equals adminUser.Id into adminUsers
                from adminUser in adminUsers.DefaultIfEmpty()
                join targetUser in db.Users.AsNoTracking()
                    on audit.TargetUserId equals targetUser.Id into targetUsers
                from targetUser in targetUsers.DefaultIfEmpty()
                orderby audit.CreatedAt descending, audit.Id descending
                select new AdminAuditEntryResponse(
                    audit.Id,
                    audit.AdminUserId,
                    adminUser == null ? null : adminUser.DisplayName,
                    audit.TargetUserId,
                    targetUser == null ? null : targetUser.DisplayName,
                    audit.Action,
                    audit.Reason,
                    audit.CorrelationId,
                    audit.CreatedAt))
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        return Results.Ok(new AdminAuditLogResponse(
            items, request.Page, request.PageSize, totalCount));
    }
}
