namespace Patchbay.Core.Sessions;

/// <summary>
/// One remote session. A tab owns exactly one of these for its lifetime.
///
/// There is nothing visual here, on purpose. The real implementation wraps an
/// ActiveX control that paints into an HWND, and Patchbay.Core must not know
/// what an HWND is — the architecture test enforces that. Getting the picture
/// on screen is the App's side of the seam, and lands with M4-03.
///
/// <para>
/// <b>Threading.</b> A session belongs to the thread that created it. The real
/// host wraps an STA COM object whose events arrive on the UI thread, so the
/// abstraction promises no more than that: create, connect and dispose from
/// one thread, and expect <see cref="StateChanged"/> back on it.
/// </para>
/// </summary>
public interface IRemoteSession : IDisposable
{
    /// <summary>Identifies this session, as distinct from the node it came from.</summary>
    Guid Id { get; }

    /// <summary>What this session was opened for. Fixed for its lifetime.</summary>
    SessionRequest Request { get; }

    SessionState State { get; }

    /// <summary>The most recent message, for the status bar. Null when there is nothing to say.</summary>
    string? StatusMessage { get; }

    /// <summary>
    /// What the engine can say about the live connection that the request
    /// cannot (M5-17): the resolution actually negotiated, the security layer
    /// actually agreed to, the gateway actually in use, and the round trip.
    /// <see cref="SessionVitals.Unknown"/> until there is a session, and again
    /// once there is not.
    /// </summary>
    SessionVitals Vitals { get; }

    /// <summary>
    /// The last logon error the far end reported, or null if it never reported
    /// one (M4-08).
    ///
    /// Here, rather than left inside the engine, because it is the one fact
    /// that decides whether trying again is safe: everything else about an
    /// ending is visible in the transition, and a refused sign-in is not.
    /// Retrying one submits the same credentials to the same account, and
    /// enough of that locks it out — a failure Patchbay would have caused
    /// rather than reported. See <see cref="SessionEnding.IsRefusal"/>.
    /// </summary>
    int? LastLogonError { get; }

    /// <summary>
    /// Raised on every transition, including ones nobody asked for — a dropped
    /// connection arrives here and nowhere else.
    /// </summary>
    event EventHandler<SessionStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// Raised when <see cref="Vitals"/> changes. Separate from
    /// <see cref="StateChanged"/> because latency moves while the state does
    /// not, and will move often once M5-18 is measuring it.
    /// </summary>
    event EventHandler<SessionVitalsChangedEventArgs>? VitalsChanged;

    /// <summary>
    /// Connects, completing when the session is live. A session that has
    /// disconnected or failed may be connected again, which is what makes
    /// retry and auto-reconnect (M4-08) possible without rebuilding the tab.
    /// </summary>
    /// <exception cref="RemoteSessionException">The connection could not be made.</exception>
    /// <exception cref="InvalidOperationException">A connect is already in flight, or the session is live.</exception>
    /// <exception cref="OperationCanceledException">
    /// Cancelled, or <see cref="DisconnectAsync"/> was called while connecting.
    /// </exception>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ends the session, and cancels a connect that is still in flight. Safe
    /// to call on a session that is already down, so closing a tab does not
    /// have to check first.
    /// </summary>
    Task DisconnectAsync();
}
