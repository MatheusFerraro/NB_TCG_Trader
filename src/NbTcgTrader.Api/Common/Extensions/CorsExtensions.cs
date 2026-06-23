namespace NbTcgTrader.Api.Common.Extensions;

/// <summary>
/// CORS wiring. Origins come from the <c>Cors:AllowedOrigins</c> configuration
/// array. The default is empty, so unless an environment supplies origins the
/// policy permits none — locked down by default (CLAUDE.md §10, §15).
/// </summary>
public static class CorsExtensions
{
    public const string PolicyName = "FrontendCors";

    public static IServiceCollection AddApiCors(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                      ?? [];

        services.AddCors(options =>
        {
            options.AddPolicy(PolicyName, policy =>
            {
                if (origins.Length == 0)
                {
                    // No origins configured: allow nothing (no Access-Control headers).
                    return;
                }

                policy
                    .WithOrigins(origins)   // explicit origins, never a wildcard
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials();
            });
        });

        return services;
    }
}
