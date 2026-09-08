using System.Net;

namespace NbTcgTrader.Api.Common.Email;

/// <summary>
/// Renders the three auth emails (issue #69): verify your address, reset your
/// password, and the security notice after a password change.
/// </summary>
/// <remarks>
/// Plain string templates rather than a view engine — three static layouts do not
/// justify Razor in a Web API. Every interpolated value is HTML-encoded, so a display
/// name of <c>&lt;script&gt;</c> renders as text; links come from
/// <see cref="EmailLinkBuilder"/> and are URL-encoded there.
/// </remarks>
public static class EmailTemplates
{
    public const string VerificationKind = "email-verification";
    public const string PasswordResetKind = "password-reset";
    public const string PasswordChangedKind = "password-changed";

    private const string BrandName = "NB TCG Trader";

    public static EmailMessage Verification(string to, string displayName, string verifyLink) =>
        new(
            To: to,
            Subject: $"Confirm your {BrandName} email address",
            HtmlBody: Layout(
                heading: "Confirm your email",
                greeting: displayName,
                intro: "Thanks for signing up. Confirm this address to finish setting up your account.",
                buttonLabel: "Confirm my email",
                buttonUrl: verifyLink,
                footer: "This link expires in a few hours. If you didn't create an account, ignore this email."),
            TextBody: Text(
                greeting: displayName,
                intro: "Thanks for signing up. Confirm your email address to finish setting up your account:",
                link: verifyLink,
                footer: "This link expires in a few hours. If you didn't create an account, ignore this email."),
            Kind: VerificationKind);

    public static EmailMessage PasswordReset(string to, string displayName, string resetLink) =>
        new(
            To: to,
            Subject: $"Reset your {BrandName} password",
            HtmlBody: Layout(
                heading: "Reset your password",
                greeting: displayName,
                intro: "We received a request to reset your password. Choose a new one with the link below.",
                buttonLabel: "Choose a new password",
                buttonUrl: resetLink,
                footer: "This link expires shortly and can be used once. If you didn't ask for it, "
                        + "no action is needed — your password has not changed."),
            TextBody: Text(
                greeting: displayName,
                intro: "We received a request to reset your password. Choose a new one here:",
                link: resetLink,
                footer: "This link expires shortly and can be used once. If you didn't ask for it, "
                        + "no action is needed — your password has not changed."),
            Kind: PasswordResetKind);

    public static EmailMessage PasswordChanged(string to, string displayName, string loginLink) =>
        new(
            To: to,
            Subject: $"Your {BrandName} password was changed",
            HtmlBody: Layout(
                heading: "Your password was changed",
                greeting: displayName,
                intro: "Your password was just changed and every signed-in session was signed out. "
                       + "If this was you, there is nothing else to do.",
                buttonLabel: "Sign in",
                buttonUrl: loginLink,
                footer: "If this wasn't you, reset your password immediately and contact us."),
            TextBody: Text(
                greeting: displayName,
                intro: "Your password was just changed and every signed-in session was signed out. "
                       + "If this was you, there is nothing else to do. Sign in here:",
                link: loginLink,
                footer: "If this wasn't you, reset your password immediately and contact us."),
            Kind: PasswordChangedKind);

    /// <summary>
    /// One inline-styled layout for all three messages. Inline CSS and a table-free
    /// single column keep it readable in clients that strip &lt;style&gt; blocks.
    /// </summary>
    private static string Layout(
        string heading,
        string greeting,
        string intro,
        string buttonLabel,
        string buttonUrl,
        string footer)
    {
        // The URL is already percent-encoded by EmailLinkBuilder, so it only needs
        // HTML-escaping to sit safely in an attribute: '&' between query parameters
        // becomes '&amp;', and a quote cannot close the attribute early. Percent-
        // encoding it again here would escape the "://" and break the link.
        var href = Encode(buttonUrl);

        return $"""
            <!doctype html>
            <html lang="en">
              <body style="margin:0;padding:24px;background:#f5f5f7;font-family:system-ui,-apple-system,'Segoe UI',sans-serif;color:#1c1c1e;">
                <div style="max-width:520px;margin:0 auto;background:#ffffff;border-radius:12px;padding:32px;">
                  <p style="margin:0 0 24px;font-size:14px;font-weight:600;letter-spacing:.04em;text-transform:uppercase;color:#6b6b70;">{Encode(BrandName)}</p>
                  <h1 style="margin:0 0 16px;font-size:22px;line-height:1.3;">{Encode(heading)}</h1>
                  <p style="margin:0 0 8px;font-size:16px;">Hi {Encode(greeting)},</p>
                  <p style="margin:0 0 24px;font-size:16px;line-height:1.5;">{Encode(intro)}</p>
                  <p style="margin:0 0 24px;">
                    <a href="{href}" style="display:inline-block;background:#1f6feb;color:#ffffff;text-decoration:none;padding:12px 20px;border-radius:8px;font-size:16px;font-weight:600;">{Encode(buttonLabel)}</a>
                  </p>
                  <p style="margin:0 0 24px;font-size:13px;line-height:1.5;color:#6b6b70;">
                    If the button doesn't work, copy and paste this link into your browser:<br />
                    <span style="word-break:break-all;">{Encode(buttonUrl)}</span>
                  </p>
                  <p style="margin:0;font-size:13px;line-height:1.5;color:#6b6b70;">{Encode(footer)}</p>
                </div>
              </body>
            </html>
            """;
    }

    private static string Text(string greeting, string intro, string link, string footer) =>
        $"""
        {BrandName}

        Hi {greeting},

        {intro}

        {link}

        {footer}
        """;

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
