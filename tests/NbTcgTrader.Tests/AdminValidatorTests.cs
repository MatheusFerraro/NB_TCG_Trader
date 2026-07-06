using NbTcgTrader.Api.Features.Admin;
using Shouldly;

namespace NbTcgTrader.Tests;

// Validator rules for the admin hub requests: a lock must carry a reason (the
// audit trail depends on it), unlock reasons are optional but bounded, and the
// user-table query has the same pagination guardrails as the other list endpoints.
public sealed class AdminValidatorTests
{
    private readonly LockUserRequestValidator _lock = new();
    private readonly UnlockUserRequestValidator _unlock = new();
    private readonly ListUsersRequestValidator _list = new();

    [Fact]
    public void Lock_with_a_reason_passes()
    {
        _lock.Validate(new LockUserRequest("Repeated scam reports")).IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Lock_without_a_reason_fails(string reason)
    {
        var result = _lock.Validate(new LockUserRequest(reason));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(LockUserRequest.Reason));
    }

    [Fact]
    public void Lock_reason_over_500_chars_fails()
    {
        _lock.Validate(new LockUserRequest(new string('x', 501))).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Unlock_reason_is_optional_but_bounded()
    {
        _unlock.Validate(new UnlockUserRequest(null)).IsValid.ShouldBeTrue();
        _unlock.Validate(new UnlockUserRequest("resolved")).IsValid.ShouldBeTrue();
        _unlock.Validate(new UnlockUserRequest(new string('x', 501))).IsValid.ShouldBeFalse();
    }

    [Theory]
    [InlineData(0, 25)]
    [InlineData(10_001, 25)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public void User_list_rejects_out_of_range_paging(int page, int pageSize)
    {
        _list.Validate(new ListUsersRequest(Page: page, PageSize: pageSize))
            .IsValid.ShouldBeFalse();
    }

    [Fact]
    public void User_list_defaults_pass()
    {
        _list.Validate(new ListUsersRequest()).IsValid.ShouldBeTrue();
    }
}
