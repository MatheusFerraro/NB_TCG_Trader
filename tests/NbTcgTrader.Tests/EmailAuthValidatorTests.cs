using NbTcgTrader.Api.Features.Auth;
using Shouldly;

namespace NbTcgTrader.Tests;

// Fast, DB-free validator checks for the email-backed auth slices (#69). These are
// the first gate on every one of the new endpoints, and three of the four are
// anonymous, so they carry more weight than usual (CLAUDE.md §11).
public class EmailAuthValidatorTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-email")]
    public void Forgot_password_rejects_a_non_address(string email) =>
        new ForgotPasswordValidator().Validate(new ForgotPasswordRequest(email))
            .IsValid.ShouldBeFalse();

    [Fact]
    public void Forgot_password_accepts_a_plausible_address() =>
        new ForgotPasswordValidator().Validate(new ForgotPasswordRequest("alice@cards.test"))
            .IsValid.ShouldBeTrue();

    [Fact]
    public void Forgot_password_rejects_an_over_long_address() =>
        // 256 is the Identity column length; anything longer cannot match an account
        // and should not reach the store.
        new ForgotPasswordValidator()
            .Validate(new ForgotPasswordRequest($"{new string('a', 250)}@cards.test"))
            .IsValid.ShouldBeFalse();

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    public void Resend_verification_rejects_a_non_address(string email) =>
        new ResendVerificationValidator().Validate(new ResendVerificationRequest(email))
            .IsValid.ShouldBeFalse();

    [Fact]
    public void Verify_email_requires_both_link_values()
    {
        var validator = new VerifyEmailValidator();

        validator.Validate(new VerifyEmailRequest("", "token")).IsValid.ShouldBeFalse();
        validator.Validate(new VerifyEmailRequest("user-1", "")).IsValid.ShouldBeFalse();
        validator.Validate(new VerifyEmailRequest("user-1", "token")).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Reset_password_accepts_a_policy_compliant_password() =>
        new ResetPasswordValidator()
            .Validate(new ResetPasswordRequest("alice@cards.test", "token", "Sup3rSecret!Pwd"))
            .IsValid.ShouldBeTrue();

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("elevenchar")]
    public void Reset_password_rejects_a_password_under_the_identity_minimum(string password) =>
        // Mirrors PersistenceExtensions' RequiredLength = 12 so the client gets field
        // errors without a round-trip to the store.
        new ResetPasswordValidator()
            .Validate(new ResetPasswordRequest("alice@cards.test", "token", password))
            .IsValid.ShouldBeFalse();

    [Fact]
    public void Reset_password_requires_a_token() =>
        new ResetPasswordValidator()
            .Validate(new ResetPasswordRequest("alice@cards.test", "", "Sup3rSecret!Pwd"))
            .IsValid.ShouldBeFalse();
}
