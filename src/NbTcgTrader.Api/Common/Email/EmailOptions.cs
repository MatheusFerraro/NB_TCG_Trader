namespace NbTcgTrader.Api.Common.Email;

/// <summary>Which <see cref="IEmailSender"/> implementation backs the app.</summary>
public enum EmailProvider
{
    /// <summary>
    /// Development default: renders each message to a file on disk instead of
    /// sending it. Lets the verification / reset flows be exercised end to end
    /// without a provider account or a real inbox (issue #69).
    /// </summary>
    FileDrop = 0,

    /// <summary>Resend's HTTP API — the free-tier provider chosen for the MVP.</summary>
    Resend = 1,
}

/// <summary>
/// Transactional email configuration (issue #69). Everything here is non-secret
/// except <see cref="ApiKey"/>, which comes from user-secrets / environment
/// variables and is never committed or logged (CLAUDE.md §10, §15).
/// </summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>
    /// Kill switch. When false nothing is ever handed to a provider — messages are
    /// dropped after a log line. Flip it off if the free quota is exhausted.
    /// </summary>
    public bool Enabled { get; set; } = true;

    public EmailProvider Provider { get; set; } = EmailProvider.FileDrop;

    /// <summary>Envelope sender address. Must be on a domain verified with the provider.</summary>
    public string FromAddress { get; set; } = "no-reply@localhost";

    /// <summary>Display name shown next to <see cref="FromAddress"/>.</summary>
    public string FromName { get; set; } = "NB TCG Trader";

    /// <summary>Optional Reply-To; falls back to <see cref="FromAddress"/> when unset.</summary>
    public string? ReplyToAddress { get; set; }

    /// <summary>
    /// Absolute base URL of the frontend, used to build the links inside emails
    /// (e.g. <c>https://nbtcgtrader.vercel.app</c>). Differs per environment, so it
    /// is validated on start rather than defaulted.
    /// </summary>
    public string FrontendBaseUrl { get; set; } = "http://localhost:5173";

    /// <summary>
    /// Whether registering an account queues a verification email. Sending still
    /// requires <see cref="Enabled"/>; this only controls the register hook.
    /// </summary>
    public bool SendVerificationOnRegister { get; set; } = true;

    /// <summary>Provider API key. SECRET — user-secrets / environment only.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Provider API base URL. Must end with a trailing slash.</summary>
    public string BaseUrl { get; set; } = "https://api.resend.com/";

    public int TimeoutSeconds { get; set; } = 15;

    /// <summary>
    /// Directory the <see cref="EmailProvider.FileDrop"/> sender writes to, relative
    /// to the content root unless absolute. Gitignored.
    /// </summary>
    public string DropDirectory { get; set; } = "sent-emails";

    /// <summary>
    /// Capacity of the in-process outbox queue. Sending happens off the request
    /// thread so a slow provider never stalls register / forgot-password.
    /// </summary>
    public int QueueCapacity { get; set; } = 500;
}
