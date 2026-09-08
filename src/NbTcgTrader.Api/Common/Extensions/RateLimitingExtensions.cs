using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace NbTcgTrader.Api.Common.Extensions;

/// <summary>
/// Built-in ASP.NET Core rate limiting (CLAUDE.md §10, §15). Named policies are
/// attached to sensitive endpoints as they are added — auth (#6) and import
/// (#14) — via <c>.RequireRateLimiting(...)</c>. A lenient global limiter adds
/// defence-in-depth for everything else.
/// </summary>
public static class RateLimitingExtensions
{
    public const string AuthPolicy = "auth";
    public const string ImportPolicy = "import";
    public const string EmailPolicy = "email";

    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Defence-in-depth: cap total throughput per client IP.
            // NOTE: behind a proxy/ingress the real client IP requires
            // ForwardedHeaders middleware — wired with deployment (#24).
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 100,
                        Window = TimeSpan.FromMinutes(1),
                    }));

            // Stricter limit for auth endpoints (brute-force / credential stuffing).
            options.AddFixedWindowLimiter(AuthPolicy, limiter =>
            {
                limiter.PermitLimit = 10;
                limiter.Window = TimeSpan.FromMinutes(1);
            });

            // Endpoints that put a message in someone's inbox. Tighter than the auth
            // policy because the cost of abuse is not just CPU: every request burns a
            // slice of the provider's free monthly quota and can be used to mail-bomb
            // a third party who never signed up (issue #69).
            options.AddFixedWindowLimiter(EmailPolicy, limiter =>
            {
                limiter.PermitLimit = 3;
                limiter.Window = TimeSpan.FromMinutes(5);
            });

            // Import is expensive (file parse + catalog matching): keep it low.
            options.AddFixedWindowLimiter(ImportPolicy, limiter =>
            {
                limiter.PermitLimit = 5;
                limiter.Window = TimeSpan.FromMinutes(1);
            });
        });

        return services;
    }
}
