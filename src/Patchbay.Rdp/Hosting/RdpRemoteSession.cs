using System.Windows.Forms;
using Patchbay.Core.Sessions;
using Patchbay.Rdp.Interop;

namespace Patchbay.Rdp.Hosting;

/// <summary>
/// Everything the shell needs in order to <em>draw</em> a session: a window,
/// and the sizing choice that decides what goes in it (M5-09).
///
/// <para>
/// Declared here rather than in <c>Core</c> because the thing being handed over
/// is a WinForms control and <c>Core</c> is not allowed to know what one is.
/// The shell references both projects, so it is the one place the two can meet
/// — and a session that does not implement this is not a broken session, it is
/// the fake, which has no window and never will.
/// </para>
/// </summary>
public interface IHostedSessionView
{
    /// <summary>The window to put on screen. Owned by the session, not by whoever shows it.</summary>
    Control View { get; }

    /// <summary>
    /// Whether the picture is scaled to fit rather than shown pixel for pixel
    /// and scrolled. Belongs to the tab, not to the document (M5-09).
    /// </summary>
    bool SmartSizing { get; set; }

    /// <summary>Where the picture ended up and what it was scaled by.</summary>
    SessionPlacement Placement { get; }

    /// <summary>Raised when <see cref="Placement"/> changes, for the status bar (M5-17).</summary>
    event EventHandler? PlacementChanged;
}

/// <summary>
/// A real session: the RDP control, driven.
///
/// This is the piece the whole of M4 has been building towards, and almost all
/// of it is assembly rather than invention. The settings come from
/// <see cref="RdpSettingsMapper"/> (M4-04), the announcements are read by
/// <see cref="SessionSignalRouter"/> (M4-06), what may follow what is
/// <see cref="SessionStateMachine"/>'s (M4-05), and the words for a failure are
/// <see cref="SessionReasons"/>' (M4-07) — all of which live in <c>Core</c> and
/// are tested there. What is left here is the three things only this layer can
/// do: own a window, call two COM methods, and turn an event-driven control
/// into something a caller can await.
///
/// <para>
/// <b>A session cannot connect until something has given it a window.</b> The
/// control is an ActiveX control; it has no COM object until its handle exists,
/// and no handle until it is in a window that is on screen. That is not a
/// detail that can be hidden — but it can be waited for, and
/// <see cref="ConnectAsync"/> does, because the shell adds a tab and connects
/// it in one go while WPF realises the tab a moment later. Waiting yields the
/// dispatcher, which is exactly what lets the realisation happen. Waiting
/// forever would not, so it gives up and says why.
/// </para>
///
/// <para>
/// <b>Threading.</b> One thread, as the interface promises: the control belongs
/// to the thread that made it and raises its events there. Nothing here hops
/// threads, and nothing here needs to.
/// </para>
/// </summary>
public sealed class RdpRemoteSession : IRemoteSession, IHostedSessionView
{
    /// <summary>
    /// How long to wait for something to put this session on screen. Long
    /// enough that a slow first layout is not mistaken for a bug, short enough
    /// that a session nobody ever showed does not sit there indefinitely
    /// looking like it is connecting.
    /// </summary>
    private static readonly TimeSpan WindowTimeout = TimeSpan.FromSeconds(10);

    private readonly SessionStateMachine _lifecycle = new();
    private readonly RdpSessionControl _control;
    private readonly RdpSessionPane _pane;
    private readonly SessionSignalRouter _router;

    private TaskCompletionSource<bool>? _connecting;
    private CancellationTokenRegistration _cancellation;
    private SessionVitals _vitals;
    private bool _disposed;

    internal RdpRemoteSession(SessionRequest request, RdpEngineInfo engine)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(engine);

        Request = request;

        _control = new RdpSessionControl(engine) { Dock = DockStyle.Fill };
        _pane = new RdpSessionPane(_control)
        {
            Dock = DockStyle.Fill,

            // The resolution being asked for, so the pane can letterbox from
            // the first frame rather than after the first resize. Replaced
            // with what the far end agreed to the moment it connects.
            SessionSize = new PixelSize(
                request.Settings.DesktopWidth ?? 0,
                request.Settings.DesktopHeight ?? 0),
            SmartSizing = request.Settings.UseSmartSizing ?? true,
        };

        _router = new SessionSignalRouter(_lifecycle, request.Endpoint, _control.CreateReasons().Describer);

        _lifecycle.Changed += OnLifecycleChanged;
        _control.SignalReceived += OnSignalReceived;
    }

    public event EventHandler<SessionStateChangedEventArgs>? StateChanged;

    public event EventHandler<SessionVitalsChangedEventArgs>? VitalsChanged;

    public Guid Id { get; } = Guid.NewGuid();

    public SessionRequest Request { get; }

    public SessionState State => _lifecycle.State;

    public string? StatusMessage => _lifecycle.StatusMessage;

    public SessionVitals Vitals => _vitals;

    /// <inheritdoc />
    public int? LastLogonError => _router.LastLogonError;

    /// <inheritdoc />
    public Control View => _pane;

    /// <inheritdoc />
    public bool SmartSizing
    {
        get => _pane.SmartSizing;
        set => _pane.SmartSizing = value;
    }

    /// <inheritdoc />
    public SessionPlacement Placement => _pane.Placement;

    /// <inheritdoc />
    public event EventHandler? PlacementChanged
    {
        // Straight through to the pane, which is what actually works the
        // placement out. Relaying it through a field here would be a second
        // copy of the same subscription list for no gain.
        add => _pane.PlacementChanged += value;
        remove => _pane.PlacementChanged -= value;
    }

    /// <summary>
    /// What the settings mapper made of this connection (M4-04), or null until
    /// the first connect. Worth showing when it has concerns: they are the
    /// settings this control would not take.
    /// </summary>
    public RdpSettingsReport? Settings { get; private set; }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_lifecycle.CanConnect)
        {
            throw new InvalidOperationException(
                $"Cannot connect a session that is {State}. Disconnect it first.");
        }

        // Before the state moves, because a session that never got a window
        // has not begun connecting and should not spend ten seconds claiming
        // to be.
        await WaitForWindow(cancellationToken).ConfigureAwait(true);

        TaskCompletionSource<bool> attempt = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _connecting = attempt;
        _cancellation = cancellationToken.Register(CancelAttempt);

        try
        {
            _lifecycle.MoveTo(SessionState.Connecting, $"Connecting to {Request.Endpoint}…");

            RdpClientInstance client = _control.EnsureCreated();

            // Settings before Connect, always. The control reads most of them
            // once, as the connection is made, and applying them afterwards
            // succeeds and does nothing at all.
            Settings = _control.ApplySettings(Request);

            client.Connect();

            await attempt.Task.ConfigureAwait(true);
        }
        catch (RdpEngineException ex)
        {
            // The control would not start. Distinct from a connection that was
            // attempted and failed, and the state has to be moved by hand
            // because no event is coming.
            _lifecycle.TryMoveTo(SessionState.Failed, ex.Message, ex);
            throw new RemoteSessionException(ex.Message, ex);
        }
        finally
        {
            _cancellation.Dispose();
            _cancellation = default;
            _connecting = null;
        }
    }

    public async Task DisconnectAsync()
    {
        if (_disposed || !_lifecycle.CanDisconnect)
        {
            return;
        }

        // Moved first, so that the disconnect the control is about to announce
        // is read as one Patchbay asked for rather than as the far end
        // hanging up — the router keys on exactly this.
        _lifecycle.MoveTo(SessionState.Disconnecting, "Disconnecting…");

        try
        {
            _control.Client?.Disconnect();
        }
        catch (RdpEngineException)
        {
            // Already down, or too broken to be told. Either way there is
            // nothing left to end, and the wait below would never finish.
            _lifecycle.TryMoveTo(SessionState.Disconnected, "Disconnected.");
            return;
        }

        await WaitFor(SessionState.Disconnected, SessionState.Failed).ConfigureAwait(true);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        // Before the flag, so a tab closing on a live session still hears that
        // the session ended.
        try
        {
            if (_lifecycle.CanDisconnect)
            {
                _control.Client?.Disconnect();
            }
        }
        catch (RdpEngineException)
        {
            // Going away regardless.
        }

        _lifecycle.TryMoveTo(SessionState.Disconnected, "Disconnected.");

        _disposed = true;

        _control.SignalReceived -= OnSignalReceived;
        _lifecycle.Changed -= OnLifecycleChanged;

        _cancellation.Dispose();
        _connecting?.TrySetCanceled();
        _connecting = null;

        // The pane owns the control and disposing it takes the window with it,
        // which is the point: a closed tab should stop costing a socket and a
        // decoder now rather than at the next collection.
        _pane.Dispose();

        StateChanged = null;
        VitalsChanged = null;
    }

    private void OnSignalReceived(object? sender, SessionSignalEventArgs e)
    {
        _router.Report(e.Signal, e.Code, e.Reconnect);

        if (e.Signal is SessionSignal.IdleTimedOut)
        {
            EndIdleSession();
        }
    }

    /// <summary>
    /// Closes a session the control has reported as idle (M4-15).
    ///
    /// <para>
    /// The control raises the notification and then does nothing, which is the
    /// part of this setting that is easy to get wrong: a host that only listens
    /// leaves a session somebody asked to be closed sitting open, and a host
    /// that only listens <em>and</em> reports produces a message about a
    /// disconnect over the top of a live desktop.
    /// </para>
    ///
    /// <para>
    /// It goes out through the ordinary disconnect rather than by moving the
    /// state directly, which is what makes it an ending nobody chases: the
    /// session passes through <c>Disconnecting</c>, and M4-08 reads that as a
    /// disconnect somebody asked for. An idle timeout that immediately
    /// reconnected would be a timeout in name only.
    /// </para>
    /// </summary>
    private void EndIdleSession()
    {
        // A session the control calls idle is a session the control is drawing,
        // so the window is there — but posting to a window that is not is an
        // exception rather than a no-op, and this runs on a native stack where
        // an exception has nowhere sensible to go.
        if (_disposed || !_pane.IsHandleCreated)
        {
            return;
        }

        // Fire and forget on the dispatcher's own thread: this runs inside the
        // control's call frame, and calling Disconnect from there asks the
        // control to tear itself down while it is still on the stack.
        _ = _pane.BeginInvoke(new Action(() =>
        {
            if (!_disposed)
            {
                _ = DisconnectAsync();
            }
        }));
    }

    private void OnLifecycleChanged(object? sender, SessionStateChangedEventArgs e)
    {
        // Readings belong to a live connection and to nothing else, so every
        // transition away from Connected clears them (M5-17).
        SetVitals(e.State is SessionState.Connected ? Measured() : SessionVitals.Unknown);

        StateChanged?.Invoke(this, e);

        if (_connecting is not { } attempt)
        {
            return;
        }

        switch (e.State)
        {
            case SessionState.Connected:
                attempt.TrySetResult(true);
                break;

            case SessionState.Failed:
                attempt.TrySetException(
                    e.Error as RemoteSessionException
                    ?? new RemoteSessionException(e.Message ?? "The connection failed."));
                break;

            case SessionState.Disconnected:
                // Called off, or ended before it was ever up. Neither is a
                // failure, and reporting one would offer a retry to somebody
                // who just changed their mind.
                attempt.TrySetCanceled();
                break;

            default:
                break;
        }
    }

    /// <summary>
    /// What the control can say about the live session (M5-17). Two real
    /// measurements now — the resolution the far end agreed to, and how it
    /// proved itself (M4-09) — and the round trip still to come with
    /// <c>M5-18</c>. Nothing here is filled in from the request, which is the
    /// one thing the status bar must never be told: a session configured for
    /// network level authentication that came up without it connected anyway,
    /// and reporting the setting back would hide exactly that.
    /// </summary>
    private SessionVitals Measured() => new()
    {
        Resolution = _control.DesktopSize,
        Security = _control.NegotiatedSecurity,
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

    private void CancelAttempt()
    {
        // Through the control, so that the cancellation arrives as an ordinary
        // disconnect and every listener sees the same ending. The attempt task
        // is completed by that event, not here.
        try
        {
            _control.Client?.Disconnect();
        }
        catch (RdpEngineException)
        {
            _lifecycle.TryMoveTo(SessionState.Disconnected, "Connection cancelled.");
        }
    }

    /// <summary>
    /// Waits until the pane has a window, because the COM object does not
    /// exist before that and neither does anything to connect with.
    /// </summary>
    private async Task WaitForWindow(CancellationToken cancellationToken)
    {
        if (_pane.IsHandleCreated)
        {
            return;
        }

        TaskCompletionSource<bool> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnHandleCreated(object? sender, EventArgs e) => ready.TrySetResult(true);

        _pane.HandleCreated += OnHandleCreated;

        try
        {
            // Checked again now that the handler is attached: the handle may
            // have arrived in the moment between the test above and this line,
            // and nothing would ever raise the event again.
            if (_pane.IsHandleCreated)
            {
                return;
            }

            using CancellationTokenSource timeout = new(WindowTimeout);
            using CancellationTokenSource linked =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

            using (linked.Token.Register(() => ready.TrySetCanceled()))
            {
                try
                {
                    await ready.Task.ConfigureAwait(true);
                }
                catch (OperationCanceledException) when (timeout.IsCancellationRequested)
                {
                    throw new RemoteSessionException(
                        "This session never appeared on screen, so there was nothing to connect with. "
                        + "That usually means the tab was closed before it opened.");
                }
            }
        }
        finally
        {
            _pane.HandleCreated -= OnHandleCreated;
        }
    }

    /// <summary>Waits for the session to reach one of <paramref name="states"/>.</summary>
    private async Task WaitFor(params SessionState[] states)
    {
        if (states.Contains(_lifecycle.State))
        {
            return;
        }

        TaskCompletionSource<bool> reached = new(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnChanged(object? sender, SessionStateChangedEventArgs e)
        {
            if (states.Contains(e.State))
            {
                reached.TrySetResult(true);
            }
        }

        _lifecycle.Changed += OnChanged;

        try
        {
            if (states.Contains(_lifecycle.State))
            {
                return;
            }

            using CancellationTokenSource timeout = new(WindowTimeout);
            using (timeout.Token.Register(() => reached.TrySetResult(false)))
            {
                // A control that will not say it has disconnected is not worth
                // waiting on forever; the tab is closing either way.
                await reached.Task.ConfigureAwait(true);
            }
        }
        finally
        {
            _lifecycle.Changed -= OnChanged;
        }
    }
}
