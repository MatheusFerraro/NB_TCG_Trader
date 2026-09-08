using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using NbTcgTrader.Api.Common.Domain;
using NbTcgTrader.Api.Common.Email;

namespace NbTcgTrader.Api.Features.Auth;

/// <summary>
/// Builds and queues the Auth slice's three emails (issue #69). Token minting lives
/// here so registration, resend-verification, and forgot-password all produce tokens
/// the matching endpoints can validate — one place, one contract.
/// </summary>
/// <remarks>
/// Tokens come from ASP.NET Core Identity's own providers (Data Protection backed,
/// time-limited, single-use against the user's security stamp). They are never
/// logged: every log line here names the kind of email and the user id, nothing more
/// (CLAUDE.md §15).
/// </remarks>
public sealed class AuthEmailNotifier(
    UserManager<AppUser> users,
    IEmailDispatcher dispatcher,
    EmailLinkBuilder links,
    IOptions<EmailOptions> options,
    ILogger<AuthEmailNotifier> logger)
{
    private readonly EmailOptions _options = options.Value;

    /// <summary>Whether registering should queue a verification email.</summary>
    public bool VerificationOnRegisterEnabled => _options.SendVerificationOnRegister;

    public async Task SendVerificationAsync(AppUser user)
    {
        if (string.IsNullOrWhiteSpace(user.Email))
        {
            return;
        }

        var token = await users.GenerateEmailConfirmationTokenAsync(user);
        var link = links.BuildVerifyEmailLink(user.Id, token);

        Queue(EmailTemplates.Verification(user.Email, user.DisplayName, link), user.Id);
    }

    public async Task SendPasswordResetAsync(AppUser user)
    {
        if (string.IsNullOrWhiteSpace(user.Email))
        {
            return;
        }

        var token = await users.GeneratePasswordResetTokenAsync(user);
        var link = links.BuildResetPasswordLink(user.Email, token);

        Queue(EmailTemplates.PasswordReset(user.Email, user.DisplayName, link), user.Id);
    }

    public void SendPasswordChangedNotice(AppUser user)
    {
        if (string.IsNullOrWhiteSpace(user.Email))
        {
            return;
        }

        Queue(
            EmailTemplates.PasswordChanged(user.Email, user.DisplayName, links.BuildLoginLink()),
            user.Id);
    }

    private void Queue(EmailMessage message, string userId)
    {
        // Enqueue is fire-and-forget by design: the endpoint's response must not wait
        // on the provider, and a full outbox must not fail the user's request.
        if (dispatcher.Enqueue(message))
        {
            logger.LogInformation(
                "Queued {Kind} email for user {UserId}", message.Kind, userId);
        }
        else
        {
            logger.LogWarning(
                "Could not queue the {Kind} email for user {UserId}", message.Kind, userId);
        }
    }
}
