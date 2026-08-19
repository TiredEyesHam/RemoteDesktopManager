namespace Patchbay.Core.Sessions;

/// <summary>
/// Where a session is in its life. Deliberately small: this is the state the
/// interface promises, and what may follow what is
/// <see cref="SessionStateMachine"/>'s (M4-05). The reconnect states M4-08
/// needs are still to come.
///
/// The RDP control reports rather more than this — see
/// <see cref="SessionSignal"/> for the six announcements Patchbay listens to
/// and <see cref="SessionSignalRouter"/> for what it makes of them. They
/// collapse into these six states because these are what the interface has to
/// draw: a spinner, a live session, or a reason it is not.
/// </summary>
public enum SessionState
{
    /// <summary>Created but never connected.</summary>
    Idle = 0,

    /// <summary>A connect attempt is in flight.</summary>
    Connecting = 1,

    /// <summary>Live. The only state in which pixels are on screen.</summary>
    Connected = 2,

    /// <summary>A disconnect has been asked for and has not finished.</summary>
    Disconnecting = 3,

    /// <summary>
    /// Ended without error — either because someone asked, or because the far
    /// end closed the session. Both are ordinary, so neither is a failure.
    /// </summary>
    Disconnected = 4,

    /// <summary>
    /// Ended because something went wrong. Carries a message worth showing;
    /// <see cref="SessionStateChangedEventArgs.Error"/> has the detail.
    /// </summary>
    Failed = 5,
}
