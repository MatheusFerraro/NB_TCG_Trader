using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using NbTcgTrader.Api.Common.Extensions;
using Shouldly;

namespace NbTcgTrader.Tests;

// DB-free coverage of GET /catalog/cards validation (BACKLOG #9, #72). The search now
// reads from Postgres, so result/paging/trigram behavior lives in the Testcontainers
// integration test (CatalogSearchTests). Validation short-circuits before the handler
// touches the database, so these cases need no database at all.
public sealed class CatalogEndpointsTests
{
    private const string Placeholder = "https://api.example.com/assets/card-placeholder.svg";

    [Fact]
    public async Task Search_with_invalid_page_returns_400_problem_details()
    {
        using var factory = new CatalogApiFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/catalog/cards?query=charizard&page=0");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task Search_without_any_filter_returns_400_problem_details()
    {
        using var factory = new CatalogApiFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/catalog/cards");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    // Boots the real app without opening a database: a filterless/invalid request is
    // rejected by validation before the handler runs, so migrations stay off and a
    // throwaway connection string satisfies the fail-fast startup guard.
    private sealed class CatalogApiFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Database:ApplyMigrationsOnStartup", "false");
            builder.UseSetting(
                "ConnectionStrings:Default",
                "Host=localhost;Port=5432;Database=nbtcg_test_unused;Username=test;Password=test");
            builder.UseSetting("Jwt:SigningKey", TestJwt.SigningKey);
            builder.UseSetting("Jwt:Issuer", TestJwt.Issuer);
            builder.UseSetting("Jwt:Audience", TestJwt.Audience);

            // Registering a user now queues a verification email (#69). Tests do not
            // exercise it, so pin the kill switch off: nothing is rendered, nothing
            // is written to a drop directory, and a developer's user-secrets cannot
            // point a test run at a real provider.
            builder.UseSetting("Email:Enabled", "false");
            builder.UseSetting("Catalog:PlaceholderImageUrl", Placeholder);

            // Developer user-secrets may set Admin:SeedEmails; pin it empty so the
            // admin seed doesn't reach for the (unreachable) database on boot.
            builder.UseSetting(AdminSeedExtensions.SeedEmailsKey, "");
        }
    }
}
