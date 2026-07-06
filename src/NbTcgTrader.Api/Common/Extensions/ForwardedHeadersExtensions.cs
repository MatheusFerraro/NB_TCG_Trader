using Microsoft.AspNetCore.HttpOverrides;

namespace NbTcgTrader.Api.Common.Extensions;

/// <summary>
/// Forwarded-headers configuration for running behind a TLS-terminating ingress
/// (Azure Container Apps / Fly.io / Render, CLAUDE.md §4). The platform proxy
/// sets <c>X-Forwarded-For</c>/<c>X-Forwarded-Proto</c>; honouring them keeps
/// per-client rate limiting partitioned on the real client IP and lets
/// HTTPS redirection/HSTS see the original scheme instead of the proxy's
/// plain-HTTP hop.
/// </summary>
public static class ForwardedHeadersExtensions
{
    public static IServiceCollection AddApiForwardedHeaders(this IServiceCollection services)
    {
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders =
                ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

            // The container is reachable only through the platform ingress, whose
            // address is dynamic — the loopback-only defaults would reject it, so
            // trust the immediate hop instead of a fixed proxy list.
            options.KnownNetworks.Clear();
            options.KnownProxies.Clear();

            // Exactly one trusted hop: only the right-most X-Forwarded-For entry
            // (appended by the ingress) is honoured, so clients can't spoof their
            // IP — and thereby their rate-limit partition — by sending their own
            // forwarded headers.
            options.ForwardLimit = 1;
        });

        return services;
    }
}
