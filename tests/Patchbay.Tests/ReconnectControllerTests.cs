using Patchbay.Core.Sessions;

namespace Patchbay.Tests;

/// <summary>
/// The countdown, the count, and what they say (M4-08).
///
/// Time comes in through <see cref="ReconnectController.Tick"/>, which is what
/// makes any of this testable: every case here involves waiting a minute or
/// seven, and a suite that actually waited would take an hour to tell anybody
/// anything.
/// </summary>
public class ReconnectControllerTests
{
    private const double Centre = 0.5;

    private static ReconnectController Controller(ReconnectPolicy? policy = null)
        => new(policy ?? ReconnectPolicy.Default, () => Centre);

    private static SessionEnding Ending(SessionState from, SessionState to, int? logonError = null)
        => new() { From = from, To = to, LogonError = logonError };

    private static SessionEnding Break() => Ending(SessionState.Connected, SessionState.Failed);

    private static SessionEnding AttemptFailed()
        => Ending(SessionState.Connecting, SessionState.Failed);

    /// <summary>Runs a countdown out and reports whether it came due.</summary>
    private static bool RunOut(ReconnectController controller)
    {
        // A second at a time, as the shell does, with a generous bound so a
        // countdown that never finishes fails the test rather than hanging it.
        for (int second = 0; second < 600; second++)
        {
            if (controller.Tick(TimeSpan.FromSeconds(1)))
            {
                return true;
            }
        }

        return false;
    }

    // ── At rest ─────────────────────────────────────────────────────────

    [Fact]
    public void A_fresh_controller_is_doing_nothing()
    {
        ReconnectController controller = Controller();

        Assert.Equal(ReconnectVerdict.NotAnInterruption, controller.Verdict);
        Assert.False(controller.IsWaiting);
        Assert.False(controller.IsRunning);
        Assert.Equal(0, controller.Attempts);
        Assert.Null(controller.Summary);
    }

    [Fact]
    public void Ticking_with_nothing_pending_does_nothing()
        => Assert.False(Controller().Tick(TimeSpan.FromMinutes(1)));

    [Fact]
    public void Time_does_not_run_backwards()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => Controller().Tick(TimeSpan.FromSeconds(-1)));

    // ── One countdown ───────────────────────────────────────────────────

    [Fact]
    public void A_break_starts_a_countdown()
    {
        ReconnectController controller = Controller();

        Assert.True(controller.Ended(Break()).ShouldRetry);
        Assert.True(controller.IsWaiting);
        Assert.True(controller.IsRunning);
        Assert.Equal(TimeSpan.FromSeconds(5), controller.Remaining);
        Assert.Equal(1, controller.Attempt);
    }

    [Fact]
    public void The_countdown_comes_due_exactly_once()
    {
        ReconnectController controller = Controller();
        controller.Ended(Break());

        Assert.False(controller.Tick(TimeSpan.FromSeconds(4)));
        Assert.True(controller.Tick(TimeSpan.FromSeconds(1)));

        // The attempt is the caller's now; ticking again must not ask for a
        // second one.
        Assert.False(controller.Tick(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void A_tick_longer_than_the_wait_still_comes_due_once()
    {
        // A machine that was asleep, or a dispatcher that was busy laying out a
        // tree. The wait is over, not overdrawn.
        ReconnectController controller = Controller();
        controller.Ended(Break());

        Assert.True(controller.Tick(TimeSpan.FromMinutes(10)));
        Assert.Equal(TimeSpan.Zero, controller.Remaining);
    }

    [Fact]
    public void Coming_due_counts_the_attempt()
    {
        ReconnectController controller = Controller();
        controller.Ended(Break());
        RunOut(controller);

        Assert.Equal(1, controller.Attempts);
        Assert.False(controller.IsWaiting);

        // Still running: the attempt it released has not been answered yet.
        Assert.True(controller.IsRunning);
    }

    // ── A whole sequence ────────────────────────────────────────────────

    [Fact]
    public void A_sequence_makes_exactly_the_permitted_number_of_attempts()
    {
        ReconnectController controller = Controller();
        int made = 0;

        // The drop, then an attempt that fails every time — a machine that is
        // never coming back.
        controller.Ended(Break());

        while (controller.IsWaiting || controller.IsRunning)
        {
            if (RunOut(controller))
            {
                made++;
                controller.Ended(AttemptFailed());
            }
            else
            {
                break;
            }
        }

        Assert.Equal(ReconnectPolicy.Default.AttemptLimit, made);
        Assert.Equal(ReconnectVerdict.Exhausted, controller.Verdict);
        Assert.False(controller.IsRunning);
    }

    [Fact]
    public void Each_wait_in_a_sequence_is_longer_than_the_last()
    {
        ReconnectController controller = Controller();
        List<TimeSpan> waits = [];

        controller.Ended(Break());

        for (int attempt = 0; attempt < 4; attempt++)
        {
            waits.Add(controller.Remaining);
            RunOut(controller);
            controller.Ended(AttemptFailed());
        }

        Assert.Equal(
            [
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(20),
                TimeSpan.FromSeconds(40),
            ],
            waits);
    }

    [Fact]
    public void A_session_that_comes_back_starts_the_next_drop_from_scratch()
    {
        // A connection that drops once a fortnight and recovers every time has
        // made one attempt, over and over — not thirty-six. A counter that only
        // went up would eventually declare a healthy link exhausted.
        ReconnectController controller = Controller();

        controller.Ended(Break());
        RunOut(controller);
        controller.Ended(AttemptFailed());
        RunOut(controller);

        Assert.Equal(2, controller.Attempts);

        controller.Reset();
        controller.Ended(Break());

        Assert.Equal(1, controller.Attempt);
        Assert.Equal(TimeSpan.FromSeconds(5), controller.Remaining);
    }

    // ── Being interrupted ───────────────────────────────────────────────

    [Fact]
    public void Cancelling_stops_the_countdown()
    {
        ReconnectController controller = Controller();
        controller.Ended(Break());
        controller.Cancel();

        Assert.Equal(ReconnectVerdict.Cancelled, controller.Verdict);
        Assert.False(controller.IsWaiting);
        Assert.False(controller.IsRunning);
        Assert.False(RunOut(controller));
    }

    [Fact]
    public void Cancelling_leaves_the_count_where_it_was()
    {
        // Somebody stopping a wait has not said the session is well again, and
        // pretending otherwise would hand a machine that has already failed
        // three times a fresh set of ten.
        ReconnectController controller = Controller();

        controller.Ended(Break());
        RunOut(controller);
        controller.Cancel();

        Assert.Equal(1, controller.Attempts);
    }

    [Fact]
    public void Cancelling_nothing_changes_nothing()
    {
        ReconnectController controller = Controller();
        controller.Cancel();

        Assert.Equal(ReconnectVerdict.NotAnInterruption, controller.Verdict);
    }

    [Fact]
    public void A_refusal_stops_a_sequence_that_was_running()
    {
        ReconnectController controller = Controller();

        controller.Ended(Break());
        RunOut(controller);
        controller.Ended(Ending(
            SessionState.Connecting,
            SessionState.Failed,
            SessionReasons.LogonBadCredentials));

        Assert.Equal(ReconnectVerdict.Refused, controller.Verdict);
        Assert.False(controller.IsRunning);
    }

    // ── What it says ────────────────────────────────────────────────────

    [Fact]
    public void A_countdown_names_the_seconds_and_the_attempt()
    {
        ReconnectController controller = Controller();
        controller.Ended(Break());

        Assert.Equal("Reconnecting in 5 s — attempt 1 of 10", controller.Summary);
    }

    [Fact]
    public void The_countdown_rounds_up_so_it_never_reads_zero_while_waiting()
    {
        // Rounding to nearest would show "in 0 s" for the last half second,
        // which looks like a stuck clock.
        ReconnectController controller = Controller();
        controller.Ended(Break());
        controller.Tick(TimeSpan.FromMilliseconds(4900));

        Assert.Equal("Reconnecting in 1 s — attempt 1 of 10", controller.Summary);
    }

    [Fact]
    public void An_attempt_in_flight_drops_the_countdown_and_keeps_the_number()
    {
        ReconnectController controller = Controller();
        controller.Ended(Break());
        RunOut(controller);

        Assert.Equal("Reconnecting — attempt 1 of 10", controller.Summary);
    }

    [Fact]
    public void Giving_up_says_how_many_times_it_tried()
    {
        ReconnectController controller = Controller(ReconnectPolicy.Default with { AttemptLimit = 2 });

        controller.Ended(Break());
        RunOut(controller);
        controller.Ended(AttemptFailed());
        RunOut(controller);
        controller.Ended(AttemptFailed());

        Assert.Equal("Gave up reconnecting after 2 attempts.", controller.Summary);
    }

    [Fact]
    public void Giving_up_without_trying_says_that_instead()
    {
        // "Gave up after 0 attempts" is arithmetic, not a sentence.
        ReconnectController controller = Controller(ReconnectPolicy.Default with { AttemptLimit = 0 });
        controller.Ended(Break());

        Assert.Equal("Not reconnecting: no attempts are allowed.", controller.Summary);
    }

    [Fact]
    public void A_refusal_says_why_it_is_not_trying()
    {
        ReconnectController controller = Controller();
        controller.Ended(Ending(
            SessionState.Connected,
            SessionState.Failed,
            SessionReasons.LogonBadCredentials));

        Assert.Equal("Not reconnecting: the sign-in was refused.", controller.Summary);
    }

    [Fact]
    public void Being_cancelled_says_so()
    {
        ReconnectController controller = Controller();
        controller.Ended(Break());
        controller.Cancel();

        Assert.Equal("Reconnecting cancelled.", controller.Summary);
    }

    [Fact]
    public void A_connection_with_reconnecting_switched_off_says_nothing_at_all()
    {
        // The session's own failure message is the whole of what there is to
        // say. A line reading "auto-reconnect is disabled" under every failure
        // is noise on every connection somebody deliberately configured.
        ReconnectController controller = Controller(ReconnectPolicy.Off);
        controller.Ended(Break());

        Assert.Null(controller.Summary);
    }

    [Fact]
    public void An_ordinary_ending_says_nothing_either()
    {
        ReconnectController controller = Controller();
        controller.Ended(Ending(SessionState.Connected, SessionState.Disconnected));

        Assert.Null(controller.Summary);
    }

    // ── The spread ──────────────────────────────────────────────────────

    [Fact]
    public void The_spread_is_asked_for_once_per_decision()
    {
        int asked = 0;
        ReconnectController controller = new(ReconnectPolicy.Default, () =>
        {
            asked++;
            return Centre;
        });

        controller.Ended(Break());
        controller.Ended(AttemptFailed());

        Assert.Equal(2, asked);
    }

    [Fact]
    public void The_spread_reaches_the_wait()
    {
        ReconnectController early = new(ReconnectPolicy.Default, () => 0.0);
        early.Ended(Break());

        Assert.Equal(TimeSpan.FromSeconds(4), early.Remaining);
    }

    [Fact]
    public void Two_sessions_dropped_together_do_not_come_back_together()
    {
        // A gateway restarting takes every session through it down at the same
        // instant. Without a spread they all come back at the same instant too,
        // repeatedly, aimed at the machine that has just come up.
        ReconnectController first = new(ReconnectPolicy.Default, () => 0.1);
        ReconnectController second = new(ReconnectPolicy.Default, () => 0.9);

        first.Ended(Break());
        second.Ended(Break());

        Assert.NotEqual(first.Remaining, second.Remaining);
    }
}
