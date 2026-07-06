using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NbTcgTrader.Api.Common.Domain;
using NbTcgTrader.Api.Common.Extensions;
using NbTcgTrader.Api.Common.Persistence;
using Shouldly;
using Testcontainers.PostgreSql;

namespace NbTcgTrader.Tests;

// End-to-end coverage of the /admin hub (admin/operations backlog) against a real
// Postgres via Testcontainers. Covers the ACs: admin endpoints are protected by the
// Admin role policy (401 anonymous, 403 non-admin), admins can list users and view a
// detail safely (contact handles reduced to flags), lock requires a reason and blocks
// sign-in + token refresh, unlock restores access, and every sensitive action lands in
// the audit log. Skips cleanly when Docker is unavailable. Each test gets its own host
// (own rate-limit bucket) and users.
[Collection(IntegrationTestCollection.Name)]
[Trait("Category", "Integration")]
public sealed class AdminEndpointsTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const string Password = "Sup3rSecret!Pwd";

    private readonly List<AdminApiFactory> _factories = [];
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
    public async Task Admin_endpoints_return_401_without_a_token()
    {
        var factory = CreateFactory();
        var client = factory.CreateClient();

        (await client.GetAsync("/admin/users")).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
        (await client.GetAsync("/admin/dashboard")).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
        (await client.PostAsJsonAsync("/admin/users/some-id/lock", new { reason = "x" }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [SkippableFact]
    public async Task Admin_endpoints_return_403_for_non_admin_users()
    {
        var factory = CreateFactory();
        var client = factory.CreateClient();
        var user = await RegisterAsync(client);

        (await SendGetAsync(client, user.Token, "/admin/users")).StatusCode
            .ShouldBe(HttpStatusCode.Forbidden);
        (await SendGetAsync(client, user.Token, "/admin/audit-log")).StatusCode
            .ShouldBe(HttpStatusCode.Forbidden);
        (await SendPostAsync(client, user.Token, $"/admin/users/{user.Id}/lock",
                new { reason = "no" }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [SkippableFact]
    public async Task Admin_can_list_and_search_users_without_exposing_contact_handles()
    {
        var factory = CreateFactory();
        var client = factory.CreateClient();
        var admin = await CreateAdminAsync(factory, client);
        var target = await RegisterAsync(client, displayName: "Misty",
            discordHandle: "misty#0042");

        var response = await SendGetAsync(client, admin.Token, "/admin/users?search=Misty");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        // Present/absent flags only — the handle value itself never leaves the API.
        body.ShouldNotContain("misty#0042");

        var page = JsonSerializer.Deserialize<UserListDto>(body, Json);
        page.ShouldNotBeNull();
        var row = page.Items.ShouldHaveSingleItem();
        row.Id.ShouldBe(target.Id);
        row.DisplayName.ShouldBe("Misty");
        row.HasDiscordHandle.ShouldBeTrue();
        row.HasContactEmail.ShouldBeFalse();
        row.IsLockedOut.ShouldBeFalse();
    }

    [SkippableFact]
    public async Task Lock_requires_a_reason()
    {
        var factory = CreateFactory();
        var client = factory.CreateClient();
        var admin = await CreateAdminAsync(factory, client);
        var target = await RegisterAsync(client);

        var response = await SendPostAsync(client, admin.Token,
            $"/admin/users/{target.Id}/lock", new { reason = "" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    [SkippableFact]
    public async Task Lock_blocks_sign_in_and_refresh_and_unlock_restores_access()
    {
        var factory = CreateFactory();
        var client = factory.CreateClient();
        var admin = await CreateAdminAsync(factory, client);
        var target = await RegisterAsync(client);

        (await SendPostAsync(client, admin.Token, $"/admin/users/{target.Id}/lock",
                new { reason = "Suspicious listings reported" }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Locked: password sign-in is refused with the opaque credentials error.
        (await client.PostAsJsonAsync("/auth/login",
                new { email = target.Email, password = Password }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // Locked: the pre-lock refresh token no longer mints new tokens.
        (await client.PostAsJsonAsync("/auth/refresh",
                new { refreshToken = target.RefreshToken }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // The lock and its mandatory reason are in the audit trail.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var entry = await db.AdminAuditLogs
                .SingleAsync(a => a.TargetUserId == target.Id
                                  && a.Action == AdminAction.UserLocked);
            entry.AdminUserId.ShouldBe(admin.Id);
            entry.Reason.ShouldBe("Suspicious listings reported");
            entry.CorrelationId.ShouldNotBeNullOrWhiteSpace();
        }

        (await SendPostAsync(client, admin.Token, $"/admin/users/{target.Id}/unlock",
                new { reason = "Resolved with the user" }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Unlocked: sign-in works again, and the unlock is audited too.
        (await client.PostAsJsonAsync("/auth/login",
                new { email = target.Email, password = Password }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.AdminAuditLogs.AnyAsync(a => a.TargetUserId == target.Id
                                                   && a.Action == AdminAction.UserUnlocked))
                .ShouldBeTrue();
        }
    }

    [SkippableFact]
    public async Task Admins_cannot_lock_themselves_or_other_admins()
    {
        var factory = CreateFactory();
        var client = factory.CreateClient();
        var admin = await CreateAdminAsync(factory, client);
        var otherAdmin = await RegisterAsync(client);
        await PromoteToAdminAsync(factory, otherAdmin.Id);

        (await SendPostAsync(client, admin.Token, $"/admin/users/{admin.Id}/lock",
                new { reason = "self" }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await SendPostAsync(client, admin.Token, $"/admin/users/{otherAdmin.Id}/lock",
                new { reason = "other admin" }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [SkippableFact]
    public async Task Locking_an_unknown_user_returns_404()
    {
        var factory = CreateFactory();
        var client = factory.CreateClient();
        var admin = await CreateAdminAsync(factory, client);

        var response = await SendPostAsync(client, admin.Token,
            "/admin/users/no-such-user/lock", new { reason = "ghost" });

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    [SkippableFact]
    public async Task User_detail_is_safe_and_viewing_it_is_audited()
    {
        var factory = CreateFactory();
        var client = factory.CreateClient();
        var admin = await CreateAdminAsync(factory, client);
        var target = await RegisterAsync(client, discordHandle: "brock#7777");

        var response = await SendGetAsync(client, admin.Token, $"/admin/users/{target.Id}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldNotContain("brock#7777");

        var detail = JsonSerializer.Deserialize<UserDetailDto>(body, Json);
        detail.ShouldNotBeNull();
        detail.Id.ShouldBe(target.Id);
        detail.HasDiscordHandle.ShouldBeTrue();
        detail.IsLockedOut.ShouldBeFalse();
        detail.CollectionItemCount.ShouldBe(0);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.AdminAuditLogs.AnyAsync(a => a.TargetUserId == target.Id
                                               && a.AdminUserId == admin.Id
                                               && a.Action == AdminAction.UserDetailViewed))
            .ShouldBeTrue();
    }

    [SkippableFact]
    public async Task Dashboard_activity_and_audit_log_return_operational_data()
    {
        var factory = CreateFactory();
        var client = factory.CreateClient();
        var admin = await CreateAdminAsync(factory, client);
        var target = await RegisterAsync(client, displayName: "Gary");

        var dashboard = await SendGetAsync(client, admin.Token, "/admin/dashboard");
        dashboard.StatusCode.ShouldBe(HttpStatusCode.OK);
        var stats = await dashboard.Content.ReadFromJsonAsync<DashboardDto>(Json);
        stats.ShouldNotBeNull();
        stats.TotalUsers.ShouldBeGreaterThanOrEqualTo(2);
        stats.NewUsersLast7Days.ShouldBeGreaterThanOrEqualTo(2);
        stats.LockedUsers.ShouldBe(0);

        var activity = await SendGetAsync(client, admin.Token, "/admin/activity");
        activity.StatusCode.ShouldBe(HttpStatusCode.OK);
        var snapshot = await activity.Content.ReadFromJsonAsync<ActivityDto>(Json);
        snapshot.ShouldNotBeNull();
        snapshot.RecentRegistrations.ShouldContain(u => u.Id == target.Id);
        // The admin signed in via /auth/login, so they appear as a recent sign-in.
        snapshot.RecentSignIns.ShouldContain(u => u.Id == admin.Id);

        // Lock once so the audit page has a guaranteed row.
        (await SendPostAsync(client, admin.Token, $"/admin/users/{target.Id}/lock",
                new { reason = "audit page check" }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var audit = await SendGetAsync(client, admin.Token, "/admin/audit-log");
        audit.StatusCode.ShouldBe(HttpStatusCode.OK);
        var auditPage = await audit.Content.ReadFromJsonAsync<AuditLogDto>(Json);
        auditPage.ShouldNotBeNull();
        auditPage.TotalCount.ShouldBeGreaterThanOrEqualTo(1);
        var lockEntry = auditPage.Items.ShouldHaveSingleItem();
        lockEntry.Action.ShouldBe("UserLocked");
        lockEntry.AdminUserId.ShouldBe(admin.Id);
        lockEntry.TargetUserId.ShouldBe(target.Id);
        lockEntry.Reason.ShouldBe("audit page check");
    }

    private AdminApiFactory CreateFactory()
    {
        Skip.If(_dockerUnavailableReason is not null, _dockerUnavailableReason);

        var factory = new AdminApiFactory(_postgres!.GetConnectionString());
        _factories.Add(factory);
        return factory;
    }

    /// <summary>
    /// Registers a user, promotes them to Admin server-side, and signs in again so
    /// the returned access token carries the role claim (the pre-promotion token
    /// would not).
    /// </summary>
    private async Task<TestUser> CreateAdminAsync(AdminApiFactory factory, HttpClient client)
    {
        var admin = await RegisterAsync(client, displayName: "Admin");
        await PromoteToAdminAsync(factory, admin.Id);

        var login = await client.PostAsJsonAsync("/auth/login",
            new { email = admin.Email, password = Password });
        login.StatusCode.ShouldBe(HttpStatusCode.OK);
        var auth = await login.Content.ReadFromJsonAsync<JsonElement>(Json);

        return admin with { Token = auth.GetProperty("accessToken").GetString()! };
    }

    private static async Task PromoteToAdminAsync(AdminApiFactory factory, string userId)
    {
        using var scope = factory.Services.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

        if (!await roles.RoleExistsAsync(AuthenticationExtensions.AdminRole))
        {
            (await roles.CreateAsync(new IdentityRole(AuthenticationExtensions.AdminRole)))
                .Succeeded.ShouldBeTrue();
        }

        var user = await users.FindByIdAsync(userId);
        user.ShouldNotBeNull();
        (await users.AddToRoleAsync(user, AuthenticationExtensions.AdminRole))
            .Succeeded.ShouldBeTrue();
    }

    private static async Task<TestUser> RegisterAsync(
        HttpClient client,
        string displayName = "Ash",
        string? discordHandle = null)
    {
        var email = $"user-{Guid.NewGuid():N}@example.com";
        var response = await client.PostAsJsonAsync("/auth/register", new
        {
            email,
            password = Password,
            displayName,
            city = "Moncton",
            country = "Canada",
            discordHandle,
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var auth = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        return new TestUser(
            auth.GetProperty("user").GetProperty("id").GetString()!,
            email,
            auth.GetProperty("accessToken").GetString()!,
            auth.GetProperty("refreshToken").GetString()!);
    }

    private static Task<HttpResponseMessage> SendGetAsync(
        HttpClient client, string token, string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> SendPostAsync(
        HttpClient client, string token, string url, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client.SendAsync(request);
    }

    private sealed record TestUser(string Id, string Email, string Token, string RefreshToken);

    // Local DTOs assert the over-the-wire JSON contract independent of the API's
    // internal record shapes.
    private sealed record UserListDto(
        IReadOnlyList<UserRowDto> Items, int Page, int PageSize, int TotalCount);

    private sealed record UserRowDto(
        string Id, string DisplayName, string? Email, string? City, string? Country,
        bool HasContactEmail, bool HasDiscordHandle, bool HasInstagramHandle,
        bool IsLockedOut, DateTimeOffset CreatedAt);

    private sealed record UserDetailDto(
        string Id, string DisplayName, string? Email, bool HasDiscordHandle,
        bool IsLockedOut, int AccessFailedCount, int CollectionItemCount,
        int ActiveListingCount);

    private sealed record DashboardDto(
        int TotalUsers, int NewUsersLast7Days, int NewUsersLast30Days, int LockedUsers,
        int ActiveListings, int FailedImportsLast7Days);

    private sealed record ActivityDto(
        IReadOnlyList<ActivityUserDto> RecentSignIns,
        IReadOnlyList<ActivityUserDto> RecentRegistrations);

    private sealed record ActivityUserDto(string Id, string DisplayName);

    private sealed record AuditLogDto(
        IReadOnlyList<AuditEntryDto> Items, int Page, int PageSize, int TotalCount);

    private sealed record AuditEntryDto(
        long Id, string AdminUserId, string? AdminDisplayName, string? TargetUserId,
        string? TargetDisplayName, string Action, string? Reason, string? CorrelationId);

    // Boots the real app against the Testcontainers Postgres (migrations on) with a
    // throwaway Jwt/placeholder config. No admin seed email is configured — tests
    // promote via UserManager so the pre/post-promotion token difference is real.
    private sealed class AdminApiFactory(string connectionString)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Database:ApplyMigrationsOnStartup", "true");
            builder.UseSetting("ConnectionStrings:Default", connectionString);
            builder.UseSetting("Jwt:SigningKey", TestJwt.SigningKey);
            builder.UseSetting("Jwt:Issuer", TestJwt.Issuer);
            builder.UseSetting("Jwt:Audience", TestJwt.Audience);
            builder.UseSetting(
                "Catalog:PlaceholderImageUrl",
                "https://api.example.com/assets/card-placeholder.svg");
        }
    }
}
