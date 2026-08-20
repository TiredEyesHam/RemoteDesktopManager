namespace Patchbay.Core.Sessions;

/// <summary>
/// Something the RDP control announced (M4-06). A report, not a state.
///
/// The control raises more than this; these are the ten that change what
/// Patchbay believes about a session. Naming them after what the control said
/// rather than what should happen next keeps the deciding in
/// <see cref="SessionSignalRouter"/>, where it can be tested without a Windows
/// target or a server.
/// </summary>
public enum SessionSignal
{
    /// <summary><c>OnConnecting</c>. An attempt has begun.</summary>
    Connecting = 0,

    /// <summary>
    /// <c>OnConnected</c>. The transport is up and there are pixels, usually a
    /// logon screen. Nobody has signed in yet; that is <see cref="LoggedOn"/>.
    /// </summary>
    Connected = 1,

    /// <summary><c>OnLoginComplete</c>. Someone is signed in.</summary>
    LoggedOn = 2,

    /// <summary>
    /// <c>OnDisconnected</c>, with a disconnect reason. The reason is what
    /// separates an ordinary end from a failure, so it must not be dropped on
    /// the way through.
    /// </summary>
    Disconnected = 3,

    /// <summary>
    /// <c>OnLogonError</c>, with a logon error code. Not an end of session: the
    /// control keeps the connection and shows a logon screen, which is what
    /// makes the credential re-prompt in M4-10 possible.
    /// </summary>
    LogonError = 4,

    /// <summary>
    /// <c>OnFatalError</c>, with a control error code. The control has broken,
    /// which is not the same as the far end refusing us.
    /// </summary>
    FatalError = 5,

    /// <summary>
    /// <c>OnAutoReconnecting2</c>. The control lost the transport and is
    /// rejoining the session on its own (M4-08). Ends nothing, changes no
    /// state.
    ///
    /// Not <c>OnAutoReconnecting</c>: its third parameter is an <c>[out]</c>
    /// the control reads back to decide whether to carry on, so a host that
    /// answers it carelessly stops the reconnect it meant to watch, silently.
    /// The newer member has nothing to answer and carries more besides — the
    /// attempt cap, and whether this computer is the one that went offline.
    /// </summary>
    Reconnecting = 6,

    /// <summary>
    /// <c>OnAutoReconnected</c>. The session is back. Nothing ended, so nothing
    /// restarts.
    /// </summary>
    Reconnected = 7,

    /// <summary>
    /// <c>OnIdleTimeoutNotification</c>. The idle timeout the control was given
    /// has run out (M4-15).
    ///
    /// The control does not act on this; it tells the container and waits.
    /// Reading it as an ending gives you a disconnect message sitting over a
    /// session that is still live. Ending it is Patchbay's call and goes out
    /// through <c>Disconnect</c>, so it arrives as a disconnect that was asked
    /// for.
    /// </summary>
    IdleTimedOut = 8,

    /// <summary>
    /// <c>OnAuthenticationWarningDisplayed</c>. The control could not prove the
    /// server and has put its own warning on screen over the session (M4-09).
    ///
    /// Nothing has failed and nothing has ended; the attempt is waiting on a
    /// person, and a session that sits in Connecting with no explanation looks
    /// broken. The warning cannot be replaced by one of ours, because the
    /// certificate is never handed to the container — see
    /// <see cref="RdpAuthenticationType"/> for the rest of that.
    ///
    /// <c>OnReceivedTSPublicKey</c> (DISPID 16) is the only member that carries
    /// server identity, and is left undeclared: it has the same <c>[out]</c>
    /// problem as <c>OnAutoReconnecting</c>, cannot be tested without a server,
    /// and leaving it alone keeps the control's own certificate checking.
    /// </summary>
    AuthenticationWarningDisplayed = 9,

    /// <summary>
    /// <c>OnAuthenticationWarningDismissed</c>. The warning has gone. It does
    /// not say which way it was answered and no other member does either;
    /// accepting continues to <see cref="Connected"/>, refusing arrives as an
    /// ordinary <see cref="Disconnected"/>.
    /// </summary>
    AuthenticationWarningDismissed = 10,
}
