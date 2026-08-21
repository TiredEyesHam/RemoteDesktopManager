using System.Diagnostics;
using System.Globalization;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Patchbay.Core.Sessions;
using Patchbay.Rdp.Hosting;

namespace Patchbay.App.ViewModels;

/// <summary>
/// One tab (M5-01). A session, plus the handful of things a strip needs to
/// draw one: a name, a state, and a sentence about it.
///
/// The tab outlives the connection on purpose. A session that drops still has
/// a tab, still says why, and can be connected again from the same place — a
/// tab that disappeared when a server rebooted would take the reconnect with
/// it and leave someone wondering whether they had imagined it.
/// </summary>
public sealed partial class SessionTabViewModel : ObservableObject, IDisposable
{
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;

    /// <summary>Whether this tab is the one on screen. Set by the shell.</summary>
    [ObservableProperty]
    private bool _isActive;

    /// <summary>
    /// Whether the picture is scaled to fit this tab rather than scrolled
    /// (M5-09). Starts from the connection's <c>UseSmartSizing</c> and is the
    /// tab's own from then on — toggling it is a way of looking at a session,
    /// not a change to the connection, and writing it back to the document
    /// would edit a saved file because someone squinted at something.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SizingLabel))]
    [NotifyPropertyChangedFor(nameof(SizingSummary))]
    private bool _smartSizing;

    /// <summary>
    /// Where the picture ended up and what it was scaled by (M5-09), pushed in
    /// by whatever is drawing the session. Left at
    /// <see cref="SessionPlacement.Nowhere"/> there is no percentage to show,
    /// which is the honest answer for a tab that has never drawn anything.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusFields))]
    private SessionPlacement _placement = SessionPlacement.Nowhere;

    private readonly IHostedSessionView? _hosted;

    /// <summary>
    /// Time since this tab's countdown was last charged (M4-08). Per tab, not
    /// per window: a countdown that begins while another tab's is already
    /// running would otherwise be charged for the part of the interval that
    /// passed before it existed, and lose up to a whole tick of its first wait.
    /// </summary>
    private readonly Stopwatch _sinceTick = new();

    private bool _disposed;

    /// <summary>
    /// Raised when a reconnect has been scheduled and something needs to start
    /// ticking (M4-08). The tab keeps the arithmetic; the shell keeps the
    /// clock, because a countdown has to be redrawn on the thread that draws.
    /// </summary>
    public event EventHandler? ReconnectScheduled;

    /// <summary>
    /// Raised when the far end has refused a sign-in and is still holding the
    /// session open, so a panel is due (M3-06). Handled by the shell, which is
    /// what knows how to save a password and how to reconnect.
    /// </summary>
    public event EventHandler? CredentialsRequested;

    public SessionTabViewModel(IRemoteSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        Session = session;
        Session.StateChanged += OnStateChanged;
        Session.VitalsChanged += OnVitalsChanged;

        Reconnect = new ReconnectController(ReconnectPolicy.For(session.Request.Settings));

        _smartSizing = session.Request.Settings.UseSmartSizing ?? true;

        // A session that is drawn in a window of its own tells this tab where
        // its picture ended up. One that is not — the fake — leaves the
        // placement at Nowhere, which is the honest answer for a tab that has
        // never drawn anything.
        if (session is IHostedSessionView hosted)
        {
            _hosted = hosted;
            _smartSizing = hosted.SmartSizing;
            _placement = hosted.Placement;

            hosted.PlacementChanged += OnPlacementChanged;
        }
    }

    public IRemoteSession Session { get; }

    /// <summary>
    /// This tab's reconnect sequence (M4-08). One per tab, because the count
    /// and the countdown are per session, and because a tab is exactly what
    /// survives a session ending — which is what makes reconnecting into the
    /// same tab possible at all.
    /// </summary>
    public ReconnectController Reconnect { get; }

    /// <summary>What the tab is labelled — the node's name, not its address.</summary>
    public string Title => Session.Request.DisplayName;

    /// <summary>Host and port, for the tooltip and the overlay.</summary>
    public string Endpoint => Session.Request.Endpoint;

    public SessionState State => Session.State;

    /// <summary>The session's own words, or a plain statement of state.</summary>
    public string StatusMessage => Session.StatusMessage ?? StateLabel;

    public bool IsBusy => State is SessionState.Connecting or SessionState.Disconnecting;

    public bool IsLive => State is SessionState.Connected;

    public bool HasFailed => State is SessionState.Failed;

    /// <summary>
    /// The docked credential panel, or null when nothing is being asked
    /// (M3-06). Set by the shell, which is what knows how to save a password
    /// and how to reconnect.
    /// </summary>
    public CredentialPromptViewModel? Prompt
    {
        get;
        private set
        {
            field = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsPromptShowing));
        }
    }

    /// <summary>
    /// Whether the panel is docked. Independent of whether the session is
    /// showing: a refusal on a live session leaves the logon screen up behind
    /// the panel, and one that ended leaves the overlay up behind it.
    /// </summary>
    public bool IsPromptShowing => Prompt is not null;

    /// <summary>
    /// Whether the far end refused a sign-in and is still holding the session
    /// open, which is the moment to ask (M4-10).
    /// </summary>
    public bool IsAwaitingCredentials => Session.IsAwaitingCredentials;

    /// <summary>Docks a panel, replacing any that was already there.</summary>
    public void Ask(CredentialPrompt prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        Prompt = new CredentialPromptViewModel(prompt);
    }

    /// <summary>
    /// Takes the panel away, forgetting whatever was typed into it. Called
    /// when it is answered, when it is dismissed, and whenever the session
    /// moves on without it.
    /// </summary>
    public void StopAsking()
    {
        Prompt?.Forget();
        Prompt = null;
    }

    /// <summary>Whether connecting is worth offering. Retry and reconnect are the same button.</summary>
    public bool CanConnect =>
        State is SessionState.Idle or SessionState.Disconnected or SessionState.Failed;

    /// <summary>
    /// The resolution the session is running at, falling back to the one asked
    /// for while there is no session to ask. The far end is free to hand back
    /// something else entirely — a session-size policy does exactly that — so
    /// the negotiated figure wins wherever there is one.
    /// </summary>
    public PixelSize SessionSize => Session.Vitals.Resolution.IsEmpty
        ? new PixelSize(
            Session.Request.Settings.DesktopWidth ?? 0,
            Session.Request.Settings.DesktopHeight ?? 0)
        : Session.Vitals.Resolution;

    /// <summary>
    /// Host, resolution, security layer, gateway and latency (M5-17). Built in
    /// <c>Core</c>, where the rule about what is a fact and what is only a
    /// configured intention is written down once and tested.
    /// </summary>
    public IReadOnlyList<SessionStatusField> StatusFields =>
        SessionStatusLine.Build(Session.Request, State, Session.Vitals, Placement);

    /// <summary>Whether a reconnect is counting down or being attempted (M4-08).</summary>
    public bool IsReconnecting => Reconnect.IsRunning;

    /// <summary>
    /// The countdown, or what became of it. Shown under the failure rather than
    /// in place of it: why the session went is still the more useful half, and
    /// replacing it with "reconnecting in 12 s" loses it at exactly the moment
    /// somebody would want to read it.
    /// </summary>
    public string? ReconnectSummary => Reconnect.Summary;

    /// <summary>What the sizing button says. It names the state, not the action.</summary>
    public string SizingLabel => SmartSizing ? "Scaled to fit" : "Actual size";

    /// <summary>The resolution and what is being done with it, for the overlay.</summary>
    public string SizingSummary => SessionSize.IsEmpty
        ? SizingLabel
        : string.Create(
            CultureInfo.InvariantCulture,
            $"{SessionSize.Width} × {SessionSize.Height}, {(SmartSizing ? "scaled to fit" : "shown at actual size")}");

    public string StateLabel => State switch
    {
        SessionState.Idle => "Not connected",
        SessionState.Connecting => "Connecting…",
        SessionState.Connected => "Connected",
        SessionState.Disconnecting => "Disconnecting…",
        SessionState.Disconnected => "Disconnected",
        SessionState.Failed => "Could not connect",
        _ => string.Empty,
    };

    /// <summary>
    /// Connects, swallowing the failure. Everything worth saying about it has
    /// already arrived through <see cref="IRemoteSession.StateChanged"/> and is
    /// on screen; rethrowing here would only turn a refused password into a
    /// crash dialog.
    /// </summary>
    public async Task ConnectAsync()
    {
        try
        {
            await Session.ConnectAsync().ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is RemoteSessionException
            or OperationCanceledException
            or InvalidOperationException)
        {
            // Reported through the state, which the overlay is bound to.
        }
    }

    public async Task DisconnectAsync() => await Session.DisconnectAsync().ConfigureAwait(true);

    /// <summary>
    /// Moves the countdown on, and says whether it is time to connect (M4-08).
    ///
    /// Driven from outside because a visible countdown is redrawn on the
    /// thread that draws, so the clock belongs to the window. Elapsed time is
    /// measured rather than assumed to be the interval: a busy dispatcher, or
    /// a machine that has been asleep, delivers ticks late, and subtracting a
    /// fixed second per tick would drift.
    /// </summary>
    public bool Tick()
    {
        TimeSpan elapsed = _sinceTick.Elapsed;
        _sinceTick.Restart();

        bool due = Reconnect.Tick(elapsed);
        RefreshReconnect();

        return due;
    }

    /// <summary>
    /// Stops the countdown because somebody said so. The count is left where
    /// it was: cancelling a wait is not a claim that the session is well again.
    /// </summary>
    public void CancelReconnect()
    {
        Reconnect.Cancel();
        RefreshReconnect();
    }

    /// <summary>
    /// Forgets the sequence, for a connect a person asked for. Deliberately not
    /// done on every move to <see cref="SessionState.Connecting"/>: the
    /// automatic attempts go through there too, and resetting on those would
    /// make the attempt limit unreachable.
    /// </summary>
    public void ForgetReconnect()
    {
        Reconnect.Reset();
        RefreshReconnect();
    }

    public override string ToString() => $"{Title} ({StateLabel})";

    /// <summary>
    /// Stops listening. The session itself belongs to the workspace, which
    /// disposes it when the tab closes — doing it here as well would end a
    /// session twice and hide which of the two meant to.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Session.StateChanged -= OnStateChanged;
        Session.VitalsChanged -= OnVitalsChanged;

        if (_hosted is not null)
        {
            _hosted.PlacementChanged -= OnPlacementChanged;
        }
    }

    /// <summary>
    /// Pushes the toggle at the window that has to act on it (M5-09). The
    /// property is the tab's; the letterboxing is the pane's.
    /// </summary>
    partial void OnSmartSizingChanged(bool value)
    {
        if (_hosted is not null)
        {
            _hosted.SmartSizing = value;
        }
    }

    private void OnPlacementChanged(object? sender, EventArgs e)
    {
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.Invoke(RefreshPlacement);
            return;
        }

        RefreshPlacement();
    }

    private void RefreshPlacement()
    {
        if (_hosted is not null)
        {
            Placement = _hosted.Placement;
        }
    }

    private void OnVitalsChanged(object? sender, SessionVitalsChangedEventArgs e)
    {
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.Invoke(RefreshVitals);
            return;
        }

        RefreshVitals();
    }

    private void OnStateChanged(object? sender, SessionStateChangedEventArgs e)
    {
        // The interface promises these arrive on the thread that made the
        // session, and for both hosts they do. Checked rather than trusted:
        // a state change that lands on the wrong thread would throw somewhere
        // far away from the code that caused it.
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.Invoke(() => Refresh(e));
            return;
        }

        Refresh(e);
    }

    private void Refresh(SessionStateChangedEventArgs e)
    {
        // Before the notifications, so that anything reading IsReconnecting off
        // the back of a state change sees the answer for this transition rather
        // than the one before it.
        NoteReconnect(e);

        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(StatusMessage));
        OnPropertyChanged(nameof(StateLabel));
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(IsLive));
        OnPropertyChanged(nameof(HasFailed));
        OnPropertyChanged(nameof(CanConnect));
        OnPropertyChanged(nameof(IsAwaitingCredentials));

        // A panel outlives a transition only while the question still stands.
        // Connecting, connected and logged on all answer it; so does an
        // ending, because there is nothing left to sign in to.
        if (!Session.IsAwaitingCredentials)
        {
            StopAsking();
        }
        else if (Prompt is null)
        {
            CredentialsRequested?.Invoke(this, EventArgs.Empty);
        }

        // The state is one of the five fields, and the other four are cleared
        // by the same transitions that change it.
        RefreshVitals();
        RefreshReconnect();
    }

    /// <summary>
    /// Decides what this transition means for reconnecting (M4-08). The rule
    /// itself lives in <c>Core</c>; what is here is which fact goes in.
    /// </summary>
    private void NoteReconnect(SessionStateChangedEventArgs e)
    {
        // The logon error cannot come from the transition, because a transition
        // does not carry one, and it is the fact the whole safety of this rests
        // on. See SessionEnding.
        SessionEnding ending = SessionEnding.For(e, Session.LastLogonError);

        if (e.State is SessionState.Connected)
        {
            // Back up. Whatever sequence was running has done what it was for,
            // and the next drop deserves a fresh set of attempts rather than
            // the remains of the last one.
            Reconnect.Reset();
            return;
        }

        if (!ending.IsEnded)
        {
            return;
        }

        if (Reconnect.Ended(ending).ShouldRetry)
        {
            // From now, not from whenever the window last ticked.
            _sinceTick.Restart();

            ReconnectScheduled?.Invoke(this, EventArgs.Empty);
        }
    }

    private void RefreshReconnect()
    {
        OnPropertyChanged(nameof(IsReconnecting));
        OnPropertyChanged(nameof(ReconnectSummary));
    }

    private void RefreshVitals()
    {
        OnPropertyChanged(nameof(StatusFields));
        OnPropertyChanged(nameof(SessionSize));
        OnPropertyChanged(nameof(SizingSummary));
    }
}
