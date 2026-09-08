namespace NbTcgTrader.Api.Common.Email;

/// <summary>
/// A single transactional email, already rendered. Handlers build one of these
/// from <see cref="EmailTemplates"/> and hand it to <see cref="IEmailDispatcher"/>;
/// no provider type ever crosses a slice boundary.
/// </summary>
/// <param name="To">Recipient address.</param>
/// <param name="Subject">Subject line.</param>
/// <param name="HtmlBody">HTML part.</param>
/// <param name="TextBody">Plain-text part, for clients that refuse HTML.</param>
/// <param name="Kind">
/// Short, non-identifying label ("email-verification", "password-reset") used in
/// logs and file-drop names. Never contains a token or any message content.
/// </param>
public sealed record EmailMessage(
    string To,
    string Subject,
    string HtmlBody,
    string TextBody,
    string Kind);

/// <summary>Outcome of a single send attempt. Never carries the message body.</summary>
/// <param name="Succeeded">True when the provider accepted the message.</param>
/// <param name="ProviderMessageId">Provider-assigned id, when one was returned.</param>
/// <param name="Error">Short failure reason for logs. Never a token or a body.</param>
public sealed record EmailSendResult(bool Succeeded, string? ProviderMessageId, string? Error)
{
    public static EmailSendResult Success(string? providerMessageId = null) =>
        new(true, providerMessageId, null);

    public static EmailSendResult Failure(string error) => new(false, null, error);
}
