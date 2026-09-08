namespace NbTcgTrader.Api.Common.Email;

/// <summary>
/// The <c>Email:Enabled=false</c> kill switch (issue #69). Accepts every message and
/// delivers none, so exhausting the provider's free quota — or an incident — is a
/// one-variable change that leaves registration and password reset working rather
/// than erroring.
/// </summary>
public sealed class DisabledEmailSender(ILogger<DisabledEmailSender> logger) : IEmailSender
{
    public Task<EmailSendResult> SendAsync(
        EmailMessage message, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Email sending is disabled (Email:Enabled=false); dropped the {Kind} email",
            message.Kind);

        return Task.FromResult(EmailSendResult.Success());
    }
}
