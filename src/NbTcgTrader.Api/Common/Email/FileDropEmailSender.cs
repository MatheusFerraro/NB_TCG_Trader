using System.Text;
using Microsoft.Extensions.Options;

namespace NbTcgTrader.Api.Common.Email;

/// <summary>
/// Development sender: writes each message to a file under <c>Email:DropDirectory</c>
/// instead of delivering it. That keeps the verification and password-reset flows
/// fully testable locally — open the file, click the link — with no provider account,
/// no free-quota spend, and no risk of mailing a real person (issue #69).
/// </summary>
/// <remarks>
/// The rendered body contains a single-use token, so it goes to a gitignored file and
/// never to the log. Only the recipient, the kind, and the file path are logged.
/// </remarks>
public sealed class FileDropEmailSender(
    IOptions<EmailOptions> options,
    IHostEnvironment environment,
    ILogger<FileDropEmailSender> logger) : IEmailSender
{
    private readonly EmailOptions _options = options.Value;

    public async Task<EmailSendResult> SendAsync(
        EmailMessage message, CancellationToken cancellationToken)
    {
        try
        {
            var directory = Path.IsPathRooted(_options.DropDirectory)
                ? _options.DropDirectory
                : Path.Combine(environment.ContentRootPath, _options.DropDirectory);

            Directory.CreateDirectory(directory);

            // Sortable, collision-free, and built only from server-side values — the
            // recipient address never reaches the file name.
            var name = $"{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{message.Kind}-{Guid.NewGuid():N}.html";
            var path = Path.Combine(directory, name);

            var contents = new StringBuilder()
                .AppendLine("<!-- NB TCG Trader local email drop (no message was sent) -->")
                .AppendLine($"<!-- To: {message.To} -->")
                .AppendLine($"<!-- Subject: {message.Subject} -->")
                .AppendLine("<!-- Plain-text part:")
                .AppendLine(message.TextBody)
                .AppendLine("-->")
                .AppendLine(message.HtmlBody)
                .ToString();

            await File.WriteAllTextAsync(path, contents, cancellationToken);

            logger.LogInformation(
                "Wrote {Kind} email for {Recipient} to {Path} (file-drop sender; nothing was sent)",
                message.Kind,
                message.To,
                path);

            return EmailSendResult.Success(name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not write the {Kind} email to the drop directory", message.Kind);
            return EmailSendResult.Failure("Drop directory is not writable.");
        }
    }
}
