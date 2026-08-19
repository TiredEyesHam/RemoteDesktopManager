using Patchbay.Core.Sessions;

namespace Patchbay.Tests;

/// <summary>
/// The transition table is the contract between Patchbay and every source of
/// session state — the fake, the RDP control's own events, and the reconnect
/// loop that comes with M4-08. It is written out in full here rather than
/// spot-checked, because the interesting failures are the moves that ought to
/// be refused and quietly are not: a session that goes straight from Idle to
/// Connected has skipped every place a credential prompt or a certificate
/// warning could have appeared.
/// </summary>
public class SessionStateMachineTests
{
    /// <summary>
    /// Every move that is allowed, and nothing else is. Kept as data so the
    /// matrix test below can prove the table has no extra doors in it.
    /// </summary>
    private static readonly (SessionState From, SessionState To)[] Legal =
    [
        (SessionState.Idle, SessionState.Connecting),

        (SessionState.Connecting, SessionState.Connected),
        (SessionState.Connecting, SessionState.Disconnecting),
        (SessionState.Connecting, SessionState.Disconnected),
        (SessionState.Connecting, SessionState.Failed),

        (SessionState.Connected, SessionState.Disconnecting),
        (SessionState.Connected, SessionState.Disconnected),
        (SessionState.Connected, SessionState.Failed),

        (SessionState.Disconnecting, SessionState.Disconnected),
        (SessionState.Disconnecting, SessionState.Failed),

        (SessionState.Disconnected, SessionState.Connecting),
        (SessionState.Failed, SessionState.Connecting),
    ];

    private static SessionStateMachine At(SessionState state)
    {
        SessionStateMachine machine = new();

        switch (state)
        {
            case SessionState.Idle:
                break;

            case SessionState.Connecting:
                machine.MoveTo(SessionState.Connecting);
                break;

            case SessionState.Connected:
                machine.MoveTo(SessionState.Connecting);
                machine.MoveTo(SessionState.Connected);
                break;

            case SessionState.Disconnecting:
                machine.MoveTo(SessionState.Connecting);
                machine.MoveTo(SessionState.Connected);
                machine.MoveTo(SessionState.Disconnecting);
                break;

            case SessionState.Disconnected:
                machine.MoveTo(SessionState.Connecting);
                machine.MoveTo(SessionState.Disconnected);
                break;

            case SessionState.Failed:
                machine.MoveTo(SessionState.Connecting);
                machine.MoveTo(SessionState.Failed);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(state));
        }

        return machine;
    }

    [Fact]
    public void A_new_session_has_not_started()
    {
        SessionStateMachine machine = new();

        Assert.Equal(SessionState.Idle, machine.State);
        Assert.Null(machine.StatusMessage);
        Assert.True(machine.CanConnect);
        Assert.False(machine.CanDisconnect);
        Assert.False(machine.IsBusy);
        Assert.False(machine.IsLive);
    }

    [Fact]
    public void The_table_allows_exactly_the_moves_it_says_it_does()
    {
        SessionState[] all = Enum.GetValues<SessionState>();
        List<string> surprises = [];

        foreach (SessionState from in all)
        {
            foreach (SessionState to in all)
            {
                bool expected = Array.Exists(Legal, move => move.From == from && move.To == to);
                bool actual = SessionStateMachine.IsLegal(from, to);

                if (expected != actual)
                {
                    surprises.Add($"{from} -> {to} was {(actual ? "allowed" : "refused")}");
                }
            }
        }

        Assert.Empty(surprises);
    }

    [Fact]
    public void A_session_never_goes_straight_from_idle_to_connected()
    {
        SessionStateMachine machine = new();

        Assert.Throws<InvalidOperationException>(() => machine.MoveTo(SessionState.Connected));
        Assert.Equal(SessionState.Idle, machine.State);
    }

    [Fact]
    public void A_refused_move_names_both_states()
    {
        SessionStateMachine machine = At(SessionState.Connected);

        InvalidOperationException error =
            Assert.Throws<InvalidOperationException>(() => machine.MoveTo(SessionState.Connecting));

        Assert.Contains("Connected", error.Message, StringComparison.Ordinal);
        Assert.Contains("Connecting", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Asking_for_the_state_it_is_already_in_changes_nothing()
    {
        SessionStateMachine machine = At(SessionState.Connected);
        int changes = 0;
        machine.Changed += (_, _) => changes++;

        Assert.False(machine.TryMoveTo(SessionState.Connected, "again"));

        Assert.Equal(0, changes);
        Assert.Equal(SessionState.Connected, machine.State);
    }

    [Fact]
    public void Saying_so_twice_is_not_an_error_when_the_control_is_the_one_saying_it()
    {
        SessionStateMachine machine = At(SessionState.Connected);

        Assert.True(machine.TryMoveTo(SessionState.Disconnected, "The remote computer ended the session."));

        // A second announcement of the same disconnect must not throw: the
        // control reports the world, and the world is allowed to repeat itself.
        Assert.False(machine.TryMoveTo(SessionState.Disconnected, "again"));
        Assert.Equal(SessionState.Disconnected, machine.State);
    }

    [Fact]
    public void A_change_carries_where_it_came_from()
    {
        SessionStateMachine machine = At(SessionState.Connecting);
        SessionStateChangedEventArgs? seen = null;
        machine.Changed += (_, e) => seen = e;

        machine.MoveTo(SessionState.Connected, "Connected to web-01:3389.");

        Assert.NotNull(seen);
        Assert.Equal(SessionState.Connecting, seen.PreviousState);
        Assert.Equal(SessionState.Connected, seen.State);
        Assert.Equal("Connected to web-01:3389.", seen.Message);
        Assert.Equal("Connected to web-01:3389.", machine.StatusMessage);
    }

    [Fact]
    public void A_failure_carries_the_error_as_well_as_the_message()
    {
        SessionStateMachine machine = At(SessionState.Connecting);
        SessionStateChangedEventArgs? seen = null;
        machine.Changed += (_, e) => seen = e;

        RemoteSessionException error = new("The certificate could not be checked.");
        machine.MoveTo(SessionState.Failed, error.Message, error);

        Assert.NotNull(seen);
        Assert.Same(error, seen.Error);
        Assert.Equal(SessionState.Failed, machine.State);
    }

    [Fact]
    public void A_session_dropped_by_the_far_end_is_not_a_failure()
    {
        SessionStateMachine machine = At(SessionState.Connected);

        machine.MoveTo(SessionState.Disconnected, "The remote computer ended the session.");

        // Logging off is a disconnect. Reporting it as a failure would offer a
        // retry to someone who meant to leave.
        Assert.Equal(SessionState.Disconnected, machine.State);
        Assert.True(machine.CanConnect);
    }

    [Theory]
    [InlineData(SessionState.Disconnected)]
    [InlineData(SessionState.Failed)]
    public void A_session_can_be_started_again_after_it_ends(SessionState resting)
    {
        SessionStateMachine machine = At(resting);

        Assert.True(machine.CanConnect);
        machine.MoveTo(SessionState.Connecting);

        Assert.Equal(SessionState.Connecting, machine.State);
    }

    [Theory]
    [InlineData(SessionState.Idle, true, false, false, false)]
    [InlineData(SessionState.Connecting, false, true, true, false)]
    [InlineData(SessionState.Connected, false, true, false, true)]
    [InlineData(SessionState.Disconnecting, false, false, true, false)]
    [InlineData(SessionState.Disconnected, true, false, false, false)]
    [InlineData(SessionState.Failed, true, false, false, false)]
    public void What_the_ui_may_offer_follows_from_the_state(
        SessionState state,
        bool canConnect,
        bool canDisconnect,
        bool isBusy,
        bool isLive)
    {
        SessionStateMachine machine = At(state);

        Assert.Equal(canConnect, machine.CanConnect);
        Assert.Equal(canDisconnect, machine.CanDisconnect);
        Assert.Equal(isBusy, machine.IsBusy);
        Assert.Equal(isLive, machine.IsLive);
    }

    [Fact]
    public void An_attempt_in_flight_can_be_abandoned_without_pretending_it_failed()
    {
        SessionStateMachine machine = At(SessionState.Connecting);

        machine.MoveTo(SessionState.Disconnected, "Connection cancelled.");

        Assert.Equal(SessionState.Disconnected, machine.State);
    }

    [Fact]
    public void Only_one_of_several_threads_starts_the_connection()
    {
        SessionStateMachine machine = new();
        int accepted = 0;

        // The real thing has a UI thread raising control events and a close or
        // a cancel arriving from elsewhere. Whoever gets there first wins, and
        // exactly one of them does.
        Parallel.For(0, 64, _ =>
        {
            if (machine.TryMoveTo(SessionState.Connecting, "Connecting…"))
            {
                Interlocked.Increment(ref accepted);
            }
        });

        Assert.Equal(1, accepted);
        Assert.Equal(SessionState.Connecting, machine.State);
    }

    [Fact]
    public void A_handler_that_ends_the_session_it_was_told_about_does_not_deadlock()
    {
        SessionStateMachine machine = At(SessionState.Connecting);

        // Closing a tab in response to a state change is the obvious thing for
        // the shell to do, and it re-enters immediately.
        machine.Changed += (_, e) =>
        {
            if (e.State is SessionState.Connected)
            {
                machine.MoveTo(SessionState.Disconnecting, "Disconnecting…");
            }
        };

        machine.MoveTo(SessionState.Connected, "Connected.");

        Assert.Equal(SessionState.Disconnecting, machine.State);
    }

    // ── News that is not a move (M4-08) ─────────────────────────────────

    [Fact]
    public void Announcing_changes_the_message_and_not_the_state()
    {
        SessionStateMachine machine = new();
        machine.MoveTo(SessionState.Connecting);
        machine.MoveTo(SessionState.Connected, "Connected.");

        Assert.True(machine.Announce("Reconnecting — attempt 2 of 5…"));
        Assert.Equal(SessionState.Connected, machine.State);
        Assert.Equal("Reconnecting — attempt 2 of 5…", machine.StatusMessage);
    }

    [Fact]
    public void An_announcement_arrives_with_the_same_state_on_both_sides()
    {
        // Which is what marks it as news rather than a move, and what the
        // reconnect rules read as no ending at all — because it is not one.
        SessionStateMachine machine = new();
        machine.MoveTo(SessionState.Connecting);
        machine.MoveTo(SessionState.Connected);

        SessionStateChangedEventArgs? seen = null;
        machine.Changed += (_, e) => seen = e;

        machine.Announce("Reconnecting…");

        Assert.NotNull(seen);
        Assert.Equal(SessionState.Connected, seen.PreviousState);
        Assert.Equal(SessionState.Connected, seen.State);
    }

    [Fact]
    public void A_control_repeating_itself_repaints_nothing()
    {
        SessionStateMachine machine = new();
        machine.MoveTo(SessionState.Connecting);

        int raised = 0;
        machine.Changed += (_, _) => raised++;

        Assert.True(machine.Announce("Reconnecting…"));
        Assert.False(machine.Announce("Reconnecting…"));
        Assert.Equal(1, raised);
    }

    [Fact]
    public void An_announcement_with_nothing_to_say_is_refused()
        => Assert.Throws<ArgumentException>(() => new SessionStateMachine().Announce("  "));
}
