using Microsoft.AspNetCore.Http.Features;
using NbTcgTrader.Api.Features.Import;

namespace NbTcgTrader.Api.Common.Extensions;

/// <summary>
/// Import slice wiring (BACKLOG #13/#14): upload limits and the upload handler.
/// Validators (none yet — the upload validates its file in the handler) would be
/// picked up by the assembly-wide FluentValidation scan.
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
        return services;
    }
}
