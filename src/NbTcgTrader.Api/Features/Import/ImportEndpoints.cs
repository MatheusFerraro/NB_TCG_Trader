using System.Security.Claims;
using NbTcgTrader.Api.Common.Extensions;
using NbTcgTrader.Api.Common.Filters;

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

        // Reconciliation (#16): list the rows a job still needs a decision on, then
        // resolve each (pick a catalog card) or skip it. All owner-scoped; a job/row that
        // is not the caller's is an indistinguishable 404.
        group.MapGet("/jobs/{jobId:int}/rows",
                (int jobId, ClaimsPrincipal principal,
                        ListImportRowsHandler handler, CancellationToken ct) =>
                    handler.HandleAsync(jobId, principal, ct))
            .WithName("ListImportRows")
            .Produces<UnmatchedRowsResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/jobs/{jobId:int}/rows/{rowId:int}/resolve",
                (int jobId, int rowId, ResolveImportRowRequest request,
                        ClaimsPrincipal principal,
                        ResolveImportRowHandler handler, CancellationToken ct) =>
                    handler.HandleAsync(jobId, rowId, request, principal, ct))
            .WithName("ResolveImportRow")
            .WithValidation<ResolveImportRowRequest>()
            .Produces<ResolveImportRowResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/jobs/{jobId:int}/rows/{rowId:int}/skip",
                (int jobId, int rowId, ClaimsPrincipal principal,
                        SkipImportRowHandler handler, CancellationToken ct) =>
                    handler.HandleAsync(jobId, rowId, principal, ct))
            .WithName("SkipImportRow")
            .Produces<SkipImportRowResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return endpoints;
    }
}
