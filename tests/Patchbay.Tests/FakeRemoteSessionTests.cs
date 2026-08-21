using Patchbay.Core.Security;
using Patchbay.Core.Model;
using Patchbay.Core.Sessions;

namespace Patchbay.Tests;

/// <summary>
/// The fake is what the whole UI is built against until M4-02 lands, so its
/// behaviour is a contract in its own right — particularly the paths a real
/// server makes hard to produce on demand: a refused connection, a connect
/// abandoned half-way, and a session dropped from the far end.
/// </summary>
public class FakeRemoteSessionTests
{
    private static readonly SessionRequest Request = new()
    {
        HostName = "web-01",
        Settings = ConnectionSettings.Defaults,
        DisplayName = "WEB-PRD-01",
    };

    private static (FakeRemoteSessionHost Host, IRemoteSession Session) NewSession(
        Action<FakeRemoteSessionHost>? configure = null)
    {
        FakeRemoteSessionHost host = new();
        configure?.Invoke(host);
        return (host, host.CreateSession(Request));
    }

    private static List<SessionStateChangedEventArgs> Record(IRemoteSession session)
    {
        List<SessionStateChangedEventArgs> events = [];
        session.StateChanged += (_, e) => events.Add(e);
        return events;
    }

    [Fact]
    public void A_host_says_plainly_that_it_is_not_real()
    {
        FakeRemoteSessionHost host = new();

        Assert.True(host.IsSimulated);
        Assert.NotEmpty(host.Description);
    }

    [Fact]
    public void A_new_session_is_idle_and_connected_to_nothing()
    {
        (FakeRemoteSessionHost host, IRemoteSession session) = NewSession();

        Assert.Equal(SessionState.Idle, session.State);
        Assert.Null(session.StatusMessage);
        Assert.Same(session, Assert.Single(host.Sessions));
    }

    [Fact]
    public async Task Connecting_goes_through_connecting_on_the_way_to_connected()
    {
        (_, IRemoteSession session) = NewSession();
        List<SessionStateChangedEventArgs> events = Record(session);

        await session.ConnectAsync();

        Assert.Equal(SessionState.Connected, session.State);
        Assert.Equal([SessionState.Connecting, SessionState.Connected], events.Select(e => e.State));
        Assert.Equal(SessionState.Idle, events[0].PreviousState);
        Assert.Contains("web-01:3389", session.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_refused_connection_fails_with_a_message_worth_showing()
    {
        (_, IRemoteSession session) = NewSession(h => h.FailConnections("The computer could not be found."));
        List<SessionStateChangedEventArgs> events = Record(session);

        RemoteSessionException error =
            await Assert.ThrowsAsync<RemoteSessionException>(() => session.ConnectAsync());

        Assert.Equal("The computer could not be found.", error.Message);
        Assert.Equal(SessionState.Failed, session.State);
        Assert.Equal("The computer could not be found.", session.StatusMessage);
        Assert.Same(error, events[^1].Error);
        Assert.Equal(SessionState.Connecting, events[^1].PreviousState);
    }

    [Fact]
    public async Task A_cancelled_connect_ends_disconnected_rather_than_failed()
    {
        (_, IRemoteSession session) = NewSession();
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => session.ConnectAsync(cancelled.Token));

        // Changing your mind is not an error, and must not be reported as one.
        Assert.Equal(SessionState.Disconnected, session.State);
    }

    /// <summary>Closing a tab while its session is still connecting.</summary>
    [Fact]
    public async Task Disconnecting_during_a_connect_abandons_the_attempt()
    {
        (_, IRemoteSession session) = NewSession(h => h.ConnectDelay = TimeSpan.FromMinutes(5));

        Task connecting = session.ConnectAsync();
        Assert.Equal(SessionState.Connecting, session.State);

        await session.DisconnectAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connecting);

        Assert.Equal(SessionState.Disconnected, session.State);
    }

    [Fact]
    public async Task Disconnecting_passes_through_disconnecting()
    {
        (_, IRemoteSession session) = NewSession();
        await session.ConnectAsync();
        List<SessionStateChangedEventArgs> events = Record(session);

        await session.DisconnectAsync();

        Assert.Equal([SessionState.Disconnecting, SessionState.Disconnected], events.Select(e => e.State));
        Assert.Equal(SessionState.Disconnected, session.State);
    }

    /// <summary>Closing a tab should not have to ask whether the session is up.</summary>
    [Fact]
    public async Task Disconnecting_a_session_that_is_already_down_does_nothing()
    {
        (_, IRemoteSession session) = NewSession();
        List<SessionStateChangedEventArgs> events = Record(session);

        await session.DisconnectAsync();

        Assert.Empty(events);
        Assert.Equal(SessionState.Idle, session.State);
    }

    [Fact]
    public async Task A_session_dropped_by_the_far_end_reports_itself()
    {
        (FakeRemoteSessionHost host, IRemoteSession session) = NewSession();
        await session.ConnectAsync();
        List<SessionStateChangedEventArgs> events = Record(session);

        host.Sessions[0].SimulateDisconnect("The remote computer restarted.");

        SessionStateChangedEventArgs dropped = Assert.Single(events);
        Assert.Equal(SessionState.Connected, dropped.PreviousState);
        Assert.Equal(SessionState.Disconnected, dropped.State);
        Assert.Equal("The remote computer restarted.", dropped.Message);
    }

    [Fact]
    public async Task A_session_can_be_connected_again_after_it_drops()
    {
        (FakeRemoteSessionHost host, IRemoteSession session) = NewSession();
        await session.ConnectAsync();
        host.Sessions[0].SimulateDisconnect();

        await session.ConnectAsync();

        Assert.Equal(SessionState.Connected, session.State);
        Assert.Equal(2, host.Sessions[0].ConnectAttempts);
    }

    [Fact]
    public async Task Connecting_a_live_session_is_a_mistake_not_a_no_op()
    {
        (_, IRemoteSession session) = NewSession();
        await session.ConnectAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => session.ConnectAsync());
    }

    [Fact]
    public async Task Disposing_a_live_session_ends_it_first()
    {
        (_, IRemoteSession session) = NewSession();
        await session.ConnectAsync();
        List<SessionStateChangedEventArgs> events = Record(session);

        session.Dispose();

        Assert.Equal(SessionState.Disconnected, Assert.Single(events).State);
        Assert.Equal(SessionState.Disconnected, session.State);
    }

    [Fact]
    public async Task A_disposed_session_cannot_be_reused()
    {
        (_, IRemoteSession session) = NewSession();
        session.Dispose();
        session.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => session.ConnectAsync());
    }

    [Fact]
    public void Every_session_gets_its_own_identity()
    {
        FakeRemoteSessionHost host = new();

        IRemoteSession first = host.CreateSession(Request);
        IRemoteSession second = host.CreateSession(Request);

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(2, host.Sessions.Count);
        Assert.Same(Request, first.Request);
    }

    // ── Readings (M5-17) ────────────────────────────────────────────────

    [Fact]
    public void A_session_that_has_not_connected_knows_nothing_about_itself()
    {
        (_, IRemoteSession session) = NewSession();

        Assert.True(session.Vitals.IsUnknown);
    }

    [Fact]
    public async Task Connecting_produces_readings_and_announces_them()
    {
        (_, IRemoteSession session) = NewSession(h =>
        {
            h.SimulatedSecurity = SessionSecurity.NetworkLevel;
            h.SimulatedLatency = TimeSpan.FromMilliseconds(18);
        });

        List<SessionVitals> reported = [];
        session.VitalsChanged += (_, e) => reported.Add(e.Vitals);

        await session.ConnectAsync();

        Assert.Equal(new PixelSize(1920, 1080), session.Vitals.Resolution);
        Assert.Equal(SessionSecurity.NetworkLevel, session.Vitals.Security);
        Assert.Equal(TimeSpan.FromMilliseconds(18), session.Vitals.Latency);
        Assert.Equal(session.Vitals, Assert.Single(reported));
    }

    [Fact]
    public async Task Readings_arrive_before_the_state_that_makes_them_true()
    {
        // Otherwise a handler drawing the status bar on Connected draws it from
        // the readings of a session that had not connected yet.
        (_, IRemoteSession session) = NewSession();

        List<string> order = [];
        session.VitalsChanged += (_, _) => order.Add("vitals");
        session.StateChanged += (_, e) => order.Add($"state:{e.State}");

        await session.ConnectAsync();

        Assert.Equal(["state:Connecting", "vitals", "state:Connected"], order);
    }

    [Fact]
    public async Task A_session_that_ends_forgets_what_it_knew()
    {
        // A status bar still reporting 1920 x 1080 and 18 ms about a connection
        // that ended two minutes ago is not stale, it is wrong.
        (_, IRemoteSession session) = NewSession(h => h.SimulatedLatency = TimeSpan.FromMilliseconds(18));

        await session.ConnectAsync();
        await session.DisconnectAsync();

        Assert.True(session.Vitals.IsUnknown);
    }

    [Fact]
    public async Task A_session_that_drops_forgets_too()
    {
        (FakeRemoteSessionHost host, IRemoteSession session) = NewSession();

        await session.ConnectAsync();
        host.Sessions[0].SimulateDisconnect();

        Assert.True(session.Vitals.IsUnknown);
    }

    [Fact]
    public async Task A_failed_connect_leaves_nothing_behind_either()
    {
        (_, IRemoteSession session) = NewSession(h => h.FailConnections("Refused."));

        await Assert.ThrowsAsync<RemoteSessionException>(() => session.ConnectAsync());

        Assert.True(session.Vitals.IsUnknown);
    }

    [Fact]
    public async Task A_round_trip_can_be_reported_without_anything_else_changing()
    {
        (FakeRemoteSessionHost host, IRemoteSession session) = NewSession();

        await session.ConnectAsync();

        List<SessionStateChangedEventArgs> states = Record(session);
        host.Sessions[0].SimulateLatency(TimeSpan.FromMilliseconds(210));

        Assert.Equal(TimeSpan.FromMilliseconds(210), session.Vitals.Latency);
        Assert.Equal(new PixelSize(1920, 1080), session.Vitals.Resolution);
        Assert.Empty(states);
    }

    [Fact]
    public void A_session_with_no_connection_has_no_round_trip_to_report()
    {
        FakeRemoteSessionHost host = new();
        using FakeRemoteSession session = (FakeRemoteSession)host.CreateSession(Request);

        Assert.Throws<InvalidOperationException>(
            () => session.SimulateLatency(TimeSpan.FromMilliseconds(20)));
    }

    [Fact]
    public async Task A_gateway_is_only_claimed_when_the_connection_always_uses_one()
    {
        // Set to fall back to a gateway, whether one was used is not something
        // a session that connected to nothing is in a position to say.
        FakeRemoteSessionHost host = new();
        ConnectionSettings settings = ConnectionSettings.Defaults;
        settings.GatewayHostName = "gw.example.com";
        settings.GatewayUsage = GatewayUsage.WhenDirectFails;

        using IRemoteSession session = host.CreateSession(new SessionRequest
        {
            HostName = "web-01",
            Settings = settings,
        });

        await session.ConnectAsync();

        Assert.Null(session.Vitals.GatewayHostName);
    }

    [Fact]
    public async Task A_gateway_that_is_always_used_is_reported()
    {
        FakeRemoteSessionHost host = new();
        ConnectionSettings settings = ConnectionSettings.Defaults;
        settings.GatewayHostName = "gw.example.com";
        settings.GatewayUsage = GatewayUsage.Always;

        using IRemoteSession session = host.CreateSession(new SessionRequest
        {
            HostName = "web-01",
            Settings = settings,
        });

        await session.ConnectAsync();

        Assert.Equal("gw.example.com", session.Vitals.GatewayHostName);
    }

    [Fact]
    public void A_fake_does_not_flatter_itself_about_security()
    {
        // Reporting the configuration everyone wants would hide the one field
        // whose job is to notice when something weaker was agreed to.
        Assert.Equal(SessionSecurity.Tls, new FakeRemoteSessionHost().SimulatedSecurity);
        Assert.Null(new FakeRemoteSessionHost().SimulatedLatency);
    }

    // ── A refused sign-in (M4-08) ───────────────────────────────────────

    private static async Task<FakeRemoteSession> Connected()
    {
        (_, IRemoteSession session) = NewSession();
        await session.ConnectAsync();

        return (FakeRemoteSession)session;
    }

    [Fact]
    public async Task A_fresh_session_has_never_been_refused()
    {
        FakeRemoteSession session = await Connected();

        Assert.Null(session.LastLogonError);
    }

    [Fact]
    public async Task A_refusal_fails_the_session_and_records_the_code()
    {
        // The one ending that must never be retried automatically. There is no
        // other way to rehearse it without a server that will refuse a password
        // on demand.
        FakeRemoteSession session = await Connected();

        session.SimulateRefusal();

        Assert.Equal(SessionState.Failed, session.State);
        Assert.Equal(SessionReasons.LogonBadCredentials, session.LastLogonError);
    }

    [Fact]
    public async Task A_refused_session_reads_as_a_refusal()
    {
        FakeRemoteSession session = await Connected();
        session.SimulateRefusal();

        SessionEnding ending = new()
        {
            From = SessionState.Connected,
            To = session.State,
            LogonError = session.LastLogonError,
        };

        Assert.True(ending.IsRefusal);
        Assert.Equal(
            ReconnectVerdict.Refused,
            ReconnectRules.Decide(ReconnectPolicy.Default, ending, 0).Verdict);
    }

    [Fact]
    public async Task Connecting_again_forgets_the_last_refusal()
    {
        // Exactly as SessionSignalRouter forgets on OnConnecting: a fresh
        // attempt carries none of the last one's baggage, or somebody who fixed
        // their password is told it is still wrong.
        FakeRemoteSession session = await Connected();
        session.SimulateRefusal();

        await session.ConnectAsync();

        Assert.Null(session.LastLogonError);
    }

    [Fact]
    public void A_session_that_never_connected_cannot_be_refused()
    {
        (_, IRemoteSession session) = NewSession();

        Assert.Throws<InvalidOperationException>(
            () => ((FakeRemoteSession)session).SimulateRefusal());
    }

    // ── A sign-in the far end will not take (M3-06) ──

    [Fact]
    public async Task A_refused_sign_in_can_leave_the_session_up()
    {
        // What the real control does: a logon error ends nothing, which is the
        // only reason a docked prompt is possible at all.
        FakeRemoteSession session = await Connected();

        session.SimulateLogonPrompt(StatusLogonFailure);

        Assert.Equal(SessionState.Connected, session.State);
        Assert.True(session.IsAwaitingCredentials);
        Assert.Equal(StatusLogonFailure, session.LastLogonError);
    }

    [Fact]
    public async Task An_account_that_will_not_open_is_not_worth_asking_about()
    {
        FakeRemoteSession session = await Connected();

        session.SimulateLogonPrompt(AccountLockedOut);

        Assert.Equal(SessionState.Connected, session.State);
        Assert.False(session.IsAwaitingCredentials);
    }

    [Fact]
    public async Task Connecting_again_stops_the_asking()
    {
        FakeRemoteSession session = await Connected();
        session.SimulateLogonPrompt(StatusLogonFailure);

        await session.DisconnectAsync();
        await session.ConnectAsync();

        Assert.False(session.IsAwaitingCredentials);
    }

    [Fact]
    public async Task A_new_sign_in_is_carried_by_the_next_attempt()
    {
        // The tab survives and the session does not, which is the whole shape
        // of M4-10's other half.
        FakeRemoteSession session = await Connected();
        session.SimulateLogonPrompt(StatusLogonFailure);

        await session.DisconnectAsync();
        session.UseCredentials(new SessionCredentials { UserName = "svc-other", Password = Secret.From("x") });
        await session.ConnectAsync();

        Assert.Equal("svc-other", session.Request.Credentials.UserName);
        Assert.Equal(SessionState.Connected, session.State);
    }

    [Fact]
    public async Task The_sign_in_cannot_be_changed_underneath_an_attempt()
    {
        // Applied to neither attempt reliably, so it is refused rather than
        // raced. The delay is what makes the window observable at all.
        (_, IRemoteSession session) = NewSession(
            h => h.ConnectDelay = TimeSpan.FromMilliseconds(200));

        Task connecting = session.ConnectAsync();

        Assert.Equal(SessionState.Connecting, session.State);
        Assert.Throws<InvalidOperationException>(
            () => session.UseCredentials(SessionCredentials.None));

        await connecting;
    }

    [Fact]
    public async Task A_session_showing_a_logon_screen_is_still_connected()
    {
        // So the panel cannot offer Connect: there is nothing to connect.
        // Answering it disconnects first, which is what the shell does.
        FakeRemoteSession session = await Connected();
        session.SimulateLogonPrompt(StatusLogonFailure);

        await Assert.ThrowsAsync<InvalidOperationException>(() => session.ConnectAsync());
    }

    private const int StatusLogonFailure = -1073741715;
    private const int AccountLockedOut = -1073741260;
}
