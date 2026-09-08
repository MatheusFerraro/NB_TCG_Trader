using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace NbTcgTrader.Api.Common.Email;

/// <summary>
/// Resend (https://resend.com) transactional sender — the free-tier provider chosen
/// for the MVP (issue #69). One POST to <c>/emails</c> with a bearer API key; quota
/// and domain-authentication notes live in README "Transactional email".
/// </summary>
/// <remarks>
/// Failures are swallowed into an <see cref="EmailSendResult"/> rather than thrown:
/// a provider outage must never turn a successful registration into a 500. Nothing
/// here logs the API key, the rendered body, or a token (CLAUDE.md §15).
/// </remarks>
public sealed class ResendEmailSender(
    HttpClient http,
    IOptions<EmailOptions> options,
    ILogger<ResendEmailSender> logger) : IEmailSender
{
    private readonly EmailOptions _options = options.Value;

    public async Task<EmailSendResult> SendAsync(
        EmailMessage message, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            // Misconfiguration, not a transient fault: say so once, clearly, without
            // echoing anything secret.
            logger.LogError(
                "Email:ApiKey is not configured; {Kind} email was not sent", message.Kind);
            return EmailSendResult.Failure("Email:ApiKey is not configured.");
        }

        var payload = new ResendSendRequest(
            From: FormatFrom(),
            To: [message.To],
            Subject: message.Subject,
            Html: message.HtmlBody,
            Text: message.TextBody,
            ReplyTo: _options.ReplyToAddress);

        try
        {
            using var response = await http.PostAsJsonAsync("emails", payload, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                // The provider's error body can be long and may echo the payload, so
                // only the status code reaches the log.
                logger.LogWarning(
                    "Resend rejected the {Kind} email with status {StatusCode}",
                    message.Kind,
                    (int)response.StatusCode);

                return EmailSendResult.Failure($"Provider returned {(int)response.StatusCode}.");
            }

            var body = await response.Content
                .ReadFromJsonAsync<ResendSendResponse>(cancellationToken);

            return EmailSendResult.Success(body?.Id);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(
                ex, "Resend was unreachable while sending the {Kind} email", message.Kind);
            return EmailSendResult.Failure("Provider unreachable.");
        }
    }

    /// <summary>
    /// Resend accepts either a bare address or RFC 5322 "Name &lt;addr&gt;". The
    /// display name is dropped when it contains a quote or angle bracket rather than
    /// escaped — a header must never be splittable by configuration.
    /// </summary>
    private string FormatFrom()
    {
        var name = _options.FromName;
        var safe = !string.IsNullOrWhiteSpace(name)
                   && name.IndexOfAny(['<', '>', '"', '\r', '\n']) < 0;

        return safe ? $"{name} <{_options.FromAddress}>" : _options.FromAddress;
    }

    private sealed record ResendSendRequest(
        [property: JsonPropertyName("from")] string From,
        [property: JsonPropertyName("to")] string[] To,
        [property: JsonPropertyName("subject")] string Subject,
        [property: JsonPropertyName("html")] string Html,
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("reply_to")] string? ReplyTo);

    private sealed record ResendSendResponse(
        [property: JsonPropertyName("id")] string? Id);
}
