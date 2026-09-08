using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace NbTcgTrader.Api.Common.Email;

/// <summary>
/// Builds the absolute frontend links embedded in emails, and encodes the Identity
/// tokens they carry (issue #69).
/// </summary>
/// <remarks>
/// Identity's tokens are base64 containing <c>+ / =</c>, which do not survive a query
/// string intact. They are base64url-encoded here and decoded by the matching handler,
/// so a copy-pasted link keeps working. The frontend origin comes from configuration —
/// never from a request header — so a Host-header injection cannot rewrite the link a
/// user is told to click.
/// </remarks>
public sealed class EmailLinkBuilder(IOptions<EmailOptions> options)
{
    private readonly EmailOptions _options = options.Value;

    public static string EncodeToken(string token) =>
        WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

    /// <summary>
    /// Reverses <see cref="EncodeToken"/>. Returns null for anything that is not
    /// valid base64url UTF-8 — a malformed link is a 400, never an exception.
    /// </summary>
    public static string? TryDecodeToken(string? encoded)
    {
        if (string.IsNullOrWhiteSpace(encoded))
        {
            return null;
        }

        try
        {
            return Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(encoded));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public string BuildVerifyEmailLink(string userId, string token) =>
        Build("/verify-email", new Dictionary<string, string?>
        {
            ["userId"] = userId,
            ["token"] = EncodeToken(token),
        });

    public string BuildResetPasswordLink(string email, string token) =>
        Build("/reset-password", new Dictionary<string, string?>
        {
            ["email"] = email,
            ["token"] = EncodeToken(token),
        });

    public string BuildLoginLink() => Build("/login", new Dictionary<string, string?>());

    private string Build(string path, IDictionary<string, string?> query)
    {
        var baseUrl = _options.FrontendBaseUrl.TrimEnd('/');
        return QueryHelpers.AddQueryString($"{baseUrl}{path}", query);
    }
}
