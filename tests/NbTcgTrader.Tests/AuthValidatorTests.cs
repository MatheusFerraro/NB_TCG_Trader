using NbTcgTrader.Api.Features.Auth;
using Shouldly;

namespace NbTcgTrader.Tests;

// Fast, DB-free validator checks (CLAUDE.md §11 prioritizes validators). The
// password rule mirrors the Identity policy so clients get a 400 before the store.
public class AuthValidatorTests
{
    private static RegisterRequest ValidRegister() => new(
        Email: "trainer@example.com",
        Password: "Sup3rSecret!Pwd",
        DisplayName: "Ash",
        City: "Moncton",
        Country: "Canada",
        ContactEmail: "contact@example.com",
        DiscordHandle: "ash#1234",
        InstagramHandle: "ash.k");

    [Fact]
    public void Register_valid_request_passes()
    {
        var result = new RegisterValidator().Validate(ValidRegister());

        result.IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    public void Register_rejects_bad_email(string email)
    {
        var request = ValidRegister() with { Email = email };

        var result = new RegisterValidator().Validate(request);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(RegisterRequest.Email));
    }

    [Theory]
    [InlineData("")]
    [InlineData("short1!A")] // under the 12-char minimum
    public void Register_rejects_weak_password(string password)
    {
        var request = ValidRegister() with { Password = password };

        var result = new RegisterValidator().Validate(request);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(RegisterRequest.Password));
    }

    [Fact]
    public void Register_rejects_empty_display_name()
    {
        var request = ValidRegister() with { DisplayName = "" };

        var result = new RegisterValidator().Validate(request);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(RegisterRequest.DisplayName));
    }

    [Fact]
    public void Register_rejects_malformed_contact_email_but_allows_empty()
    {
        new RegisterValidator()
            .Validate(ValidRegister() with { ContactEmail = "nope" })
            .IsValid.ShouldBeFalse();

        new RegisterValidator()
            .Validate(ValidRegister() with { ContactEmail = null })
            .IsValid.ShouldBeTrue();
    }

    private static UpdateProfileRequest ValidUpdate() => new(
        DisplayName: "Misty",
        City: "Cerulean",
        Country: "Canada",
        ContactEmail: "contact@example.com",
        DiscordHandle: "misty#0001",
        InstagramHandle: "misty.w");

    [Fact]
    public void UpdateProfile_valid_request_passes()
    {
        new UpdateProfileValidator().Validate(ValidUpdate()).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void UpdateProfile_rejects_empty_display_name()
    {
        var result = new UpdateProfileValidator().Validate(ValidUpdate() with { DisplayName = "" });

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(UpdateProfileRequest.DisplayName));
    }

    [Fact]
    public void UpdateProfile_rejects_malformed_contact_email_but_allows_empty()
    {
        new UpdateProfileValidator()
            .Validate(ValidUpdate() with { ContactEmail = "nope" })
            .IsValid.ShouldBeFalse();

        new UpdateProfileValidator()
            .Validate(ValidUpdate() with { ContactEmail = null })
            .IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Login_requires_email_and_password()
    {
        var result = new LoginValidator().Validate(new LoginRequest("", ""));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(LoginRequest.Email));
        result.Errors.ShouldContain(e => e.PropertyName == nameof(LoginRequest.Password));
    }

    [Fact]
    public void Refresh_requires_a_token()
    {
        new RefreshValidator().Validate(new RefreshRequest("")).IsValid.ShouldBeFalse();
        new RefreshValidator().Validate(new RefreshRequest("abc")).IsValid.ShouldBeTrue();
    }
}
