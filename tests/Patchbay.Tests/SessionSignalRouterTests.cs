using Patchbay.Core.Sessions;

namespace Patchbay.Tests;

/// <summary>
/// What the control says, and what Patchbay concludes from it (M4-06).
///
/// The events themselves cannot be tested here — they come from an ActiveX
/// control on a Windows target this project deliberately cannot reference, and
/// half of them need a server at the other end behaving badly. What can be
/// tested, and is where every interesting mistake lives, is the reading: the
/// same <c>OnDisconnected</c> arrives for a clean log off, a wrong password, a
/// pulled cable and a closed tab, and telling someone the wrong one of those
/// is how they retry a connection they meant to end, or give up on one that
/// only needed a password.
/// </summary>
public class SessionSignalRouterTests
{
    // Real codes, from the documented tables. Named rather than inlined
    // because "2308" tells a later reader nothing about why it is a failure.
    private const int NoInformation = 0;
    private const int LocalDisconnect = 1;
    private const int RemoteDisconnectByUser = 2;
    private const int RemoteDisconnectByServer = 3;
    private const int SocketClosed = 2308;
    private const int LogonFailedBadPassword = 0;
    private const int AccessDenied = -1;
    private const int StatusLogonFailure = -1073741715;
    private const int ArbitrationContinueLogon = -2;
    private const int AccountLockedOut = -1073741260;

    private const string Endpoint = "web-01:3389";

    private static (SessionStateMachine Machine, SessionSignalRouter Router) Live()
    {
        (SessionStateMachine machine, SessionSignalRouter router) = Fresh();
        router.Report(SessionSignal.Connecting);
        router.Report(SessionSignal.Connected);

        return (machine, router);
    }

    private static (SessionStateMachine Machine, SessionSignalRouter Router) Fresh()
    {
        SessionStateMachine machine = new();
        return (machine, new SessionSignalRouter(machine, Endpoint));
    }

    [Fact]
    public void A_router_needs_a_machine_and_somewhere_to_say_it_is_connecting_to()
    {
        SessionStateMachine machine = new();

        Assert.Throws<ArgumentNullException>(() => new SessionSignalRouter(null!, Endpoint));
        Assert.Throws<ArgumentException>(() => new SessionSignalRouter(machine, "  "));
    }

    [Fact]
    public void An_attempt_starting_is_reported_with_the_place_it_is_going()
    {
        (SessionStateMachine machine, SessionSignalRouter router) = Fresh();

        router.Report(SessionSignal.Connecting);

        Assert.Equal(SessionState.Connecting, machine.State);
        Assert.Contains(Endpoint, machine.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Pixels_arrive_before_anyone_has_signed_in()
    {
        (SessionStateMachine machine, SessionSignalRouter router) = Live();

        // OnConnected means the transport is up and a logon screen is showing.
        // Waiting for OnLoginComplete to call the session live would leave a
        // tab spinning at the very moment it wants typing into.
        Assert.Equal(SessionState.Connected, machine.State);
        Assert.False(router.HasLoggedOn);
    }

    [Fact]
    public void Signing_in_is_worth_knowing_and_changes_nothing()
    {
        (SessionStateMachine machine, SessionSignalRouter router) = Live();
        int changes = 0;
        machine.Changed += (_, _) => changes++;

        router.Report(SessionSignal.LoggedOn);

        Assert.True(router.HasLoggedOn);
        Assert.Equal(SessionState.Connected, machine.State);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void A_disconnect_before_the_session_came_up_is_a_failed_attempt()
    {
        (SessionStateMachine machine, SessionSignalRouter router) = Fresh();
        router.Report(SessionSignal.Connecting);

        // 1800 is what a real control answers when it cannot reach the far
        // end. Reporting that as "Disconnected" would show a tab that looks
        // like it worked and then stopped.
        router.Report(SessionSignal.Disconnected, 1800);

        Assert.Equal(SessionState.Failed, machine.State);
        Assert.Contains(Endpoint, machine.StatusMessage, StringComparison.Ordinal);
        Assert.Contains("1800", machine.StatusMessage, StringComparison.Ordinal);
        Assert.Equal(1800, router.LastDisconnectReason);
    }

    [Fact]
    public void Calling_off_an_attempt_is_not_a_failed_attempt()
    {
        (SessionStateMachine machine, SessionSignalRouter router) = Fresh();
        router.Report(SessionSignal.Connecting);

        // Cancelling mid-connect ends up here: the control reports a local
        // disconnect, and the documentation is explicit that it is not an
        // error code.
        router.Report(SessionSignal.Disconnected, LocalDisconnect);

        Assert.Equal(SessionState.Disconnected, machine.State);
    }

    [Theory]
    [InlineData(RemoteDisconnectByUser)]
    [InlineData(RemoteDisconnectByServer)]
    public void Logging_off_at_the_far_end_ends_the_session_without_failing_it(int reason)
    {
        (SessionStateMachine machine, SessionSignalRouter router) = Live();

        router.Report(SessionSignal.Disconnected, reason);

        Assert.Equal(SessionState.Disconnected, machine.State);
        Assert.True(machine.CanConnect);
    }

    [Fact]
    public void A_live_session_that_breaks_is_a_failure()
    {
        (SessionStateMachine machine, SessionSignalRouter router) = Live();

        router.Report(SessionSignal.Disconnected, SocketClosed);

        // The difference from the test above is the reason code and nothing
        // else. It is the only evidence there is, which is why it must not be
        // dropped on the way through.
        Assert.Equal(SessionState.Failed, machine.State);
    }

    [Fact]
    public void A_session_that_ends_for_no_stated_reason_is_not_called_a_failure()
    {
        (SessionStateMachine machine, SessionSignalRouter router) = Live();

        router.Report(SessionSignal.Disconnected, NoInformation);

        // Nothing says anything broke. Announcing a failure on no evidence is
        // how people learn to ignore the times there was some.
        Assert.Equal(SessionState.Disconnected, machine.State);
    }

    [Fact]
    public void A_disconnect_Patchbay_asked_for_is_a_disconnect_whatever_the_code_says()
    {
        (SessionStateMachine machine, SessionSignalRouter router) = Live();
        machine.MoveTo(SessionState.Disconnecting, "Disconnecting…");

        // Tearing a connection down mid-flight can produce an alarming code
        // for something the user asked for by closing a tab.
        router.Report(SessionSignal.Disconnected, SocketClosed);

        Assert.Equal(SessionState.Disconnected, machine.State);
    }

    [Theory]
    [InlineData(LogonFailedBadPassword)]
    [InlineData(AccessDenied)]
    [InlineData(StatusLogonFailure)]
    public void A_rejected_logon_does_not_end_the_session(int error)
    {
        (SessionStateMachine machine, SessionSignalRouter router) = Live();

        router.Report(SessionSignal.LogonError, error);

        // The control keeps the connection and puts a logon screen up. Tearing
        // the tab down here is what would make M4-10 impossible.
        Assert.Equal(SessionState.Connected, machine.State);
        Assert.True(router.HasUnreportedProblem);
        Assert.Equal(error, router.LastLogonError);
    }

    [Fact]
    public void The_disconnect_that_follows_a_rejected_logon_reports_the_logon()
    {
        (SessionStateMachine machine, SessionSignalRouter router) = Live();

        router.Report(SessionSignal.LogonError, StatusLogonFailure);
        router.Report(SessionSignal.Disconnected, LocalDisconnect);

        // Without the memory this reads "Disconnected", and someone whose
        // password was refused is told nothing at all. Note the reason code
        // says local disconnect: on its own it is not even an error.
        Assert.Equal(SessionState.Failed, machine.State);
        Assert.Contains("sign in", machine.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(router.HasUnreportedProblem);
    }

    [Theory]
    [InlineData(-7)]
    [InlineData(-6)]
    [InlineData(-5)]
    [InlineData(-4)]
    [InlineData(-3)]
    [InlineData(ArbitrationContinueLogon)]
    [InlineData(3)]
    public void Winlogon_narrating_itself_is_not_a_problem(int notice)
    {
        (SessionStateMachine machine, SessionSignalRouter router) = Live();

        router.Report(SessionSignal.LogonError, notice);
        router.Report(SessionSignal.Disconnected, RemoteDisconnectByUser);

        Assert.Equal(SessionState.Disconnected, machine.State);
    }

    [Fact]
    public void Being_denied_access_is_a_failure_even_though_the_code_is_negative()
    {
        // The tempting rule is "negative means informational". It is wrong
        // twice over, and both exceptions are the cases people actually hit.
        Assert.False(SessionSignalRouter.IsWinlogonNotice(AccessDenied));
        Assert.False(SessionSignalRouter.IsWinlogonNotice(StatusLogonFailure));
        Assert.True(SessionSignalRouter.IsWinlogonNotice(ArbitrationContinueLogon));
    }

    [Fact]
    public void A_broken_control_fails_the_session_at_once()
    {
        (SessionStateMachine machine, SessionSignalRouter router) = Live();

        router.Report(SessionSignal.FatalError, 5);

        // Unlike a logon error there is nothing left to interact with, so this
        // one does not wait for a disconnect to confirm it.
        Assert.Equal(SessionState.Failed, machine.State);
        Assert.Contains("control failed", machine.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void The_disconnect_after_a_fatal_error_does_not_talk_over_it()
    {
        (SessionStateMachine machine, SessionSignalRouter router) = Live();

        router.Report(SessionSignal.FatalError, 5);
        string? failure = machine.StatusMessage;
        router.Report(SessionSignal.Disconnected, LocalDisconnect);

        Assert.Equal(SessionState.Failed, machine.State);
        Assert.Equal(failure, machine.StatusMessage);
    }

    [Fact]
    public void A_second_attempt_does_not_inherit_the_first_ones_failure()
    {
        (SessionStateMachine machine, SessionSignalRouter router) = Live();
        router.Report(SessionSignal.LogonError, StatusLogonFailure);
        router.Report(SessionSignal.Disconnected, LocalDisconnect);

        router.Report(SessionSignal.Connecting);
        Assert.False(router.HasUnreportedProblem);

        router.Report(SessionSignal.Connected);
        router.Report(SessionSignal.Disconnected, RemoteDisconnectByUser);

        // A retry after a bad password must be able to end normally. Carrying
        // the old failure forward would mark every later session failed.
        Assert.Equal(SessionState.Disconnected, machine.State);
    }

    [Fact]
    public void The_same_news_twice_changes_nothing()
    {
        (SessionStateMachine machine, SessionSignalRouter router) = Live();
        router.Report(SessionSignal.Disconnected, RemoteDisconnectByUser);

        int changes = 0;
        machine.Changed += (_, _) => changes++;
        router.Report(SessionSignal.Disconnected, RemoteDisconnectByUser);
        router.Report(SessionSignal.Connected);

        Assert.Equal(0, changes);
        Assert.Equal(SessionState.Disconnected, machine.State);
    }

    [Fact]
    public void The_code_is_turned_into_words_by_whoever_was_given_the_job()
    {
        SessionStateMachine machine = new();
        SessionSignalRouter router = new(
            machine,
            Endpoint,
            (signal, code) => $"{signal} says {code}");

        router.Report(SessionSignal.Connecting);
        router.Report(SessionSignal.Disconnected, SocketClosed);

        // M4-07 replaces the default describer with the real table; this is
        // the seam it plugs into, and it is checked here so that swapping it
        // cannot silently stop being used.
        Assert.Contains("Disconnected says 2308", machine.StatusMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(LocalDisconnect, true)]
    [InlineData(RemoteDisconnectByUser, true)]
    [InlineData(RemoteDisconnectByServer, true)]
    [InlineData(NoInformation, false)]
    [InlineData(SocketClosed, false)]
    [InlineData(260, false)]
    public void Only_the_three_the_documentation_calls_harmless_are_ordinary(int reason, bool ordinary)
        => Assert.Equal(ordinary, SessionSignalRouter.IsOrdinaryDisconnect(reason));

    [Fact]
    public void A_signal_from_nowhere_is_refused_rather_than_ignored()
    {
        (_, SessionSignalRouter router) = Fresh();

        Assert.Throws<ArgumentOutOfRangeException>(() => router.Report((SessionSignal)99));
    }

    // ── The control's own reconnect (M4-08) ─────────────────────────────

    [Fact]
    public void The_control_rejoining_a_session_ends_nothing()
    {
        // It has not lost the session — it is holding it open and rejoining it
        // with the cookie the server issued. Calling that a disconnect would
        // tear down a tab that is about to come back with its desktop intact.
        (SessionStateMachine machine, SessionSignalRouter router) = Live();

        router.Report(
            SessionSignal.Reconnecting,
            SocketClosed,
            new SessionReconnectNotice { Attempt = 2, MaxAttempts = 5 });

        Assert.Equal(SessionState.Connected, machine.State);
        Assert.Equal("Reconnecting to web-01:3389 — attempt 2 of 5…", machine.StatusMessage);
    }

    [Fact]
    public void A_computer_that_is_offline_is_told_so()
    {
        // The one form of the problem the person in front of it can act on:
        // the far end is fine and their own network is not.
        (SessionStateMachine machine, SessionSignalRouter router) = Live();

        router.Report(
            SessionSignal.Reconnecting,
            SocketClosed,
            new SessionReconnectNotice { Attempt = 1, MaxAttempts = 5, NetworkLost = true });

        Assert.Equal(
            "Reconnecting to web-01:3389 — attempt 1 of 5 (this computer is offline)",
            machine.StatusMessage);
    }

    [Fact]
    public void A_reconnect_with_no_detail_still_says_something()
    {
        (SessionStateMachine machine, SessionSignalRouter router) = Live();

        router.Report(SessionSignal.Reconnecting);

        Assert.Equal("Reconnecting to web-01:3389…", machine.StatusMessage);
    }

    [Fact]
    public void Getting_the_session_back_says_so_and_moves_nothing()
    {
        (SessionStateMachine machine, SessionSignalRouter router) = Live();

        router.Report(
            SessionSignal.Reconnecting,
            SocketClosed,
            new SessionReconnectNotice { Attempt = 1, MaxAttempts = 5 });
        router.Report(SessionSignal.Reconnected);

        Assert.Equal(SessionState.Connected, machine.State);
        Assert.Equal("Reconnected to web-01:3389.", machine.StatusMessage);
    }

    [Fact]
    public void A_control_that_gives_up_reconnecting_reports_an_ordinary_disconnect()
    {
        // What actually happens when the control exhausts its own attempts: the
        // reconnect notices stop and an OnDisconnected arrives. From there it is
        // the same path as any other drop, which is what puts Patchbay's own
        // layer (M4-08) in charge.
        (SessionStateMachine machine, SessionSignalRouter router) = Live();

        router.Report(
            SessionSignal.Reconnecting,
            SocketClosed,
            new SessionReconnectNotice { Attempt = 5, MaxAttempts = 5 });
        router.Report(SessionSignal.Disconnected, SocketClosed);

        Assert.Equal(SessionState.Failed, machine.State);
    }

    // ── An idle session (M4-15) ─────────────────────────────────────────

    [Fact]
    public void An_idle_timeout_ends_nothing_by_itself()
    {
        // The control raises the notification and then waits. A router that
        // read it as an ending would report a disconnect over the top of a
        // desktop that is still there and still usable.
        (SessionStateMachine machine, SessionSignalRouter router) = Live();

        router.Report(SessionSignal.IdleTimedOut);

        Assert.Equal(SessionState.Connected, machine.State);
    }

    [Fact]
    public void An_idle_timeout_says_what_is_about_to_happen()
    {
        (SessionStateMachine machine, SessionSignalRouter router) = Live();

        router.Report(SessionSignal.IdleTimedOut);

        Assert.Equal(
            "The session to web-01:3389 has been idle and is being closed.",
            machine.StatusMessage);
    }

    [Fact]
    public void An_idle_session_is_remembered_as_idle()
    {
        (_, SessionSignalRouter router) = Live();

        Assert.False(router.IsIdle);

        router.Report(SessionSignal.IdleTimedOut);

        Assert.True(router.IsIdle);
    }

    [Fact]
    public void A_fresh_attempt_forgets_that_the_last_one_went_idle()
    {
        (_, SessionSignalRouter router) = Live();
        router.Report(SessionSignal.IdleTimedOut);
        router.Report(SessionSignal.Disconnected, 1);

        router.Report(SessionSignal.Connecting);

        Assert.False(router.IsIdle);
    }

    [Fact]
    public void The_disconnect_that_follows_an_idle_timeout_is_an_ordinary_one()
    {
        // Which is the whole reason the closing goes out through Disconnect
        // rather than by moving the state here: it arrives as a disconnect
        // somebody asked for, and M4-08 leaves those alone. An idle timeout
        // that reconnected itself would be a timeout in name only.
        (SessionStateMachine machine, SessionSignalRouter router) = Live();
        router.Report(SessionSignal.IdleTimedOut);

        machine.MoveTo(SessionState.Disconnecting, "Closing an idle session.");
        router.Report(SessionSignal.Disconnected, 1);

        SessionEnding ending = new()
        {
            From = SessionState.Disconnecting,
            To = SessionState.Disconnected,
        };

        Assert.Equal(SessionState.Disconnected, machine.State);
        Assert.True(ending.WasCalledOff);
    }

    // ── A server that cannot be proved (M4-09) ──────────────────────────

    [Fact]
    public void A_warning_about_the_server_ends_nothing_and_starts_nothing()
    {
        // The control has stopped to ask a person a question. Reading that as
        // a failure would put a retry button under a connection that is still
        // going perfectly well, and reading it as a connection would report a
        // session that does not exist yet.
        (SessionStateMachine machine, SessionSignalRouter router) = Fresh();
        router.Report(SessionSignal.Connecting);

        router.Report(SessionSignal.AuthenticationWarningDisplayed);

        Assert.Equal(SessionState.Connecting, machine.State);
    }

    [Fact]
    public void A_warning_about_the_server_says_where_the_warning_is()
    {
        // Where matters as much as what: the dialog belongs to the control, so
        // it is inside the session's own window rather than over the shell,
        // and somebody looking at a tab that says "Connecting…" has no reason
        // to go looking for it.
        (SessionStateMachine machine, SessionSignalRouter router) = Fresh();
        router.Report(SessionSignal.Connecting);

        router.Report(SessionSignal.AuthenticationWarningDisplayed);

        Assert.Equal(
            "web-01:3389 could not be proved to be the computer it says it is. "
            + "The session is waiting for you to answer the warning on it.",
            machine.StatusMessage);
    }

    [Fact]
    public void A_session_waiting_on_a_warning_is_remembered_as_waiting()
    {
        (_, SessionSignalRouter router) = Fresh();
        router.Report(SessionSignal.Connecting);

        Assert.False(router.IsAwaitingTrustDecision);

        router.Report(SessionSignal.AuthenticationWarningDisplayed);

        Assert.True(router.IsAwaitingTrustDecision);
    }

    [Fact]
    public void The_warning_going_away_stops_the_waiting()
    {
        (_, SessionSignalRouter router) = Fresh();
        router.Report(SessionSignal.Connecting);
        router.Report(SessionSignal.AuthenticationWarningDisplayed);

        router.Report(SessionSignal.AuthenticationWarningDismissed);

        Assert.False(router.IsAwaitingTrustDecision);
    }

    [Fact]
    public void The_warning_going_away_does_not_say_which_way_it_was_answered()
    {
        // The control does not say, and no member on it says either. So the
        // session goes back to the sentence it had before the question was
        // asked, and the answer arrives as whatever happens next.
        (SessionStateMachine machine, SessionSignalRouter router) = Fresh();
        router.Report(SessionSignal.Connecting);
        router.Report(SessionSignal.AuthenticationWarningDisplayed);

        router.Report(SessionSignal.AuthenticationWarningDismissed);

        Assert.Equal(SessionState.Connecting, machine.State);
        Assert.Equal("Connecting to web-01:3389…", machine.StatusMessage);
    }

    [Fact]
    public void Accepting_the_warning_is_the_connection_that_follows_it()
    {
        (SessionStateMachine machine, SessionSignalRouter router) = Fresh();
        router.Report(SessionSignal.Connecting);
        router.Report(SessionSignal.AuthenticationWarningDisplayed);
        router.Report(SessionSignal.AuthenticationWarningDismissed);

        router.Report(SessionSignal.Connected);

        Assert.Equal(SessionState.Connected, machine.State);
        Assert.False(router.IsAwaitingTrustDecision);
    }

    [Fact]
    public void Refusing_the_warning_is_the_failed_attempt_that_follows_it()
    {
        // A refusal arrives as an ordinary disconnect on a connection that
        // never came up, which M4-06 already reads as a failed attempt. There
        // is nothing to add here and adding something would mean inventing the
        // one fact the control withheld.
        (SessionStateMachine machine, SessionSignalRouter router) = Fresh();
        router.Report(SessionSignal.Connecting);
        router.Report(SessionSignal.AuthenticationWarningDisplayed);
        router.Report(SessionSignal.AuthenticationWarningDismissed);

        router.Report(SessionSignal.Disconnected, NoInformation);

        Assert.Equal(SessionState.Failed, machine.State);
    }

    [Fact]
    public void A_fresh_attempt_forgets_that_the_last_one_stopped_to_ask()
    {
        (_, SessionSignalRouter router) = Fresh();
        router.Report(SessionSignal.Connecting);
        router.Report(SessionSignal.AuthenticationWarningDisplayed);
        router.Report(SessionSignal.Disconnected, NoInformation);

        router.Report(SessionSignal.Connecting);

        Assert.False(router.IsAwaitingTrustDecision);
    }

    [Fact]
    public void A_warning_dismissed_on_a_live_session_says_nothing_over_the_top_of_it()
    {
        // Defensive, and the reason is the shape of the message rather than a
        // case the control is known to produce: putting "Connecting…" on a
        // session that is already connected would be a status bar walking
        // backwards.
        (SessionStateMachine machine, SessionSignalRouter router) = Live();
        string? before = machine.StatusMessage;

        router.Report(SessionSignal.AuthenticationWarningDismissed);

        Assert.Equal(before, machine.StatusMessage);
        Assert.Equal(SessionState.Connected, machine.State);
    }

    // ── A sign-in the far end will not take (M4-10) ──

    [Fact]
    public void A_refused_password_leaves_the_session_up_and_asks_again()
    {
        // The whole of M4-10 in one assertion: nothing ended, nothing failed,
        // and there is now something to ask.
        (SessionStateMachine machine, SessionSignalRouter router) = Live();

        router.Report(SessionSignal.LogonError, StatusLogonFailure);

        Assert.Equal(SessionState.Connected, machine.State);
        Assert.True(router.IsAwaitingCredentials);
        Assert.False(router.HasLoggedOn);
    }

    [Fact]
    public void A_refused_account_says_so_without_offering_anything()
    {
        // Locked out. Asking again is asking somebody to extend their own
        // lockout, so the sentence explains and stops there.
        (SessionStateMachine machine, SessionSignalRouter router) = Live();

        router.Report(SessionSignal.LogonError, AccountLockedOut);

        Assert.Equal(SessionState.Connected, machine.State);
        Assert.False(router.IsAwaitingCredentials);
        Assert.Contains("refused the account", machine.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Winlogon_talking_to_itself_asks_for_nothing()
    {
        (_, SessionSignalRouter router) = Live();

        router.Report(SessionSignal.LogonError, ArbitrationContinueLogon);

        Assert.False(router.IsAwaitingCredentials);
    }

    [Fact]
    public void Signing_in_withdraws_the_question()
    {
        // Two prompts in, and the third password worked. Leaving the offer up
        // would be a panel asking for a password over a live desktop.
        (_, SessionSignalRouter router) = Live();
        router.Report(SessionSignal.LogonError, StatusLogonFailure);

        router.Report(SessionSignal.LoggedOn);

        Assert.False(router.IsAwaitingCredentials);
        Assert.True(router.HasLoggedOn);
    }

    [Fact]
    public void The_question_goes_when_the_session_does()
    {
        // A prompt still showing over a tab that has ended is asking for a
        // password nothing will be done with.
        (_, SessionSignalRouter router) = Live();
        router.Report(SessionSignal.LogonError, StatusLogonFailure);

        router.Report(SessionSignal.Disconnected, SocketClosed);

        Assert.False(router.IsAwaitingCredentials);
    }

    [Fact]
    public void A_fresh_attempt_is_not_still_asking()
    {
        (_, SessionSignalRouter router) = Live();
        router.Report(SessionSignal.LogonError, StatusLogonFailure);

        router.Report(SessionSignal.Connecting);

        Assert.False(router.IsAwaitingCredentials);
    }

    [Fact]
    public void The_refusal_is_still_the_reason_if_the_session_then_ends()
    {
        // The offer and the held failure are separate things and both have to
        // survive the same event. Somebody who ignores the prompt and loses
        // the connection must still be told the password was refused rather
        // than that they were disconnected.
        (SessionStateMachine machine, SessionSignalRouter router) = Live();
        router.Report(SessionSignal.LogonError, StatusLogonFailure);

        router.Report(SessionSignal.Disconnected, NoInformation);

        Assert.Equal(SessionState.Failed, machine.State);
        Assert.False(router.IsAwaitingCredentials);
    }
}
