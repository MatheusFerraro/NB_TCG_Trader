using System.Text;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NbTcgTrader.Api.Features.Auth;

namespace NbTcgTrader.Api.Common.Extensions;

/// <summary>
/// JWT bearer authentication, authorization, and Auth-slice service wiring
/// (CLAUDE.md §15, BACKLOG #6). The signing key and issuer/audience come from the
/// <c>Jwt</c> config section and are validated on startup so a misconfigured
/// deployment fails fast rather than minting unverifiable tokens.
/// </summary>
public static class AuthenticationExtensions
{
    /// <summary>Identity role name for trusted operators (admin hub).</summary>
    public const string AdminRole = "Admin";

    /// <summary>Authorization policy requiring the <see cref="AdminRole"/> role.</summary>
    public const string AdminPolicy = "AdminOnly";

    public static IServiceCollection AddApiAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(
                o => !string.IsNullOrWhiteSpace(o.SigningKey)
                     && Encoding.UTF8.GetByteCount(o.SigningKey) >= 32,
                "Jwt:SigningKey must be configured and at least 256 bits (32 bytes).")
            .Validate(
                o => !string.IsNullOrWhiteSpace(o.Issuer),
                "Jwt:Issuer must be configured.")
            .Validate(
                o => !string.IsNullOrWhiteSpace(o.Audience),
                "Jwt:Audience must be configured.")
            .Validate(
                o => o.AccessTokenMinutes > 0 && o.RefreshTokenDays > 0,
                "Jwt token lifetimes must be positive.")
            .ValidateOnStart();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        // Build the validation parameters from the validated JwtOptions. Kept in a
        // named-options Configure so it picks up the same bound/validated instance.
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwtOptions) =>
            {
                var jwt = jwtOptions.Value;
                bearer.MapInboundClaims = false; // keep `sub` as-is; no legacy remap
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(jwt.SigningKey)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    // We disabled inbound claim mapping, so name the claim types
                    // explicitly: `sub` is the user id, `name` the display name.
                    NameClaimType = "name",
                    RoleClaimType = "role",
                };
            });

        // Admin-only surface: the role claim ("role") is minted into the JWT by
        // TokenService, so the policy evaluates without a database round-trip.
        services.AddAuthorization(options =>
            options.AddPolicy(AdminPolicy, policy => policy.RequireRole(AdminRole)));

        // Auth-slice services: token minting, rotation, and the endpoint handlers.
        services.AddSingleton<ITokenService, TokenService>();
        services.AddScoped<TokenIssuer>();
        services.AddScoped<RegisterHandler>();
        services.AddScoped<LoginHandler>();
        services.AddScoped<RefreshHandler>();
        services.AddScoped<LogoutHandler>();
        services.AddScoped<MeHandler>();
        services.AddScoped<UpdateProfileHandler>();

        // FluentValidation validators for the endpoint filter (CLAUDE.md §10).
        services.AddValidatorsFromAssemblyContaining<RegisterValidator>();

        return services;
    }
}
