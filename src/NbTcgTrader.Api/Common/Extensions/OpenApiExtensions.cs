using Scalar.AspNetCore;

namespace NbTcgTrader.Api.Common.Extensions;

/// <summary>
/// OpenAPI document generation plus the Scalar reference UI (CLAUDE.md §10).
/// The document is enriched with a JWT Bearer scheme via
/// <see cref="BearerSecuritySchemeTransformer"/>. Both the raw document and the
/// UI are exposed in Development only — there is no docs surface on the deployed
/// API (CLAUDE.md §15).
/// </summary>
public static class OpenApiExtensions
{
    public static IServiceCollection AddApiOpenApi(this IServiceCollection services)
    {
        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
        });

        return services;
    }

    /// <summary>
    /// Maps the OpenAPI JSON document and the Scalar UI at <c>/scalar</c>.
    /// Call inside a Development-only guard.
    /// </summary>
    public static IEndpointRouteBuilder MapApiDocs(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapOpenApi();
        endpoints.MapScalarApiReference(options =>
        {
            options
                .WithTitle("NB TCG Trader API")
                .WithTheme(ScalarTheme.Default);
        });

        return endpoints;
    }
}
