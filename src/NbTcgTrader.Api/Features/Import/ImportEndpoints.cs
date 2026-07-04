using System.Security.Claims;
using NbTcgTrader.Api.Common.Extensions;

namespace NbTcgTrader.Api.Features.Import;

/// <summary>
/// Maps the Import slice endpoints (AGENTS.md §6/§9, BACKLOG #13/#14). Import is
/// owner-scoped, so the group requires authorization by default; the template
/// download opts out because it is public, static content with no user data.
/// The strict import rate-limit policy protects the expensive upload endpoint —
/// the template is covered by the global per-IP limiter.
/// </summary>
public static class ImportEndpoints
{
    public static IEndpointRouteBuilder MapImportEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/import")
            .WithTags("Import")
            .RequireAuthorization();

        group.MapGet("/template",
                () => Results.File(
                    ImportTemplate.GetCsvBytes(),
                    ImportTemplate.ContentType,
                    ImportTemplate.FileName))
            .AllowAnonymous()
            .WithName("GetImportTemplate")
            .Produces(StatusCodes.Status200OK, contentType: ImportTemplate.ContentType);

        group.MapPost("/jobs",
                (IFormFile? file, ClaimsPrincipal principal,
                        UploadImportHandler handler, CancellationToken ct) =>
                    handler.HandleAsync(file, principal, ct))
            .WithName("UploadImport")
            .RequireRateLimiting(RateLimitingExtensions.ImportPolicy)
            // JWT bearer auth only — no cookies, so form CSRF does not apply here.
            .DisableAntiforgery()
            .Accepts<IFormFile>("multipart/form-data")
            .Produces<ImportJobResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        return endpoints;
    }
}
