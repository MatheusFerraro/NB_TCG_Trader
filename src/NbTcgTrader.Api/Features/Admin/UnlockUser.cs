using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NbTcgTrader.Api.Common.Domain;
using NbTcgTrader.Api.Common.Persistence;

namespace NbTcgTrader.Api.Features.Admin;

/// <summary>Unlock request. The reason is optional but encouraged for the audit trail.</summary>
public sealed record UnlockUserRequest(string? Reason);

public sealed class UnlockUserRequestValidator : AbstractValidator<UnlockUserRequest>
{
    public UnlockUserRequestValidator()
    {
        RuleFor(x => x.Reason).MaximumLength(500);
    }
}

/// <summary>
/// Clears a lockout (admin lock or tripped credential lockout) and resets the
/// failed-attempt counter so the user isn't immediately re-locked. Audited.
/// </summary>
public sealed class UnlockUserHandler(
    AppDbContext db,
    UserManager<AppUser> users,
    AdminAuditWriter audit,
    ILogger<UnlockUserHandler> logger)
{
    public async Task<IResult> HandleAsync(
        string userId,
        string adminUserId,
        UnlockUserRequest request,
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

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        user.LockoutEnd = null;
        user.AccessFailedCount = 0;
        var result = await users.UpdateAsync(user);
        if (!result.Succeeded)
        {
            var errors = result.Errors
                .GroupBy(e => e.Code)
                .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray());

            return Results.ValidationProblem(errors);
        }

        audit.Stage(adminUserId, userId, AdminAction.UserUnlocked, request.Reason);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation(
            "Admin {AdminUserId} unlocked user {TargetUserId}", adminUserId, userId);

        return Results.NoContent();
    }
}
