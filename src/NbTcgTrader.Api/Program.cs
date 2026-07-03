using System.Diagnostics;
using System.Text.Json.Serialization;
using NbTcgTrader.Api.Common.Errors;
using NbTcgTrader.Api.Common.Extensions;
using NbTcgTrader.Api.Features.Auth;
using NbTcgTrader.Api.Features.Catalog;
using NbTcgTrader.Api.Features.Collection;
using NbTcgTrader.Api.Features.Import;
using Serilog;

// Bootstrap logger: captures anything that fails before the host is built.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Structured logging, configured from the "Serilog" config section.
    builder.Services.AddSerilog((services, configuration) => configuration
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

    builder.Services.AddApiOpenApi();

    // Domain enums (Condition, Currency, ...) cross the wire as strings ("NM",
    // "CAD"), not opaque ints — for requests and responses alike.
    builder.Services.ConfigureHttpJsonOptions(options =>
        options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

    // Uniform error contract: ProblemDetails for every failure, with a traceId
    // for correlation. The handler keeps exception detail out of responses.
    builder.Services.AddProblemDetails(options =>
    {
        options.CustomizeProblemDetails = context =>
        {
            context.ProblemDetails.Instance ??=
                $"{context.HttpContext.Request.Method} {context.HttpContext.Request.Path}";
            context.ProblemDetails.Extensions["traceId"] =
                Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
        };
    });
    builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

    builder.Services.AddApiCors(builder.Configuration);
    builder.Services.AddApiRateLimiting();

    // EF Core (Postgres) + ASP.NET Core Identity stores.
    builder.Services.AddApiPersistence();

    // JWT bearer auth + Auth-slice services and validators.
    builder.Services.AddApiAuthentication(builder.Configuration);

    // External card-data client (pokemontcg.io) with caching + resilience (#8).
    builder.Services.AddApiCardCatalog(builder.Configuration);

    // Collection/binder slice handlers (#10).
    builder.Services.AddApiCollection();

    var app = builder.Build();

    // Apply migrations on startup in Development only, and only when enabled.
    // Tests run in Development too, so the flag lets the test host opt out and
    // avoid reaching for a database (CLAUDE.md §13).
    if (app.Environment.IsDevelopment() &&
        app.Configuration.GetValue("Database:ApplyMigrationsOnStartup", true))
    {
        await app.ApplyMigrationsAsync();
    }

    // Exception handling first so it wraps everything downstream.
    app.UseExceptionHandler();
    app.UseStatusCodePages();

    app.UseSerilogRequestLogging();

    if (app.Environment.IsDevelopment())
    {
        // OpenAPI JSON + Scalar UI at /scalar (Development only).
        app.MapApiDocs();
    }
    else
    {
        app.UseHsts();
    }

    app.UseHttpsRedirection();

    // Serve static assets (e.g. the catalog placeholder image at
    // /assets/card-placeholder.svg, #9) from wwwroot. Public and before auth.
    app.UseStaticFiles();

    app.UseCors(CorsExtensions.PolicyName);
    app.UseRateLimiter();

    app.UseAuthentication();
    app.UseAuthorization();

    // Liveness probe. Feature slices register their own endpoints under Features/.
    app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
        .WithName("HealthCheck");

    app.MapAuthEndpoints();
    app.MapCatalogEndpoints();
    app.MapCollectionEndpoints();
    app.MapImportEndpoints();

    app.Run();
}
// WebApplicationFactory (integration tests) and EF design-time tools deliberately
// stop the host: HostAbortedException, or the internal StopTheHostException thrown
// during Build(). Let those propagate instead of treating them as a fatal crash.
catch (Exception ex) when (ex is not HostAbortedException
                           && ex.GetType().Name is not "StopTheHostException")
{
    Log.Fatal(ex, "Application terminated unexpectedly");
    return 1; // non-zero so orchestrators/CI detect a failed boot
}
finally
{
    Log.CloseAndFlush();
}

return 0;

// Exposed so WebApplicationFactory<Program> can host the app in integration tests.
public partial class Program;
