using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NbTcgTrader.Api.Common.Domain;
using NbTcgTrader.Api.Common.Extensions;
using NbTcgTrader.Api.Common.Persistence;

namespace NbTcgTrader.Api.Features.Admin;

/// <summary>Lock request. A reason is mandatory — locks must be accountable.</summary>
public sealed record LockUserRequest(string Reason);

public sealed class LockUserRequestValidator : AbstractValidator<LockUserRequest>
{
    public LockUserRequestValidator()
    {
        // NotEmpty alone lets "   " through; a lock reason must have substance.
        RuleFor(x => x.Reason)
            .Must(r => !string.IsNullOrWhiteSpace(r))
            .WithMessage("'Reason' must not be empty.")
            .MaximumLength(500);
    }
}

/// <summary>
/// Locks an account indefinitely: sign-in is refused (Login honours lockout) and
/// token refresh stops (Refresh checks lockout, and every active refresh token is
/// revoked here). An access token already in the wild expires within its normal
/// ~15-minute lifetime. Guards: an admin cannot lock themselves or another admin.
/// The action and its reason land in the audit log atomically with the lock.
/// </summary>
public sealed class LockUserHandler(
    AppDbContext db,
    UserManager<AppUser> users,
    AdminAuditWriter audit,
    ILogger<LockUserHandler> logger)
{
    public async Task<IResult> HandleAsync(
        string userId,
        string adminUserId,
        LockUserRequest request,
        CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(userId);
        if (user is null)
        {
            return Results.Problem(
                detail: "No user exists with the supplied id.",
                statusCode: StatusCodes.Status404NotFound,
                title: "User not found");
        }

        if (userId == adminUserId)
        {
            return Results.Problem(
                detail: "Admins cannot lock their own account.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid lock target");
        }

        if (await users.IsInRoleAsync(user, AuthenticationExtensions.AdminRole))
        {
            return Results.Problem(
                detail: "Admin accounts cannot be locked. Remove the role first.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid lock target");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;

        // Indefinite lockout via Identity's own mechanism, so Login/Refresh's
        // IsLockedOutAsync checks pick it up with no extra state.
        user.LockoutEnabled = true;
        user.LockoutEnd = DateTimeOffset.MaxValue;
        var result = await users.UpdateAsync(user);
        if (!result.Succeeded)
        {
            var errors = result.Errors
                .GroupBy(e => e.Code)
                .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray());

            return Results.ValidationProblem(errors);
        }

        // Kill every live refresh token so the lock takes effect at the next
        // refresh, not at the token's natural expiry.
        var revoked = await db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(t => t.RevokedAt, now),
                cancellationToken);

        audit.Stage(adminUserId, userId, AdminAction.UserLocked, request.Reason);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation(
            "Admin {AdminUserId} locked user {TargetUserId}; {RevokedTokens} refresh token(s) revoked",
            adminUserId, userId, revoked);

        return Results.NoContent();
    }
}
