using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Shouldly;
using Testcontainers.PostgreSql;

namespace NbTcgTrader.Tests;

// End-to-end coverage of the Auth slice (BACKLOG #6) against a real Postgres via
// Testcontainers: register/login issue tokens, refresh rotates (old token dies),
// `me` returns the caller, and invalid input → 400 ProblemDetails. Skips cleanly
// when Docker is unavailable. The container is shared; each test gets its own host
// (so its own auth rate-limit bucket) and a unique email to stay isolated.
[Collection(IntegrationTestCollection.Name)]
public sealed class AuthEndpointsTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly List<AuthApiFactory> _factories = [];
    private PostgreSqlContainer? _postgres;
    private string? _dockerUnavailableReason;

    public async Task InitializeAsync()
    {
        try
        {
            _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
            await _postgres.StartAsync();
        }
        catch (Exception ex)
        {
            _postgres = null;
            _dockerUnavailableReason = $"Docker is not available: {ex.Message}";
        }
    }

    public async Task DisposeAsync()
    {
        foreach (var factory in _factories)
        {
            await factory.DisposeAsync();
        }

        if (_postgres is not null)
        {
            await _postgres.DisposeAsync();
        }
    }

    [SkippableFact]
    public async Task Register_returns_201_with_tokens_and_user()
    {
        var client = CreateClient();
        var email = UniqueEmail();

        var response = await client.PostAsJsonAsync("/auth/register", ValidRegister(email));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        var auth = await ReadAuthAsync(response);
        auth.AccessToken.ShouldNotBeNullOrWhiteSpace();
        auth.RefreshToken.ShouldNotBeNullOrWhiteSpace();
        auth.User.Email.ShouldBe(email);
        auth.User.DisplayName.ShouldBe("Ash");
    }

    [SkippableFact]
    public async Task Register_with_invalid_input_returns_400_problem_details()
    {
        var client = CreateClient();

        var response = await client.PostAsJsonAsync(
            "/auth/register", ValidRegister("not-an-email") with { Password = "weak" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    [SkippableFact]
    public async Task Register_duplicate_email_returns_400()
    {
        var client = CreateClient();
        var email = UniqueEmail();

        (await client.PostAsJsonAsync("/auth/register", ValidRegister(email)))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        var duplicate = await client.PostAsJsonAsync("/auth/register", ValidRegister(email));

        duplicate.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [SkippableFact]
    public async Task Login_with_valid_credentials_returns_tokens()
    {
        var client = CreateClient();
        var email = UniqueEmail();
        await client.PostAsJsonAsync("/auth/register", ValidRegister(email));

        var response = await client.PostAsJsonAsync(
            "/auth/login", new { email, password = Password });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var auth = await ReadAuthAsync(response);
        auth.AccessToken.ShouldNotBeNullOrWhiteSpace();
        auth.RefreshToken.ShouldNotBeNullOrWhiteSpace();
    }

    [SkippableFact]
    public async Task Login_with_wrong_password_returns_401()
    {
        var client = CreateClient();
        var email = UniqueEmail();
        await client.PostAsJsonAsync("/auth/register", ValidRegister(email));

        var response = await client.PostAsJsonAsync(
            "/auth/login", new { email, password = "WrongPassword!9" });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [SkippableFact]
    public async Task Refresh_rotates_token_and_old_token_is_rejected()
    {
        var client = CreateClient();
        var email = UniqueEmail();
        var registered = await ReadAuthAsync(
            await client.PostAsJsonAsync("/auth/register", ValidRegister(email)));

        var refreshed = await client.PostAsJsonAsync(
            "/auth/refresh", new { refreshToken = registered.RefreshToken });

        refreshed.StatusCode.ShouldBe(HttpStatusCode.OK);
        var rotated = await ReadAuthAsync(refreshed);
        rotated.RefreshToken.ShouldNotBe(registered.RefreshToken);

        // The original token was rotated out: replaying it must fail.
        var replay = await client.PostAsJsonAsync(
            "/auth/refresh", new { refreshToken = registered.RefreshToken });
        replay.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // ...and the new token still works.
        var again = await client.PostAsJsonAsync(
            "/auth/refresh", new { refreshToken = rotated.RefreshToken });
        again.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [SkippableFact]
    public async Task Me_returns_current_user_with_a_valid_token()
    {
        var client = CreateClient();
        var email = UniqueEmail();
        var auth = await ReadAuthAsync(
            await client.PostAsJsonAsync("/auth/register", ValidRegister(email)));

        var request = new HttpRequestMessage(HttpMethod.Get, "/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var user = await response.Content.ReadFromJsonAsync<UserDto>(Json);
        user.ShouldNotBeNull();
        user.Email.ShouldBe(email);
        user.Id.ShouldBe(auth.User.Id);
    }

    [SkippableFact]
    public async Task Me_without_a_token_returns_401()
    {
        var client = CreateClient();

        var response = await client.GetAsync("/auth/me");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // A fresh host per test => a fresh (non-partitioned) auth rate-limit bucket, so
    // one test's requests can't exhaust the 10/min auth limit for another. The host
    // applies migrations on startup, against the already-running shared container.
    private HttpClient CreateClient()
    {
        Skip.If(_dockerUnavailableReason is not null, _dockerUnavailableReason);

        var factory = new AuthApiFactory(_postgres!.GetConnectionString());
        _factories.Add(factory);
        return factory.CreateClient();
    }

    private const string Password = "Sup3rSecret!Pwd";

    private static string UniqueEmail() => $"user-{Guid.NewGuid():N}@example.com";

    private static RegisterRequestDto ValidRegister(string email) => new(
        Email: email,
        Password: Password,
        DisplayName: "Ash",
        City: "Moncton",
        Country: "Canada",
        ContactEmail: null,
        DiscordHandle: null,
        InstagramHandle: null);

    private static async Task<AuthDto> ReadAuthAsync(HttpResponseMessage response)
    {
        var auth = await response.Content.ReadFromJsonAsync<AuthDto>(Json);
        auth.ShouldNotBeNull();
        return auth;
    }

    // Local DTOs so the test asserts the over-the-wire JSON contract, independent
    // of the API's internal record shapes.
    private sealed record RegisterRequestDto(
        string Email,
        string Password,
        string DisplayName,
        string? City,
        string? Country,
        string? ContactEmail,
        string? DiscordHandle,
        string? InstagramHandle);

    private sealed record AuthDto(string AccessToken, string RefreshToken, UserDto User);

    private sealed record UserDto(string Id, string Email, string DisplayName);

    // Boots the real app against the Testcontainers Postgres, in Development so
    // startup migrations run, with a throwaway Jwt config.
    private sealed class AuthApiFactory(string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Database:ApplyMigrationsOnStartup", "true");
            builder.UseSetting("ConnectionStrings:Default", connectionString);
            builder.UseSetting("Jwt:SigningKey", TestJwt.SigningKey);
            builder.UseSetting("Jwt:Issuer", TestJwt.Issuer);
            builder.UseSetting("Jwt:Audience", TestJwt.Audience);
        }
    }
}
