namespace NbTcgTrader.Api.Common.Extensions;

/// <summary>
/// Baseline response security headers for the deployed API surface (CLAUDE.md §15).
/// The API only ever returns JSON (and a couple of static assets under wwwroot);
/// it is never a document host, so a tight CSP that forbids scripts and framing is
/// safe. The Scalar docs UI is Development-only and mounted before this runs, so it
/// is unaffected — these headers are wired only outside Development.
/// </summary>
public static class SecurityHeadersExtensions
{
    // No scripts, styles, or embedding: a JSON API needs none of them. `frame-ancestors
    // 'none'` (plus X-Frame-Options for older agents) blocks clickjacking; `base-uri`
    // and `form-action 'none'` shut down injected <base>/<form> tricks.
    private const string ContentSecurityPolicy =
        "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";

    public static IApplicationBuilder UseApiSecurityHeaders(this IApplicationBuilder app) =>
        app.Use((context, next) =>
        {
            var headers = context.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers["Content-Security-Policy"] = ContentSecurityPolicy;
            return next();
        });
}
