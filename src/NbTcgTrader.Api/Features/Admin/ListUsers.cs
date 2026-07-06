using FluentValidation;
using Microsoft.EntityFrameworkCore;
using NbTcgTrader.Api.Common.Persistence;

namespace NbTcgTrader.Api.Features.Admin;

/// <summary>
/// Admin user-table query, bound from the query string via <c>[AsParameters]</c>.
/// <see cref="Search"/> matches display name and email (contains, case-insensitive)
/// or the exact user id; the remaining filters combine (AND).
/// </summary>
public sealed record ListUsersRequest(
    string? Search = null,
    string? City = null,
    string? Country = null,
    bool? Locked = null,
    int Page = 1,
    int PageSize = 25);

public sealed class ListUsersRequestValidator : AbstractValidator<ListUsersRequest>
{
    public ListUsersRequestValidator()
    {
        RuleFor(x => x.Page).InclusiveBetween(1, 10_000);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Search).MaximumLength(256);
        RuleFor(x => x.City).MaximumLength(100);
        RuleFor(x => x.Country).MaximumLength(100);
    }
}

/// <summary>
/// One page of registered users for the admin table, newest first. Exposes
/// operational fields only: contact channels are reduced to present/absent flags
/// and no credentials or identity internals ever leave the query (CLAUDE.md §15).
/// </summary>
public sealed class ListUsersHandler(AppDbContext db)
{
    public async Task<IResult> HandleAsync(
        ListUsersRequest request,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var query = db.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var raw = request.Search.Trim();
            var pattern = $"%{Escape(raw)}%";
            query = query.Where(u =>
                u.Id == raw ||
                EF.Functions.ILike(u.DisplayName, pattern) ||
                (u.Email != null && EF.Functions.ILike(u.Email, pattern)));
        }

        if (!string.IsNullOrWhiteSpace(request.City))
        {
            var city = request.City.Trim();
            query = query.Where(u => u.City != null && u.City.ToLower() == city.ToLower());
        }

        if (!string.IsNullOrWhiteSpace(request.Country))
        {
            var country = request.Country.Trim();
            query = query.Where(u =>
                u.Country != null && u.Country.ToLower() == country.ToLower());
        }

        if (request.Locked.HasValue)
        {
            query = request.Locked.Value
                ? query.Where(u => u.LockoutEnabled && u.LockoutEnd != null && u.LockoutEnd > now)
                : query.Where(u => !u.LockoutEnabled || u.LockoutEnd == null || u.LockoutEnd <= now);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(u => u.CreatedAt)
            .ThenBy(u => u.Id) // tie-break so paging is deterministic
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(u => new AdminUserSummaryResponse(
                u.Id,
                u.DisplayName,
                u.Email,
                u.City,
                u.Country,
                u.ContactEmail != null,
                u.DiscordHandle != null,
                u.InstagramHandle != null,
                u.LockoutEnabled && u.LockoutEnd != null && u.LockoutEnd > now,
                u.CreatedAt,
                u.LastLoginAt,
                u.LastSeenAt))
            .ToListAsync(cancellationToken);

        return Results.Ok(new AdminUserListResponse(
            items, request.Page, request.PageSize, totalCount));
    }

    // Same ILike escaping as the marketplace browse: user-typed % and _ match
    // literally. EF still parameterizes the value.
    private static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
