using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NbTcgTrader.Api.Common.Email;
using Shouldly;
using Testcontainers.PostgreSql;

namespace NbTcgTrader.Tests;

// End-to-end coverage of the email-backed auth flows (#69) against a real Postgres
// via Testcontainers: verification confirms an address, forgot-password never reveals
// whether an account exists, and a completed reset changes the password AND kills the
// old sessions. The provider is replaced by a capturing dispatcher, so nothing is sent
// and the test can read the token straight out of the link the user would click.
// Skips cleanly when Docker is unavailable.
[Collection(IntegrationTestCollection.Name)]
[Trait("Category", "Integration")]
public sealed class EmailAuthEndpointsTests : IAsyncLifetime
{
    private const string Password = "Sup3rSecret!Pwd";
    private const string NewPassword = "Ev3nBetter!Pwd";
    private const string FrontendBaseUrl = "https://nbtcg.example.com";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly List<EmailAuthApiFactory> _factories = [];
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
    public async Task Registering_queues_a_verification_email_whose_link_confirms_the_address()
    {
        var (client, emails) = Create();
        var email = UniqueEmail();

        var auth = await RegisterAsync(client, email);

        var verification = emails.Single(EmailTemplates.VerificationKind);
        verification.To.ShouldBe(email);

        var link = ExtractLink(verification.TextBody);
        link.ShouldStartWith($"{FrontendBaseUrl}/verify-email?");

        var query = QueryHelpers.ParseQuery(new Uri(link).Query);
        var confirm = await client.PostAsJsonAsync(
            "/auth/email/verify",
            new { userId = query["userId"].ToString(), token = query["token"].ToString() });

        confirm.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // The confirmation is visible to the client, which is how the UI drops its
        // "unconfirmed" nudge.
        var me = await GetMeAsync(client, auth.AccessToken);
        me.GetProperty("emailConfirmed").GetBoolean().ShouldBeTrue();
    }

    [SkippableFact]
    public async Task A_tampered_confirmation_token_is_rejected_without_confirming()
    {
        var (client, emails) = Create();
        var email = UniqueEmail();
        var auth = await RegisterAsync(client, email);

        var query = QueryHelpers.ParseQuery(
            new Uri(ExtractLink(emails.Single(EmailTemplates.VerificationKind).TextBody)).Query);

        var confirm = await client.PostAsJsonAsync(
            "/auth/email/verify",
            new { userId = query["userId"].ToString(), token = "bm90LWEtcmVhbC10b2tlbg" });

        confirm.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var me = await GetMeAsync(client, auth.AccessToken);
        me.GetProperty("emailConfirmed").GetBoolean().ShouldBeFalse();
    }

    [SkippableFact]
    public async Task Forgot_password_answers_the_same_for_a_known_and_an_unknown_address()
    {
        var (client, emails) = Create();
        var known = UniqueEmail();
        await RegisterAsync(client, known);

        var forKnown = await client.PostAsJsonAsync("/auth/password/forgot", new { email = known });
        var forUnknown = await client.PostAsJsonAsync(
            "/auth/password/forgot", new { email = UniqueEmail() });

        // Identical status codes and empty bodies: the endpoint is not an oracle for
        // which addresses are registered (CLAUDE.md §15).
        forKnown.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        forUnknown.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await forKnown.Content.ReadAsStringAsync())
            .ShouldBe(await forUnknown.Content.ReadAsStringAsync());

        // The observable difference is only server-side: one email, not two.
        emails.OfKind(EmailTemplates.PasswordResetKind).Count.ShouldBe(1);
    }

    [SkippableFact]
    public async Task Resetting_the_password_changes_the_credential_and_revokes_old_sessions()
    {
        var (client, emails) = Create();
        var email = UniqueEmail();
        var original = await RegisterAsync(client, email);

        (await client.PostAsJsonAsync("/auth/password/forgot", new { email }))
            .StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var query = QueryHelpers.ParseQuery(
            new Uri(ExtractLink(emails.Single(EmailTemplates.PasswordResetKind).TextBody)).Query);

        var reset = await client.PostAsJsonAsync("/auth/password/reset", new
        {
            email = query["email"].ToString(),
            token = query["token"].ToString(),
            newPassword = NewPassword,
        });

        reset.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // The old password is dead and the new one works.
        (await LoginAsync(client, email, Password)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await LoginAsync(client, email, NewPassword)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // The refresh token minted before the reset must not outlive it — otherwise
        // resetting does nothing about the session an attacker already holds.
        var refresh = await client.PostAsJsonAsync(
            "/auth/refresh", new { refreshToken = original.RefreshToken });
        refresh.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // And the account owner is told out of band that it happened.
        emails.OfKind(EmailTemplates.PasswordChangedKind).Count.ShouldBe(1);
    }

    [SkippableFact]
    public async Task A_reset_token_cannot_be_replayed()
    {
        var (client, emails) = Create();
        var email = UniqueEmail();
        await RegisterAsync(client, email);
        await client.PostAsJsonAsync("/auth/password/forgot", new { email });

        var query = QueryHelpers.ParseQuery(
            new Uri(ExtractLink(emails.Single(EmailTemplates.PasswordResetKind).TextBody)).Query);
        object Body(string password) => new
        {
            email = query["email"].ToString(),
            token = query["token"].ToString(),
            newPassword = password,
        };

        (await client.PostAsJsonAsync("/auth/password/reset", Body(NewPassword)))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Identity's token is bound to the security stamp, which the first reset
        // rotated: a replayed link is now worthless.
        (await client.PostAsJsonAsync("/auth/password/reset", Body("Att4ckerChoice!x")))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [SkippableFact]
    public async Task Resetting_to_a_weak_password_reports_field_errors_and_keeps_the_old_one()
    {
        var (client, emails) = Create();
        var email = UniqueEmail();
        await RegisterAsync(client, email);
        await client.PostAsJsonAsync("/auth/password/forgot", new { email });

        var query = QueryHelpers.ParseQuery(
            new Uri(ExtractLink(emails.Single(EmailTemplates.PasswordResetKind).TextBody)).Query);

        var reset = await client.PostAsJsonAsync("/auth/password/reset", new
        {
            email = query["email"].ToString(),
            token = query["token"].ToString(),
            // Long enough for the FluentValidation rule, but no digit/upper/symbol —
            // so it is Identity's policy that rejects it, not the validator.
            newPassword = "allloweralpha",
        });

        reset.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await reset.Content.ReadFromJsonAsync<JsonElement>(Json);
        problem.GetProperty("errors").GetProperty("NewPassword").GetArrayLength()
            .ShouldBeGreaterThan(0);

        // Nothing changed: the original credential still signs in.
        (await LoginAsync(client, email, Password)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [SkippableFact]
    public async Task Repeated_forgot_password_requests_are_rate_limited()
    {
        var (client, _) = Create();
        var email = UniqueEmail();
        await RegisterAsync(client, email);

        // The email policy permits 3 per window; the fourth must be refused so the
        // endpoint cannot be used to mail-bomb an address or burn the free quota.
        for (var i = 0; i < 3; i++)
        {
            (await client.PostAsJsonAsync("/auth/password/forgot", new { email }))
                .StatusCode.ShouldBe(HttpStatusCode.Accepted);
        }

        (await client.PostAsJsonAsync("/auth/password/forgot", new { email }))
            .StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    // ---- helpers -----------------------------------------------------------

    private (HttpClient Client, CapturingEmailDispatcher Emails) Create()
    {
        Skip.If(_dockerUnavailableReason is not null, _dockerUnavailableReason);

        var factory = new EmailAuthApiFactory(_postgres!.GetConnectionString());
        _factories.Add(factory);
        return (factory.CreateClient(), factory.Emails);
    }

    private static string UniqueEmail() => $"user-{Guid.NewGuid():N}@example.com";

    private static async Task<AuthTokens> RegisterAsync(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync("/auth/register", new
        {
            email,
            password = Password,
            displayName = "Ash",
            city = "Moncton",
            country = "Canada",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var auth = await response.Content.ReadFromJsonAsync<AuthTokens>(Json);
        auth.ShouldNotBeNull();
        return auth;
    }

    private static Task<HttpResponseMessage> LoginAsync(
        HttpClient client, string email, string password) =>
        client.PostAsJsonAsync("/auth/login", new { email, password });

    private static async Task<JsonElement> GetMeAsync(HttpClient client, string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/auth/me");
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        var response = await client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Json);
    }

    /// <summary>
    /// Pulls the frontend link out of the plain-text part — the same string a user
    /// would copy out of their mail client.
    /// </summary>
    private static string ExtractLink(string textBody)
    {
        var link = textBody
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(line => line.StartsWith(FrontendBaseUrl, StringComparison.Ordinal));

        link.ShouldNotBeNull("the email should contain an absolute frontend link");
        return link!;
    }

    private sealed record AuthTokens(string AccessToken, string RefreshToken);

    /// <summary>
    /// Stands in for the outbox: records what would have been sent so a test can read
    /// the token out of the link. Nothing reaches a provider or the file system.
    /// </summary>
    private sealed class CapturingEmailDispatcher : IEmailDispatcher
    {
        private readonly List<EmailMessage> _messages = [];

        public bool Enqueue(EmailMessage message)
        {
            lock (_messages)
            {
                _messages.Add(message);
            }

            return true;
        }

        public IReadOnlyList<EmailMessage> OfKind(string kind)
        {
            lock (_messages)
            {
                return _messages.Where(m => m.Kind == kind).ToArray();
            }
        }

        public EmailMessage Single(string kind) => OfKind(kind).ShouldHaveSingleItem();
    }

    private sealed class EmailAuthApiFactory(string connectionString) : WebApplicationFactory<Program>
    {
        public CapturingEmailDispatcher Emails { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Database:ApplyMigrationsOnStartup", "true");
            builder.UseSetting("ConnectionStrings:Default", connectionString);
            builder.UseSetting("Jwt:SigningKey", TestJwt.SigningKey);
            builder.UseSetting("Jwt:Issuer", TestJwt.Issuer);
            builder.UseSetting("Jwt:Audience", TestJwt.Audience);

            // Pin the frontend origin so the assertions can match on it exactly.
            builder.UseSetting("Email:FrontendBaseUrl", FrontendBaseUrl);

            builder.ConfigureTestServices(services =>
            {
                // Replace the real outbox so the background sender never runs and the
                // test can inspect exactly what the app decided to send.
                services.RemoveAll<IEmailDispatcher>();
                services.AddSingleton<IEmailDispatcher>(Emails);
            });
        }
    }
}
