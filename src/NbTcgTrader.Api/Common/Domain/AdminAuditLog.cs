namespace NbTcgTrader.Api.Common.Domain;

/// <summary>Sensitive admin action recorded for accountability (BACKLOG admin hub).</summary>
public enum AdminAction
{
    UserLocked,
    UserUnlocked,
    UserDetailViewed,
    RoleChanged
}

/// <summary>
/// Immutable audit trail of admin actions: who did what to whom, why, and under
/// which request correlation id. Rows are only ever inserted — never updated or
/// deleted by application code (CLAUDE.md §15: log user ids, not credentials).
/// </summary>
public sealed class AdminAuditLog
{
    public long Id { get; set; }

    /// <summary>Id of the admin who performed the action.</summary>
    public required string AdminUserId { get; set; }

    /// <summary>Id of the user the action targeted, when the action has a target.</summary>
    public string? TargetUserId { get; set; }

    public AdminAction Action { get; set; }

    /// <summary>Operator-supplied justification. Required for locks.</summary>
    public string? Reason { get; set; }

    /// <summary>Request trace id so the action can be correlated with logs.</summary>
    public string? CorrelationId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
