namespace NbTcgTrader.Api.Features.Import;

/// <summary>
/// Maps the Import slice endpoints (CLAUDE.md §6/§9, BACKLOG #13). Import is
/// owner-scoped, so the group requires authorization by default; the template
/// download opts out because it is public, static content with no user data.
/// The strict import rate-limit policy is reserved for the expensive upload
/// endpoint (#14) — the template is covered by the global per-IP limiter.
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
                    ImportTemplate.CsvBytes,
                    ImportTemplate.ContentType,
                    ImportTemplate.FileName))
            .AllowAnonymous()
            .WithName("GetImportTemplate")
            .Produces(StatusCodes.Status200OK, contentType: ImportTemplate.ContentType);

        return endpoints;
    }
}
