using Microsoft.AspNetCore.Http.Features;
using NbTcgTrader.Api.Features.Import;

namespace NbTcgTrader.Api.Common.Extensions;

/// <summary>
/// Import slice wiring (BACKLOG #13/#14/#16): upload limits, the upload handler, and the
/// reconciliation handlers. Validators (e.g. the resolve request) are picked up by the
/// assembly-wide FluentValidation scan in <see cref="AuthenticationExtensions"/>.
/// </summary>
public static class ImportExtensions
{
    public static IServiceCollection AddApiImport(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<ImportOptions>()
            .Bind(configuration.GetSection(ImportOptions.SectionName))
            .Validate(o => o.MaxFileBytes > 0, "Import:MaxFileBytes must be positive.")
            .Validate(o => o.MaxRows > 0, "Import:MaxRows must be positive.")
            .ValidateOnStart();

        // Cap what the multipart reader will buffer slightly above the app-level file
        // cap, so an oversized upload gets the handler's clear 400 rather than the
        // framework's opaque failure, while a grossly oversized body is still cut off.
        var maxFileBytes = configuration
            .GetSection(ImportOptions.SectionName)
            .GetValue<long?>(nameof(ImportOptions.MaxFileBytes)) ?? new ImportOptions().MaxFileBytes;
        services.Configure<FormOptions>(o =>
            o.MultipartBodyLengthLimit = maxFileBytes + 64 * 1024);

        services.AddScoped<ImportRowMatcher>();
        services.AddScoped<UploadImportHandler>();

        // Reconciliation handlers (#16): list unmatched rows, resolve, skip, and suggest
        // ranked catalog candidates for an unmatched row (#66 follow-up).
        services.AddScoped<ListImportRowsHandler>();
        services.AddScoped<ResolveImportRowHandler>();
        services.AddScoped<SkipImportRowHandler>();
        services.AddScoped<SuggestRowCandidatesHandler>();
        return services;
    }
}
