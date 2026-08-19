using Patchbay.Core.Sessions;

namespace Patchbay.Tests;

/// <summary>
/// Reading the number that comes with a refused sign-in (M4-10).
///
/// <para>
/// The question under test is not what went wrong but whether asking again is
/// any use, and getting it wrong has a cost in both directions. Offering a
/// re-prompt for a locked account is how somebody types their correct password
/// three more times into a door that will not open. Refusing one for a
/// mistyped password is how they close the tab and start again.
/// </para>
/// </summary>
public class LogonFailureTests
{
    // ── Winlogon narrating itself ───────────────────────────────────────

    // The six arbitration codes, and the logon warning at 3 — which is why
    // this cannot be written as "negative means notice".
    [Theory]
    [InlineData(-7)]
    [InlineData(-6)]
    [InlineData(-5)]
    [InlineData(-4)]
    [InlineData(-3)]
    [InlineData(-2)]
    [InlineData(3)]
    public void Winlogon_narrating_itself_is_not_a_failure(int code)
    {
        Assert.Equal(LogonOutcome.Notice, LogonFailure.Classify(code));
        Assert.False(LogonFailure.IsWorthAskingAgain(code));
    }

    [Fact]
    public void The_two_classifiers_never_disagree()
    {
        // Notice is defined by one method and consumed by another, and the
        // range they are arguing about is small enough to enumerate. Anything
        // the router calls a notice must land as Notice here and nowhere else.
        for (int code = -20; code <= 20; code++)
        {
            Assert.Equal(
                SessionSignalRouter.IsWinlogonNotice(code),
                LogonFailure.Classify(code) is LogonOutcome.Notice);
        }
    }

    // ── The password may simply be wrong ────────────────────────────────

    [Theory]
    [InlineData(LogonFailure.StatusLogonFailure)]
    [InlineData(LogonFailure.AccessDenied)]
    public void A_refusal_a_different_password_could_fix_is_worth_asking_about(int code)
    {
        Assert.Equal(LogonOutcome.WrongCredentials, LogonFailure.Classify(code));
        Assert.True(LogonFailure.IsWorthAskingAgain(code));
    }

    [Fact]
    public void Access_denied_is_not_swallowed_for_being_negative()
    {
        // The trap the router already documents: minus one sits in the middle
        // of the arbitration codes without being one of them, and reading the
        // sign alone turns the commonest failure into a notice.
        Assert.False(SessionSignalRouter.IsWinlogonNotice(LogonFailure.AccessDenied));
        Assert.NotEqual(LogonOutcome.Notice, LogonFailure.Classify(LogonFailure.AccessDenied));
    }

    [Fact]
    public void The_logon_failure_code_is_the_one_the_control_actually_reports()
    {
        // Written as a number rather than as an expression, because the point
        // of the constant is that this is what was observed arriving from the
        // control, not what an arithmetic conversion of 0xC000006D produces.
        Assert.Equal(-1073741715, LogonFailure.StatusLogonFailure);
        Assert.Equal(unchecked((int)0xC000006D), LogonFailure.StatusLogonFailure);
    }

    // ── The account will not open, whatever is typed ────────────────────

    [Theory]
    [InlineData(LogonFailure.AccountRestriction)]
    [InlineData(LogonFailure.InvalidLogonHours)]
    [InlineData(LogonFailure.InvalidWorkstation)]
    [InlineData(LogonFailure.PasswordExpired)]
    [InlineData(LogonFailure.AccountDisabled)]
    [InlineData(LogonFailure.AccountExpired)]
    [InlineData(LogonFailure.PasswordMustChange)]
    [InlineData(LogonFailure.AccountLockedOut)]
    public void An_account_that_cannot_be_used_is_not_worth_asking_about(int code)
    {
        Assert.Equal(LogonOutcome.AccountUnusable, LogonFailure.Classify(code));
        Assert.False(LogonFailure.IsWorthAskingAgain(code));
    }

    [Fact]
    public void An_expired_password_is_unusable_rather_than_wrong()
    {
        // The judgement call in the whole file. What is on file may be exactly
        // right; what is needed is a change, and that happens at the far end
        // and not in a Patchbay dialog. Asking again would be asking for
        // something the person has already given.
        Assert.Equal(LogonOutcome.AccountUnusable, LogonFailure.Classify(LogonFailure.PasswordExpired));
        Assert.Equal(LogonOutcome.AccountUnusable, LogonFailure.Classify(LogonFailure.PasswordMustChange));
    }

    [Fact]
    public void A_locked_account_is_never_offered_another_attempt()
    {
        // The one case where offering a retry does active harm: every further
        // attempt extends the lockout the person is already serving.
        Assert.False(LogonFailure.IsWorthAskingAgain(LogonFailure.AccountLockedOut));
    }

    [Fact]
    public void The_ntstatus_constants_match_their_documented_values()
    {
        Assert.Equal(unchecked((int)0xC000006E), LogonFailure.AccountRestriction);
        Assert.Equal(unchecked((int)0xC000006F), LogonFailure.InvalidLogonHours);
        Assert.Equal(unchecked((int)0xC0000070), LogonFailure.InvalidWorkstation);
        Assert.Equal(unchecked((int)0xC0000071), LogonFailure.PasswordExpired);
        Assert.Equal(unchecked((int)0xC0000072), LogonFailure.AccountDisabled);
        Assert.Equal(unchecked((int)0xC0000193), LogonFailure.AccountExpired);
        Assert.Equal(unchecked((int)0xC0000224), LogonFailure.PasswordMustChange);
        Assert.Equal(unchecked((int)0xC0000234), LogonFailure.AccountLockedOut);
    }

    // ── Everything else ─────────────────────────────────────────────────

    [Theory]
    [InlineData(42)]
    [InlineData(-1073741790)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void A_code_nobody_has_pinned_down_still_permits_asking_again(int code)
    {
        // M4-07 refuses to invent a sentence for a code nobody has checked,
        // and that stands — nothing here produces wording. What is being
        // decided is only whether to offer a prompt, and a person typing a
        // password once more is not the lockout risk an automatic retry loop
        // is. M4-08 is stricter for exactly that reason.
        Assert.Equal(LogonOutcome.Unknown, LogonFailure.Classify(code));
        Assert.True(LogonFailure.IsWorthAskingAgain(code));
    }

    [Fact]
    public void No_two_constants_share_a_value()
    {
        // A duplicate would compile, would be unreachable in the switch, and
        // would silently classify one of the two cases as the other.
        int[] codes =
        [
            LogonFailure.StatusLogonFailure,
            LogonFailure.AccessDenied,
            LogonFailure.AccountRestriction,
            LogonFailure.InvalidLogonHours,
            LogonFailure.InvalidWorkstation,
            LogonFailure.PasswordExpired,
            LogonFailure.AccountDisabled,
            LogonFailure.AccountExpired,
            LogonFailure.PasswordMustChange,
            LogonFailure.AccountLockedOut,
        ];

        Assert.Equal(codes.Length, codes.Distinct().Count());
    }
}
