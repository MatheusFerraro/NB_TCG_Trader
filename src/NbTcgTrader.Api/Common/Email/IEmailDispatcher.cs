namespace NbTcgTrader.Api.Common.Email;

/// <summary>
/// Hands a message to the outbox without waiting for the provider. Slices call this
/// rather than <see cref="IEmailSender"/> directly: registering an account must not
/// take as long as an HTTP round-trip to a third party, and must not fail when that
/// third party does (issue #69, "keep email sending asynchronous").
/// </summary>
public interface IEmailDispatcher
{
    /// <summary>
    /// Queues <paramref name="message"/> for background delivery. Returns false when
    /// the outbox is full — the caller logs and carries on; it never throws.
    /// </summary>
    bool Enqueue(EmailMessage message);
}
