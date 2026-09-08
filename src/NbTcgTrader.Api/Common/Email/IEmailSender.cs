namespace NbTcgTrader.Api.Common.Email;

/// <summary>
/// Provider-agnostic transactional email port (issue #69). Implementations are
/// swappable through <c>Email:Provider</c> so the free-tier choice can change
/// without touching a single feature slice (CLAUDE.md §8's pattern, applied to email).
/// </summary>
public interface IEmailSender
{
    /// <summary>
    /// Attempts one delivery. Implementations must never throw for a provider-side
    /// failure — return a failed <see cref="EmailSendResult"/> so the caller can log
    /// and move on. Never log or return the message body, tokens, or the API key.
    /// </summary>
    Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken);
}
