using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi.Models;

namespace NbTcgTrader.Api.Common.Extensions;

/// <summary>
/// Adds a Bearer security <em>requirement</em> to each operation whose endpoint
/// requires authorization, so the Scalar UI gates them behind the "Authorize" box
/// and the document reflects which routes need a token. Endpoints marked
/// <c>[AllowAnonymous]</c> (register/login/refresh) and unprotected ones (e.g.
/// <c>/health</c>) are left open. Complements
/// <see cref="BearerSecuritySchemeTransformer"/>, which only declares the scheme.
/// </summary>
internal sealed class AuthorizationOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata;

        var requiresAuth = metadata.OfType<IAuthorizeData>().Any()
                           && !metadata.OfType<IAllowAnonymous>().Any();

        if (!requiresAuth)
        {
            return Task.CompletedTask;
        }

        var scheme = new OpenApiSecurityScheme
        {
            Reference = new OpenApiReference
            {
                Type = ReferenceType.SecurityScheme,
                Id = BearerSecuritySchemeTransformer.SchemeName,
            },
        };

        operation.Security =
        [
            new OpenApiSecurityRequirement { [scheme] = Array.Empty<string>() },
        ];

        return Task.CompletedTask;
    }
}
