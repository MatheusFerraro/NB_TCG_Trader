using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi.Models;

namespace NbTcgTrader.Api.Common.Extensions;

/// <summary>
/// Registers a JWT <c>Bearer</c> security scheme on the OpenAPI document so the
/// Scalar UI renders an "Authorize" box (CLAUDE.md §10). This only declares the
/// scheme in <c>components.securitySchemes</c>; it does NOT add a global security
/// requirement, so anonymous endpoints (e.g. <c>/health</c>) are not mislabeled.
/// </summary>
/// <remarks>
/// This is the MS-documented <c>BearerSecuritySchemeTransformer</c> pattern,
/// simplified: the reference sample gates on <c>IAuthenticationSchemeProvider</c>
/// detecting a registered JWT scheme; the scheme is declared unconditionally here.
/// Per-endpoint security <em>requirements</em> (skipping <c>[AllowAnonymous]</c>
/// endpoints) are added by <see cref="AuthorizationOperationTransformer"/>.
/// </remarks>
internal sealed class BearerSecuritySchemeTransformer : IOpenApiDocumentTransformer
{
    public const string SchemeName = "Bearer";

    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes[SchemeName] = new OpenApiSecurityScheme
        {
            // type: http + scheme: bearer => the token is always read from the
            // Authorization header. `In` is only meaningful for apiKey schemes,
            // so it is intentionally omitted here.
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Paste a JWT access token. The 'Bearer ' prefix is added automatically.",
        };

        return Task.CompletedTask;
    }
}
