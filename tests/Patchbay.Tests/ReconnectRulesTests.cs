using Patchbay.Core.Sessions;

namespace Patchbay.Tests;

/// <summary>
/// Whether a session that ended should be brought back (M4-08).
///
/// The whole of the interesting half of auto-reconnect is here, because the
/// rest is a timer and a button. Two decisions carry almost all the weight:
/// what is allowed to <em>start</em> a sequence, and what is allowed to end
/// one. Getting the first too wide gives an application that chases a hostname
/// that has never resolved; too narrow and it does not survive the reboot it
/// exists for. Getting the second wrong locks somebody's account.
/// </summary>
public class ReconnectRulesTests
{
    private const double Centre = 0.5;

    private static SessionEnding Ending(
        SessionState from,
        SessionState to,
        int? logonError = null) => new()
        {
            From = from,
            To = to,
            LogonError = logonError,
        };

    /// <summary>A working session breaking: the one ending that starts a sequence.</summary>
    private static SessionEnding Break() => Ending(SessionState.Connected, SessionState.Failed);

    private static ReconnectDecision Decide(SessionEnding ending, int attempts = 0)
        => ReconnectRules.Decide(ReconnectPolicy.Default, ending, attempts, Centre);

    // ── Guards ──────────────────────────────────────────────────────────

    [Fact]
    public void A_decision_needs_a_policy()
        => Assert.Throws<ArgumentNullException>(
            () => ReconnectRules.Decide(null!, Break(), 0));

    [Fact]
    public void A_negative_count_of_attempts_is_refused()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => ReconnectRules.Decide(ReconnectPolicy.Default, Break(), -1));

    // ── What starts a sequence ──────────────────────────────────────────

    [Fact]
    public void A_working_session_that_broke_is_reconnected()
    {
        ReconnectDecision decision = Decide(Break());

        Assert.True(decision.ShouldRetry);
        Assert.Equal(1, decision.Attempt);
        Assert.Equal(TimeSpan.FromSeconds(5), decision.Delay);
    }

    [Fact]
    public void An_attempt_that_never_got_anywhere_is_left_alone()
    {
        // Somebody is watching this one: they clicked connect a moment ago, the
        // failure is on screen and there is a button under it. Silently
        // retrying a name that does not resolve, six times with a backoff, only
        // delays the truth.
        Assert.Equal(
            ReconnectVerdict.NotAnInterruption,
            Decide(Ending(SessionState.Connecting, SessionState.Failed)).Verdict);
    }

    [Fact]
    public void Somebody_signing_out_at_the_far_end_is_not_chased()
    {
        // The commonest way a session ends, and the one where reconnecting
        // would be actively rude: it puts the person straight back where they
        // just left.
        Assert.Equal(
            ReconnectVerdict.NotAnInterruption,
            Decide(Ending(SessionState.Connected, SessionState.Disconnected)).Verdict);
    }

    [Fact]
    public void A_disconnect_that_was_asked_for_is_not_chased()
        => Assert.Equal(
            ReconnectVerdict.NotAnInterruption,
            Decide(Ending(SessionState.Disconnecting, SessionState.Disconnected)).Verdict);

    [Fact]
    public void An_attempt_that_was_called_off_is_not_chased()
    {
        // Connecting to Disconnected has meant "somebody changed their mind"
        // since M4-05, and it is the shape a cancelled connect arrives in.
        Assert.Equal(
            ReconnectVerdict.NotAnInterruption,
            Decide(Ending(SessionState.Connecting, SessionState.Disconnected)).Verdict);
    }

    [Theory]
    [InlineData(SessionState.Idle, SessionState.Connecting)]
    [InlineData(SessionState.Connecting, SessionState.Connected)]
    [InlineData(SessionState.Connected, SessionState.Disconnecting)]
    [InlineData(SessionState.Failed, SessionState.Connecting)]
    public void A_move_that_is_not_an_ending_decides_nothing(SessionState from, SessionState to)
        => Assert.Equal(ReconnectVerdict.NotAnInterruption, Decide(Ending(from, to)).Verdict);

    [Fact]
    public void News_that_changes_no_state_is_not_an_ending()
    {
        // What SessionStateMachine.Announce raises, and what the control's own
        // reconnect arrives as: the same state twice. Nothing has ended.
        Assert.Equal(
            ReconnectVerdict.NotAnInterruption,
            Decide(Ending(SessionState.Connected, SessionState.Connected)).Verdict);
    }

    // ── What continues one ──────────────────────────────────────────────

    [Fact]
    public void An_attempt_that_failed_inside_a_sequence_carries_it_on()
    {
        // The case the whole feature exists for. A machine that is rebooting
        // refuses connections for a minute or two before it starts accepting
        // them, so a sequence that stopped at the first refusal would give up
        // exactly where it was needed.
        ReconnectDecision decision = Decide(
            Ending(SessionState.Connecting, SessionState.Failed),
            attempts: 1);

        Assert.True(decision.ShouldRetry);
        Assert.Equal(2, decision.Attempt);
        Assert.Equal(TimeSpan.FromSeconds(10), decision.Delay);
    }

    [Fact]
    public void The_wait_grows_with_each_attempt()
    {
        SessionEnding failed = Ending(SessionState.Connecting, SessionState.Failed);

        Assert.Equal(TimeSpan.FromSeconds(20), Decide(failed, attempts: 2).Delay);
        Assert.Equal(TimeSpan.FromSeconds(40), Decide(failed, attempts: 3).Delay);
    }

    [Fact]
    public void An_ordinary_ending_inside_a_sequence_stops_it()
    {
        // Got back in, and then the far end closed the session. That is not the
        // drop being chased any more; it is a new and deliberate ending.
        Assert.Equal(
            ReconnectVerdict.NotAnInterruption,
            Decide(Ending(SessionState.Connected, SessionState.Disconnected), attempts: 3).Verdict);
    }

    [Fact]
    public void Somebody_calling_off_an_automatic_attempt_stops_the_sequence()
        => Assert.Equal(
            ReconnectVerdict.NotAnInterruption,
            Decide(Ending(SessionState.Connecting, SessionState.Disconnected), attempts: 3).Verdict);

    // ── The refusal rule ────────────────────────────────────────────────

    [Fact]
    public void A_refused_sign_in_is_never_retried()
    {
        // Trying again submits the same credentials to the same account, and
        // enough of that locks it out — a failure Patchbay would have caused
        // rather than reported.
        Assert.Equal(
            ReconnectVerdict.Refused,
            Decide(Ending(
                SessionState.Connected,
                SessionState.Failed,
                SessionReasons.LogonBadCredentials)).Verdict);
    }

    [Fact]
    public void A_refusal_stops_a_sequence_that_is_already_running()
    {
        // The case the ordering exists for: the first reconnect after a drop
        // reaches a machine whose password has since changed. Without this the
        // remaining attempts lock the account out with nobody at the keyboard.
        Assert.Equal(
            ReconnectVerdict.Refused,
            Decide(
                Ending(SessionState.Connecting, SessionState.Failed, SessionReasons.LogonAccessDenied),
                attempts: 2).Verdict);
    }

    [Theory]
    [InlineData(-7)]
    [InlineData(-2)]
    [InlineData(3)]
    public void Winlogon_narrating_itself_is_not_a_refusal(int notice)
    {
        // The trap is that this is not simply "negative" — see
        // SessionSignalRouter.IsWinlogonNotice. A session that dropped after a
        // logon dialog appeared is still a session that dropped.
        Assert.True(Decide(Ending(SessionState.Connected, SessionState.Failed, notice)).ShouldRetry);
    }

    [Fact]
    public void A_refusal_is_reported_even_when_no_sequence_could_have_started()
    {
        // Checked before the counting, so that a first connect refused for a
        // bad password says so rather than saying nothing.
        Assert.Equal(
            ReconnectVerdict.Refused,
            Decide(Ending(
                SessionState.Connecting,
                SessionState.Failed,
                SessionReasons.LogonBadCredentials)).Verdict);
    }

    // ── Switched off, and used up ───────────────────────────────────────

    [Fact]
    public void A_connection_with_reconnecting_switched_off_says_so()
        => Assert.Equal(
            ReconnectVerdict.Disabled,
            ReconnectRules.Decide(ReconnectPolicy.Off, Break(), 0).Verdict);

    [Fact]
    public void Switched_off_beats_every_other_answer()
    {
        // Checked first, so that turning it off is exactly as absolute as it
        // sounds.
        Assert.Equal(
            ReconnectVerdict.Disabled,
            ReconnectRules.Decide(
                ReconnectPolicy.Off,
                Ending(SessionState.Connected, SessionState.Failed, SessionReasons.LogonBadCredentials),
                0).Verdict);
    }

    [Fact]
    public void The_last_permitted_attempt_is_still_made()
        => Assert.True(Decide(Break(), attempts: 9).ShouldRetry);

    [Fact]
    public void The_one_after_it_is_not()
        => Assert.Equal(ReconnectVerdict.Exhausted, Decide(Break(), attempts: 10).Verdict);

    [Fact]
    public void A_limit_of_no_attempts_gives_up_immediately()
    {
        // Distinct from being switched off: this one has tried as hard as it
        // was allowed to, which is a different sentence to read.
        Assert.Equal(
            ReconnectVerdict.Exhausted,
            ReconnectRules.Decide(
                ReconnectPolicy.Default with { AttemptLimit = 0 },
                Break(),
                0).Verdict);
    }

    // ── The shape of an answer ──────────────────────────────────────────

    [Fact]
    public void A_refusal_counts_nothing_and_waits_for_nothing()
    {
        ReconnectDecision decision = Decide(Ending(SessionState.Connected, SessionState.Disconnected));

        Assert.Equal(0, decision.Attempt);
        Assert.Equal(TimeSpan.Zero, decision.Delay);
        Assert.False(decision.ShouldRetry);
    }

    [Fact]
    public void The_resting_answer_is_the_default_one()
    {
        // A decision nobody filled in should read as "do nothing", not as
        // "retry" — which is why NotAnInterruption is the zero.
        Assert.Equal(ReconnectVerdict.NotAnInterruption, default(ReconnectDecision).Verdict);
    }

    [Fact]
    public void The_spread_reaches_the_delay()
        => Assert.Equal(
            TimeSpan.FromSeconds(4),
            ReconnectRules.Decide(ReconnectPolicy.Default, Break(), 0, sample: 0.0).Delay);

    // ── Reading an ending off a transition ──────────────────────────────

    [Fact]
    public void An_ending_needs_a_transition()
        => Assert.Throws<ArgumentNullException>(() => SessionEnding.For(null!));

    [Fact]
    public void An_ending_takes_both_halves_of_the_transition()
    {
        SessionEnding ending = SessionEnding.For(
            new SessionStateChangedEventArgs
            {
                PreviousState = SessionState.Connected,
                State = SessionState.Failed,
            },
            logonError: -1);

        Assert.Equal(SessionState.Connected, ending.From);
        Assert.Equal(SessionState.Failed, ending.To);
        Assert.True(ending.IsBreak);
        Assert.True(ending.IsRefusal);
    }

    [Fact]
    public void An_ending_with_no_logon_error_is_not_a_refusal()
        => Assert.False(Break().IsRefusal);
}
