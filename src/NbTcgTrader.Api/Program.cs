using System.Diagnostics;
using NbTcgTrader.Api.Common.Errors;
using NbTcgTrader.Api.Common.Extensions;
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

    builder.Services.AddOpenApi();

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

    var app = builder.Build();

    // Exception handling first so it wraps everything downstream.
    app.UseExceptionHandler();
    app.UseStatusCodePages();

    app.UseSerilogRequestLogging();

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
    }
    else
    {
        app.UseHsts();
    }

    app.UseHttpsRedirection();
    app.UseCors(CorsExtensions.PolicyName);
    app.UseRateLimiter();

    // Liveness probe. Feature slices register their own endpoints under Features/.
    app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
        .WithName("HealthCheck");

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
