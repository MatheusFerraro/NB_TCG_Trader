using System.Net;
using System.Text.Json;
using Shouldly;

namespace NbTcgTrader.Tests;

// Integration tests for the cross-cutting pipeline wired in Program.cs (#3):
// health endpoint, and the ProblemDetails error contract on a failure path.
[Collection(IntegrationTestCollection.Name)]
public class CrossCuttingTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public CrossCuttingTests(ApiWebApplicationFactory factory) =>
        _factory = factory;

    [Fact]
    public async Task Health_endpoint_returns_ok()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Unknown_route_returns_problem_details()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/does-not-exist");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType
            .ShouldBe("application/problem+json");
    }

    // The test host runs in Development, where Cors:AllowedOrigins = [http://localhost:5173].

    [Fact]
    public async Task Cors_preflight_from_allowed_origin_gets_allow_origin_header()
    {
        var client = _factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Options, "/health");
        request.Headers.Add("Origin", "http://localhost:5173");
        request.Headers.Add("Access-Control-Request-Method", "GET");

        var response = await client.SendAsync(request);

        response.Headers.GetValues("Access-Control-Allow-Origin")
            .ShouldContain("http://localhost:5173");
    }

    [Fact]
    public async Task Cors_preflight_from_disallowed_origin_gets_no_allow_origin_header()
    {
        var client = _factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Options, "/health");
        request.Headers.Add("Origin", "http://evil.example.com");
        request.Headers.Add("Access-Control-Request-Method", "GET");

        var response = await client.SendAsync(request);

        response.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
    }

    // The docs surface (OpenAPI JSON + Scalar UI) is Development-only; the test
    // host runs in Development, so both are served here (#4, CLAUDE.md §10).

    [Fact]
    public async Task OpenApi_document_declares_the_bearer_security_scheme()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);

        doc.RootElement.GetProperty("paths")
            .TryGetProperty("/health", out _).ShouldBeTrue(); 

        var bearer = doc.RootElement
            .GetProperty("components")
            .GetProperty("securitySchemes")
            .GetProperty("Bearer");

        bearer.GetProperty("type").GetString().ShouldBe("http");
        bearer.GetProperty("scheme").GetString().ShouldBe("bearer");
        bearer.GetProperty("bearerFormat").GetString().ShouldBe("JWT");
    }

    [Fact]
    public async Task Scalar_ui_is_served()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/scalar");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        
        response.Content.Headers.ContentType?.MediaType
            .ShouldBe("text/html");
        
        var html = await response.Content.ReadAsStringAsync();
        html.ShouldContain("NB TCG Trader API");
    }
}
