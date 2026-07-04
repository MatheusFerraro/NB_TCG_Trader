using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using NbTcgTrader.Api.Common.Domain;
using NbTcgTrader.Api.Common.Persistence;

namespace NbTcgTrader.Api.Features.Import;

/// <summary>
/// Upload limits (BACKLOG #14, AGENTS.md §15): cap file size and row count so a
/// hostile or accidental upload cannot exhaust the server. Non-secret config under
/// the <c>Import</c> section; defaults suit the MVP's "small files, synchronous
/// processing" stance (§9).
/// </summary>
public sealed class ImportOptions
{
    public const string SectionName = "Import";

    public long MaxFileBytes { get; set; } = 1024 * 1024;

    public int MaxRows { get; set; } = 1000;
}

/// <summary>The created import job as returned to its owner.</summary>
public sealed record ImportJobResponse(
    int Id,
    string FileName,
    ImportStatus Status,
    int RowsTotal,
    int RowsMatched,
    int RowsUnmatched,
    DateTimeOffset CreatedAt)
{
    public static ImportJobResponse From(ImportJob job) => new(
        job.Id,
        job.FileName,
        job.Status,
        job.RowsTotal,
        job.RowsMatched,
        job.RowsUnmatched,
        job.CreatedAt);
}

/// <summary>
/// Accepts a template upload, guards it (extension + content-type allowlist, size cap,
/// row cap — §15), parses it with <see cref="ImportFileParser"/>, and persists the
/// <see cref="ImportJob"/> with its rows. Any file or row error rejects the whole
/// upload as a 400 ValidationProblem, so a job only ever exists fully parsed. The job
/// lands <see cref="ImportStatus.Pending"/> with every row Unmatched — catalog
/// matching is the next step (#15).
/// </summary>
public sealed class UploadImportHandler(
    AppDbContext db,
    IOptions<ImportOptions> options,
    ILogger<UploadImportHandler> logger)
{
    private static readonly string[] CsvContentTypes =
        ["text/csv", "application/csv", "application/vnd.ms-excel"];

    private static readonly string[] XlsxContentTypes =
        ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"];

    public async Task<IResult> HandleAsync(
        IFormFile? file,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var userId = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (userId is null)
        {
            return Results.Unauthorized();
        }

        if (file is null || file.Length == 0)
        {
            return FileProblem("A .csv or .xlsx file is required in the 'file' form field.");
        }

        // Never trust the client-supplied filename (§15): strip any path, keep only a
        // sanitized name for display, and derive the format from its extension.
        var fileName = SanitizeFileName(file.FileName);
        var extension = Path.GetExtension(fileName).ToLowerInvariant();

        if (extension is not (".csv" or ".xlsx"))
        {
            return FileProblem("Only .csv and .xlsx files are accepted.");
        }

        var contentType = file.ContentType.Split(';')[0].Trim();
        var allowedContentTypes = extension == ".csv" ? CsvContentTypes : XlsxContentTypes;
        if (!allowedContentTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase))
        {
            return FileProblem(
                $"The content type '{contentType}' is not valid for a {extension} file.");
        }

        var limits = options.Value;
        if (file.Length > limits.MaxFileBytes)
        {
            return FileProblem(
                $"The file is {file.Length:N0} bytes; the maximum is {limits.MaxFileBytes:N0} bytes.");
        }

        ImportParseResult parsed;
        await using (var stream = file.OpenReadStream())
        {
            // ClosedXML needs a seekable stream; the size cap makes buffering safe.
            if (extension == ".xlsx")
            {
                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer, cancellationToken);
                buffer.Position = 0;
                parsed = ImportFileParser.ParseXlsx(buffer, limits.MaxRows);
            }
            else
            {
                parsed = ImportFileParser.ParseCsv(stream, limits.MaxRows);
            }
        }

        if (parsed.HasErrors)
        {
            var errors = new Dictionary<string, string[]>();
            if (parsed.FileErrors.Count > 0)
            {
                errors["file"] = [.. parsed.FileErrors];
            }

            if (parsed.RowErrors.Count > 0)
            {
                errors["rows"] = [.. parsed.RowErrors];
            }

            return Results.ValidationProblem(errors);
        }

        var job = new ImportJob
        {
            UserId = userId,
            FileName = fileName,
            Status = ImportStatus.Pending,
            RowsTotal = parsed.Rows.Count,
            RowsMatched = 0,
            RowsUnmatched = parsed.Rows.Count,
            CreatedAt = DateTimeOffset.UtcNow,
            Rows = parsed.Rows,
        };

        db.ImportJobs.Add(job);
        await db.SaveChangesAsync(cancellationToken);

        // Counts only — never the parsed card list (§10).
        logger.LogInformation(
            "User {UserId} uploaded import job {ImportJobId} with {RowsTotal} rows",
            userId, job.Id, job.RowsTotal);

        return Results.Created($"/import/jobs/{job.Id}", ImportJobResponse.From(job));
    }

    private static IResult FileProblem(string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = [message] });

    private static string SanitizeFileName(string? clientFileName)
    {
        var name = Path.GetFileName(clientFileName ?? string.Empty).Trim();

        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }

        // Fits the ImportJob.FileName column (260); keep the extension when truncating.
        if (name.Length > 260)
        {
            var extension = Path.GetExtension(name);
            name = string.Concat(
                name.AsSpan(0, 260 - extension.Length), extension);
        }

        return name;
    }
}
