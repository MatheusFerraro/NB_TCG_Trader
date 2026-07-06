using NbTcgTrader.Api.Common.Domain;

namespace NbTcgTrader.Api.Features.Admin;

/// <summary>Operational summary for the admin dashboard.</summary>
public sealed record AdminDashboardResponse(
    int TotalUsers,
    int NewUsersLast7Days,
    int NewUsersLast30Days,
    int LockedUsers,
    int ActiveListings,
    int FailedImportsLast7Days);

/// <summary>
/// One row of the admin user table. Contact channels are reported as present /
/// not present only — the admin hub never needs the handles themselves
/// (privacy-conscious by design; CLAUDE.md §15).
/// </summary>
public sealed record AdminUserSummaryResponse(
    string Id,
    string DisplayName,
    string? Email,
    string? City,
    string? Country,
    bool HasContactEmail,
    bool HasDiscordHandle,
    bool HasInstagramHandle,
    bool IsLockedOut,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt,
    DateTimeOffset? LastSeenAt);

public sealed record AdminUserListResponse(
    IReadOnlyList<AdminUserSummaryResponse> Items,
    int Page,
    int PageSize,
    int TotalCount);

/// <summary>Per-status import job counts for one user.</summary>
public sealed record AdminImportJobCounts(int Total, int Failed, int NeedsReview, int Completed);

/// <summary>
/// Single-user operational detail. Extends the summary with lockout facts,
/// roles, and activity counts — still no contact handle values, no credentials.
/// </summary>
public sealed record AdminUserDetailResponse(
    string Id,
    string DisplayName,
    string? Email,
    string? City,
    string? Country,
    bool HasContactEmail,
    bool HasDiscordHandle,
    bool HasInstagramHandle,
    IReadOnlyList<string> Roles,
    bool IsLockedOut,
    DateTimeOffset? LockoutEnd,
    int AccessFailedCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt,
    DateTimeOffset? LastSeenAt,
    int CollectionItemCount,
    int ActiveListingCount,
    AdminImportJobCounts ImportJobs,
    IReadOnlyList<AdminAuditEntryResponse> RecentAuditEntries);

/// <summary>One immutable admin audit row (display names resolved best-effort).</summary>
public sealed record AdminAuditEntryResponse(
    long Id,
    string AdminUserId,
    string? AdminDisplayName,
    string? TargetUserId,
    string? TargetDisplayName,
    AdminAction Action,
    string? Reason,
    string? CorrelationId,
    DateTimeOffset CreatedAt);

public sealed record AdminAuditLogResponse(
    IReadOnlyList<AdminAuditEntryResponse> Items,
    int Page,
    int PageSize,
    int TotalCount);

/// <summary>A recent sign-in / registration row on the activity page.</summary>
public sealed record AdminActivityUserResponse(
    string Id,
    string DisplayName,
    DateTimeOffset? Timestamp);

/// <summary>A user currently accumulating failed sign-in attempts.</summary>
public sealed record AdminFailedSignInResponse(
    string Id,
    string DisplayName,
    int AccessFailedCount,
    bool IsLockedOut);

/// <summary>A recently failed import job (operational triage).</summary>
public sealed record AdminFailedImportResponse(
    int Id,
    string UserId,
    string UserDisplayName,
    string FileName,
    DateTimeOffset CreatedAt);

/// <summary>
/// Site/user activity snapshot derived from data the app already stores —
/// no request tracking, no IPs, no new personal data collection.
/// </summary>
public sealed record AdminActivityResponse(
    IReadOnlyList<AdminActivityUserResponse> RecentSignIns,
    IReadOnlyList<AdminActivityUserResponse> RecentRegistrations,
    IReadOnlyList<AdminFailedSignInResponse> FailedSignIns,
    IReadOnlyList<AdminFailedImportResponse> RecentFailedImports);
