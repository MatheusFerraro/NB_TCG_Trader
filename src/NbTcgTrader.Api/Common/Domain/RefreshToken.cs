namespace NbTcgTrader.Api.Common.Domain;

/// <summary>
/// A rotating refresh token issued to a user (CLAUDE.md §15). Only a hash of the
/// token value is persisted — the raw token lives solely in the client's hands —
/// so a database leak cannot be replayed. A token is single-use: refreshing
/// revokes it and issues a successor, chained via <see cref="ReplacedByTokenHash"/>
/// for audit.
/// </summary>
public sealed class RefreshToken
{
    public Guid Id { get; set; }

    public required string UserId { get; set; }

    public AppUser? User { get; set; }

    /// <summary>SHA-256 hash (Base64) of the opaque token value. Never the raw token.</summary>
    public required string TokenHash { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Set when the token is rotated or explicitly invalidated.</summary>
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>Hash of the token that superseded this one on rotation.</summary>
    public string? ReplacedByTokenHash { get; set; }

    /// <summary>Live = not yet revoked and not past expiry.</summary>
    public bool IsActive(DateTimeOffset now) => RevokedAt is null && now < ExpiresAt;
}
