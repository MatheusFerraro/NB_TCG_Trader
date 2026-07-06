using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace NbTcgTrader.Tests;

// Verifies the forwarded-headers hardening (#24): behind a TLS-terminating
// ingress the API must adopt the client IP and scheme from X-Forwarded-* — so
// rate limiting partitions on the real client and HTTPS redirection sees the
// original scheme — while trusting only the single ingress hop so clients
// can't spoof their identity with their own forwarded headers.
[Collection(IntegrationTestCollection.Name)]
public class ForwardedHeadersTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ForwardedHeadersTests(ApiWebApplicationFactory factory) =>
        _factory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.AddTransient<IStartupFilter, ConnectionProbeStartupFilter>()));

    [Fact]
    public async Task Forwarded_for_from_the_ingress_becomes_the_client_ip()
    {
        var client = _factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("X-Forwarded-For", "203.0.113.9");

        var response = await client.SendAsync(request);

        response.Headers.GetValues("X-Probe-Client-Ip").ShouldBe(["203.0.113.9"]);
    }

    [Fact]
    public async Task Only_the_ingress_hop_is_trusted_not_client_supplied_entries()
    {
        var client = _factory.CreateClient();

        // A spoofing client sends its own X-Forwarded-For; the ingress appends the
        // address it actually saw. Only the right-most (ingress) entry may win.
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("X-Forwarded-For", "10.10.10.10, 203.0.113.9");

        var response = await client.SendAsync(request);

        response.Headers.GetValues("X-Probe-Client-Ip").ShouldBe(["203.0.113.9"]);
    }

    [Fact]
    public async Task Forwarded_proto_restores_the_original_scheme()
    {
        var client = _factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("X-Forwarded-Proto", "https");

        var response = await client.SendAsync(request);

        response.Headers.GetValues("X-Probe-Scheme").ShouldBe(["https"]);
    }

    /// <summary>
    /// Stamps the request's post-middleware connection view onto response headers
    /// so tests can observe what downstream components (rate limiter, HTTPS
    /// redirection) actually see. Startup-filter middleware runs before the
    /// pipeline in Program.cs, so the values are captured via OnStarting — after
    /// the forwarded-headers middleware has rewritten the connection.
    /// </summary>
    private sealed class ConnectionProbeStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
            app =>
            {
                app.Use(async (context, nextMiddleware) =>
                {
                    context.Response.OnStarting(() =>
                    {
                        context.Response.Headers["X-Probe-Client-Ip"] =
                            context.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
                        context.Response.Headers["X-Probe-Scheme"] = context.Request.Scheme;
                        return Task.CompletedTask;
                    });
                    await nextMiddleware();
                });
                next(app);
            };
    }
}
