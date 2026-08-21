using Patchbay.Core.Model;

namespace Patchbay.Core.Sessions;

/// <summary>
/// A session that goes through every motion of connecting without a server at
/// the other end. Made by <see cref="FakeRemoteSessionHost"/>.
///
/// It is written to be strict rather than forgiving: connecting twice throws,
/// using a disposed session throws, and a simulated drop is only allowed on a
/// live session. A fake that shrugs at misuse teaches the UI habits the real
/// implementation will not tolerate.
///
/// What is and is not a legal move is not decided here — that belongs to
/// <see cref="SessionStateMachine"/> (M4-05), which the real implementation
/// shares, so the two cannot come to different conclusions about the same
/// sequence of events.
/// </summary>
public sealed class FakeRemoteSession : IRemoteSession
{
    private readonly FakeRemoteSessionHost _host;
    private readonly SessionStateMachine _lifecycle = new();

    private CancellationTokenSource? _connecting;
    private SessionVitals _vitals;
    private bool _disposed;

    internal FakeRemoteSession(SessionRequest request, FakeRemoteSessionHost host)
    {
        Request = request;
        _host = host;

        _lifecycle.Changed += OnLifecycleChanged;
    }

    public event EventHandler<SessionStateChangedEventArgs>? StateChanged;

    public event EventHandler<SessionVitalsChangedEventArgs>? VitalsChanged;

    public Guid Id { get; } = Guid.NewGuid();

    public SessionRequest Request { get; private set; }

    public SessionState State => _lifecycle.State;

    public string? StatusMessage => _lifecycle.StatusMessage;

    /// <summary>
    /// Invented readings, from whatever the host is configured to pretend
    /// (M5-17). They are as fictional as the session, which is why the shell
    /// keeps its simulated-host warning on screen beside them.
    /// </summary>
    public SessionVitals Vitals => _vitals;

    /// <inheritdoc />
    public int? LastLogonError { get; private set; }

    /// <inheritdoc />
    public bool IsAwaitingCredentials { get; private set; }

    /// <inheritdoc />
    public void UseCredentials(SessionCredentials credentials)
    {
        ArgumentNullException.ThrowIfNull(credentials);

        if (State is SessionState.Connecting or SessionState.Disconnecting)
        {
            throw new InvalidOperationException(
                $"The sign-in cannot be changed while a session is {State}.");
        }

        Request = Request with { Credentials = credentials };
    }

    /// <summary>How many times this session has been connected, successfully or not.</summary>
    public int ConnectAttempts { get; private set; }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_lifecycle.CanConnect)
        {
            throw new InvalidOperationException(
                $"Cannot connect a session that is {State}. Disconnect it first.");
        }

        ConnectAttempts++;

        // A fresh attempt carries none of the last one's baggage, exactly as
        // SessionSignalRouter forgets on OnConnecting.
        LastLogonError = null;
        IsAwaitingCredentials = false;

        // Linked so that closing a tab mid-connect abandons the attempt, which
        // is the case the UI is most likely to get wrong.
        using CancellationTokenSource linked =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _connecting = linked;

        try
        {
            _lifecycle.MoveTo(SessionState.Connecting, $"Connecting to {Request.Endpoint}…");

            try
            {
                await Task.Delay(_host.ConnectDelay, linked.Token);
            }
            catch (OperationCanceledException)
            {
                // Try rather than insist: a dispose that cancelled this attempt
                // has already ended the session, and there is nothing left to
                // report.
                _lifecycle.TryMoveTo(SessionState.Disconnected, "Connection cancelled.");
                throw;
            }

            if (_host.ConnectFailure?.Invoke(Request) is { } failure)
            {
                RemoteSessionException error = new(failure);
                _lifecycle.MoveTo(SessionState.Failed, failure, error);
                throw error;
            }

            if (!_lifecycle.TryMoveTo(SessionState.Connected, $"Connected to {Request.Endpoint}."))
            {
                throw new OperationCanceledException(
                    "The session was ended while it was still connecting.");
            }
        }
        finally
        {
            _connecting = null;
        }
    }

    public async Task DisconnectAsync()
    {
        if (_disposed || !_lifecycle.CanDisconnect)
        {
            return;
        }

        // Cancels a connect still in flight; that path reports itself.
        if (State is SessionState.Connecting)
        {
            await CancelConnect();
            return;
        }

        _lifecycle.MoveTo(SessionState.Disconnecting, "Disconnecting…");
        await Task.Delay(_host.DisconnectDelay);
        _lifecycle.MoveTo(SessionState.Disconnected, "Disconnected.");
    }

    /// <summary>
    /// Drops a live session as though the far end had gone away. There is no
    /// other way to rehearse the case, and it is the one the interface most
    /// needs to handle gracefully.
    /// </summary>
    /// <param name="message">What to show. Defaults to a plain statement of fact.</param>
    public void SimulateDisconnect(string? message = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (State is not SessionState.Connected)
        {
            throw new InvalidOperationException(
                $"Only a connected session can be dropped, and this one is {State}.");
        }

        _lifecycle.MoveTo(SessionState.Disconnected, message ?? "The remote computer ended the session.");
    }

    /// <summary>
    /// Reports a round trip, as the probe will once M5-18 is measuring one.
    /// There is nothing to measure here, so this is the only way to see what
    /// the status bar does with a slow link.
    /// </summary>
    public void SimulateLatency(TimeSpan latency)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (State is not SessionState.Connected)
        {
            throw new InvalidOperationException(
                $"Only a connected session has a round trip, and this one is {State}.");
        }

        SetVitals(_vitals with { Latency = latency });
    }

    /// <summary>
    /// Fails a session as though the far end had refused the sign-in (M4-08).
    /// There is no other way to rehearse the one ending that must never be
    /// retried automatically, and it is the case where getting it wrong locks
    /// somebody's account.
    /// </summary>
    /// <param name="logonError">
    /// Defaults to <c>STATUS_LOGON_FAILURE</c>, which is what a wrong password
    /// actually reports.
    /// </param>
    public void SimulateRefusal(int logonError = SessionReasons.LogonBadCredentials)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (State is not (SessionState.Connecting or SessionState.Connected))
        {
            throw new InvalidOperationException(
                $"Only a live session can be refused, and this one is {State}.");
        }

        LastLogonError = logonError;
        IsAwaitingCredentials = false;

        const string Message = "The user name or password is incorrect.";
        _lifecycle.MoveTo(SessionState.Failed, Message, new RemoteSessionException(Message));
    }

    /// <summary>
    /// Refuses a sign-in the way the real control does: the session stays up,
    /// nothing transitions, and a prompt becomes due if the code is one a
    /// different password could fix (M3-06, M4-10).
    ///
    /// Distinct from <see cref="SimulateLogonFailure"/>, which ends the
    /// session. Both happen in real life — the far end can refuse and keep
    /// the connection, or refuse and drop it — and the docked prompt only
    /// makes sense for the first.
    /// </summary>
    public void SimulateLogonPrompt(int logonError)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (State is not SessionState.Connected)
        {
            throw new InvalidOperationException(
                $"Only a connected session can show a logon screen, and this one is {State}.");
        }

        LastLogonError = logonError;
        IsAwaitingCredentials = LogonFailure.IsWorthAskingAgain(logonError);
    }

    /// <summary>
    /// Fails a live session as though the connection had broken.
    /// </summary>
    public void SimulateFailure(string message)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        if (State is not (SessionState.Connecting or SessionState.Connected))
        {
            throw new InvalidOperationException(
                $"Only a live session can fail, and this one is {State}.");
        }

        _lifecycle.MoveTo(SessionState.Failed, message, new RemoteSessionException(message));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _connecting?.Cancel();

        // The last transition is raised before the flag goes up, so a tab
        // closing on a live session still gets told the session ended. Try
        // rather than insist: from Idle, Disconnected or Failed there is
        // nothing to end and nothing to announce.
        _lifecycle.TryMoveTo(SessionState.Disconnected, "Disconnected.");

        _disposed = true;
        _lifecycle.Changed -= OnLifecycleChanged;
        StateChanged = null;
        VitalsChanged = null;
    }

    private void OnLifecycleChanged(object? sender, SessionStateChangedEventArgs e)
    {
        // Readings belong to a live connection and to nothing else. Hanging
        // them off the transition rather than off the connect path means a
        // drop, a failure and a dispose all clear them without any of the three
        // having to remember to — and a status bar still reporting 24 ms about
        // a session that ended is worse than one reporting nothing.
        //
        // Before the state change, so that a handler reacting to Connected
        // already has the readings to draw.
        SetVitals(e.State is SessionState.Connected ? Invented() : SessionVitals.Unknown);

        StateChanged?.Invoke(this, e);
    }

    /// <summary>
    /// What a session that connected to nothing claims about itself: the
    /// resolution it asked for, whatever security layer the host is set to
    /// pretend, and the gateway only when it was told to always use one.
    /// </summary>
    private SessionVitals Invented() => new()
    {
        Resolution = new PixelSize(
            Request.Settings.DesktopWidth ?? 0,
            Request.Settings.DesktopHeight ?? 0),
        Security = _host.SimulatedSecurity,
        GatewayHostName = Request.Settings.GatewayUsage is GatewayUsage.Always
            ? Request.Settings.GatewayHostName
            : null,
        Latency = _host.SimulatedLatency,
    };

    private void SetVitals(SessionVitals vitals)
    {
        if (_vitals == vitals)
        {
            return;
        }

        _vitals = vitals;
        VitalsChanged?.Invoke(this, new SessionVitalsChangedEventArgs { Vitals = vitals });
    }

    private async Task CancelConnect()
    {
        _connecting?.Cancel();

        // Let the cancelled connect run its own transition rather than racing
        // it to one, so callers see a single Disconnected either way.
        await Task.Yield();
    }
}
