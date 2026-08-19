namespace Patchbay.Core.Sessions;

/// <summary>
/// Something the RDP control announced (M4-06). Not a state — a report.
///
/// The control raises rather more than this; these are the ten that change
/// what Patchbay believes about a session, or what it has to say about one.
/// The rest are either someone else's job (the full-screen pair is M5-05,
/// <c>OnNetworkStatusChanged</c> carries the round trip M5-18 wants) or noise
/// for now. <c>OnReceivedTSPublicKey</c> is neither, and is left undeclared on
/// purpose — see <see cref="AuthenticationWarningDisplayed"/>.
///
/// This lives in <c>Core</c>, and it is deliberately named after what the
/// control said rather than what should happen next: the deciding is
/// <see cref="SessionSignalRouter"/>'s, and keeping the two apart is what lets
/// the interesting half be tested without a Windows target or a real server.
/// </summary>
public enum SessionSignal
{
    /// <summary><c>OnConnecting</c>. The control has begun an attempt.</summary>
    Connecting = 0,

    /// <summary>
    /// <c>OnConnected</c>. The transport is up and there are pixels: usually a
    /// logon screen. It does not mean anyone has signed in — that is
    /// <see cref="LoggedOn"/>, and a session can sit here indefinitely.
    /// </summary>
    Connected = 1,

    /// <summary><c>OnLoginComplete</c>. Someone is signed in.</summary>
    LoggedOn = 2,

    /// <summary>
    /// <c>OnDisconnected</c>, carrying a disconnect reason. The reason is the
    /// only thing that separates an ordinary end from a failure, so it must
    /// not be dropped on the way through.
    /// </summary>
    Disconnected = 3,

    /// <summary>
    /// <c>OnLogonError</c>, carrying a logon error code. Notably <b>not</b> an
    /// end of session: the control keeps the connection and puts a logon
    /// screen up, which is precisely what makes re-prompting for credentials
    /// without losing the tab possible (M4-10).
    /// </summary>
    LogonError = 4,

    /// <summary>
    /// <c>OnFatalError</c>, carrying a control error code. The control itself
    /// has broken, which is a different thing from the far end refusing us.
    /// </summary>
    FatalError = 5,

    /// <summary>
    /// <c>OnAutoReconnecting2</c>. The control lost the transport and is
    /// rejoining the session on its own (M4-08) — see
    /// <see cref="SessionReconnectNotice"/> for why this ends nothing and
    /// changes no state.
    ///
    /// <para>
    /// Deliberately <c>OnAutoReconnecting2</c> and not <c>OnAutoReconnecting</c>.
    /// The older member's third parameter is an <c>[out]</c> the control reads
    /// back to decide whether to carry on, so a host that implements it and
    /// answers carelessly stops the reconnect it was only trying to watch —
    /// silently, and with no way to tell that apart from a server that refused.
    /// The newer one has nothing to answer and carries strictly more: the
    /// attempt cap, and whether this computer is the one that went offline.
    /// Not implementing the older member leaves the control's own default in
    /// place, which is to continue.
    /// </para>
    /// </summary>
    Reconnecting = 6,

    /// <summary>
    /// <c>OnAutoReconnected</c>. The control got the session back. Nothing
    /// ended, so nothing restarts — this is only worth saying out loud.
    /// </summary>
    Reconnected = 7,

    /// <summary>
    /// <c>OnIdleTimeoutNotification</c>. The idle timeout the control was given
    /// has run out (M4-15).
    ///
    /// <para>
    /// <b>The control does not act on this; it tells the container and waits.</b>
    /// That is unusual enough to be worth stating, because the obvious reading
    /// — that the session has just ended — produces a host that reports a
    /// disconnect and then keeps a live session on screen underneath the
    /// message. Ending it is Patchbay's move to make, which is also what makes
    /// it an ending nobody chases: the disconnect goes out through
    /// <c>Disconnect</c>, so it arrives as a disconnect that was asked for and
    /// M4-08 leaves it alone.
    /// </para>
    /// </summary>
    IdleTimedOut = 8,

    /// <summary>
    /// <c>OnAuthenticationWarningDisplayed</c>. The control could not prove
    /// the server and has put its own warning on screen over the session
    /// (M4-09).
    ///
    /// <para>
    /// <b>Nothing has gone wrong and nothing has ended.</b> The attempt is
    /// paused on a person, and the only thing that changes is what there is to
    /// say about it: a session that sits in Connecting for two minutes with no
    /// explanation looks broken, and the explanation is that a dialog is
    /// waiting behind a window somebody may not be looking at.
    /// </para>
    ///
    /// <para>
    /// The warning is the control's, not Patchbay's, and it cannot be replaced
    /// by one of ours: the certificate is never handed to the container, so
    /// there is nothing to draw a better dialog with. The trust-once choice
    /// M4-09 asked for is in that dialog already, and the control keeps the
    /// answer. See <see cref="RdpAuthenticationType"/> for the whole of that
    /// finding.
    /// </para>
    ///
    /// <para>
    /// <b>The one hook that does carry server identity is deliberately not
    /// taken.</b> <c>OnReceivedTSPublicKey</c> (DISPID 16) hands over the
    /// server's public key with an <c>[out]</c> the control reads back to
    /// decide whether to continue — the same shape as
    /// <c>OnAutoReconnecting</c>, and refused here for the same reason and
    /// then some. Answering it wrongly either stops every connection or waves
    /// every server through, both silently, and it cannot be tested without a
    /// server. Leaving the member undeclared leaves the control's own
    /// certificate checking in place, which is the behaviour anybody would
    /// want by default.
    /// </para>
    /// </summary>
    AuthenticationWarningDisplayed = 9,

    /// <summary>
    /// <c>OnAuthenticationWarningDismissed</c>. The warning has gone.
    ///
    /// <para>
    /// It does not say what was answered, and there is no member that does.
    /// What follows says it instead: accepting continues to
    /// <see cref="Connected"/>, refusing arrives as an ordinary
    /// <see cref="Disconnected"/>, and both are already read correctly.
    /// </para>
    /// </summary>
    AuthenticationWarningDismissed = 10,
}
