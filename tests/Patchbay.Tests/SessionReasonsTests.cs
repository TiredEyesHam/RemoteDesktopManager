using Patchbay.Core.Sessions;

namespace Patchbay.Tests;

/// <summary>
/// What Patchbay says about the numbers the control reports (M4-07).
///
/// The decision under test is which of two sources to trust, and the answer is
/// not the obvious one: the control knows far more about its own codes than any
/// table written here could, <em>except</em> for the two commonest endings of
/// all, where it is flatly wrong.
/// </summary>
public class SessionReasonsTests
{
    /// <summary>Stands in for the control, and records what it was asked.</summary>
    private sealed class Control
    {
        private readonly string? _answer;

        internal Control(string? answer) => _answer = answer;

        internal List<int> Asked { get; } = [];

        internal string? Describe(int reason)
        {
            Asked.Add(reason);
            return _answer;
        }
    }

    // ── Where the words come from ───────────────────────────────────────

    [Fact]
    public void The_control_is_asked_about_a_reason_it_might_know()
    {
        Control control = new("Because of a protocol error, this session will be disconnected.");
        SessionReasons reasons = new(control.Describe);

        Assert.Equal(
            "Because of a protocol error, this session will be disconnected.",
            reasons.Describe(SessionSignal.Disconnected, 3334));

        Assert.Equal([3334], control.Asked);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(0)]
    public void The_control_is_never_asked_about_an_ordinary_ending(int reason)
    {
        // Asked about reason 1 — a disconnect this computer requested — the
        // real control answers "An internal error has occurred", and the same
        // for reason 2, somebody signing out. Those are the two commonest ways
        // a session ends.
        Control control = new("An internal error has occurred.");
        SessionReasons reasons = new(control.Describe);

        string text = reasons.Describe(SessionSignal.Disconnected, reason);

        Assert.Empty(control.Asked);
        Assert.DoesNotContain("internal error", text);
    }

    [Theory]
    [InlineData(1, "ended from this computer")]
    [InlineData(2, "signed out")]
    [InlineData(3, "remote computer ended")]
    [InlineData(0, "did not say why")]
    public void Each_ordinary_ending_has_words_of_its_own(int reason, string expected)
    {
        Assert.Contains(expected, new SessionReasons().Describe(SessionSignal.Disconnected, reason));
    }

    [Fact]
    public void The_ordinary_endings_are_exactly_the_ones_the_router_calls_ordinary()
    {
        // Two places decide the same thing, so they are asserted against each
        // other rather than left to drift. Reason 0 is the exception both ways:
        // it has words here and is not an ordinary ending there, because "no
        // information" during a connect attempt is a failed connection.
        foreach (int reason in Enumerable.Range(-5, 4000))
        {
            bool ordinaryHere = SessionReasons.OrdinaryEnding(reason) is not null;
            bool ordinaryThere = SessionSignalRouter.IsOrdinaryDisconnect(reason);

            if (reason == SessionReasons.DisconnectNoInformation)
            {
                Assert.True(ordinaryHere);
                Assert.False(ordinaryThere);
                continue;
            }

            Assert.Equal(ordinaryThere, ordinaryHere);
        }
    }

    [Fact]
    public void With_no_control_to_ask_the_number_stands()
    {
        Assert.Equal("error code 3334", new SessionReasons().Describe(SessionSignal.Disconnected, 3334));
    }

    [Fact]
    public void A_control_that_has_nothing_to_say_falls_back_to_the_number()
    {
        SessionReasons reasons = new(_ => null);

        Assert.Equal("error code 2825", reasons.Describe(SessionSignal.Disconnected, 2825));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\n  \t ")]
    public void Whitespace_from_the_control_is_nothing_to_say(string answer)
    {
        SessionReasons reasons = new(_ => answer);

        Assert.Equal("error code 2825", reasons.Describe(SessionSignal.Disconnected, 2825));
    }

    // ── Tidying what comes back ─────────────────────────────────────────

    [Fact]
    public void The_controls_message_box_layout_is_collapsed()
    {
        // Its strings were laid out for a message box and carry embedded
        // newlines and doubled spaces. Dropped into a status bar unedited they
        // arrive with holes in them.
        string raw = "Your Remote Desktop Services session has ended.\r\n\r\n"
            + "Your network administrator might have ended the connection.  Try connecting again.";

        Assert.Equal(
            "Your Remote Desktop Services session has ended. Your network administrator might "
            + "have ended the connection. Try connecting again.",
            SessionReasons.Tidy(raw));
    }

    [Fact]
    public void Tidying_nothing_gives_nothing()
    {
        Assert.Null(SessionReasons.Tidy(null));
        Assert.Null(SessionReasons.Tidy(""));
        Assert.Null(SessionReasons.Tidy("  \r\n "));
    }

    [Fact]
    public void Tidying_leaves_an_already_tidy_sentence_alone()
    {
        Assert.Equal("The session ended.", SessionReasons.Tidy("The session ended."));
    }

    // ── Signing in ──────────────────────────────────────────────────────

    [Fact]
    public void A_refused_password_says_so_rather_than_giving_a_number()
    {
        Assert.Equal(
            "The user name or password is not correct.",
            new SessionReasons().Describe(SessionSignal.LogonError, SessionReasons.LogonBadCredentials));
    }

    [Fact]
    public void A_refused_account_is_a_different_thing_from_a_refused_password()
    {
        Assert.Equal(
            "The remote computer refused the account.",
            new SessionReasons().Describe(SessionSignal.LogonError, SessionReasons.LogonAccessDenied));
    }

    [Theory]
    [InlineData(-7)]
    [InlineData(-5)]
    [InlineData(-2)]
    [InlineData(3)]
    public void Winlogon_narrating_itself_is_not_reported_as_a_problem(int code)
    {
        // The trap is that this is not simply "negative": -1 is a refusal and
        // so is -1073741715.
        Assert.Contains(
            "sign-in prompt",
            new SessionReasons().Describe(SessionSignal.LogonError, code));
    }

    [Fact]
    public void A_logon_code_nobody_has_pinned_down_is_reported_as_itself()
    {
        // Rather than as a plausible sentence about a code nobody has checked.
        // M4-10 earns more of these, because it has to tell a wrong password
        // apart from a locked account to know whether asking again is any use.
        Assert.Equal(
            "error code -1073741260",
            new SessionReasons().Describe(SessionSignal.LogonError, -1073741260));
    }

    [Fact]
    public void The_control_is_not_asked_about_a_logon_code()
    {
        // GetErrorDescription is about disconnects. Handing it a logon code
        // returns a confident sentence about a completely different failure.
        Control control = new("Remote Desktop can't find the computer.");
        SessionReasons reasons = new(control.Describe);

        reasons.Describe(SessionSignal.LogonError, -1073741715);

        Assert.Empty(control.Asked);
    }

    // ── Everything else ─────────────────────────────────────────────────

    [Fact]
    public void A_fatal_error_is_reported_as_its_code()
    {
        // No table, deliberately. Nothing has verified what these mean, and a
        // plausible wrong sentence is worse than a number somebody can search
        // for, because only one of the two is obviously incomplete.
        Assert.Equal("error code 6", new SessionReasons().Describe(SessionSignal.FatalError, 6));
    }

    [Fact]
    public void The_describer_is_the_shape_the_router_takes()
    {
        SessionStateMachine machine = new();
        SessionReasons reasons = new(_ => "Because of a protocol error, this session will be disconnected.");

        SessionSignalRouter router = new(machine, "web-01:3389", reasons.Describer);

        router.Report(SessionSignal.Connecting);
        router.Report(SessionSignal.Disconnected, 3334);

        Assert.Equal(SessionState.Failed, machine.State);
        Assert.Contains("protocol error", machine.StatusMessage);
    }

    [Fact]
    public void A_session_that_ended_normally_still_reads_normally_through_the_router()
    {
        SessionStateMachine machine = new();
        SessionReasons reasons = new(_ => "An internal error has occurred.");

        SessionSignalRouter router = new(machine, "web-01:3389", reasons.Describer);

        router.Report(SessionSignal.Connecting);
        router.Report(SessionSignal.Connected);
        router.Report(SessionSignal.Disconnected, 2);

        Assert.Equal(SessionState.Disconnected, machine.State);
        Assert.DoesNotContain("internal error", machine.StatusMessage);
    }
}
