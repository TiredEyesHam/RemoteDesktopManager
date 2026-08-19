using System.Runtime.InteropServices;

namespace Patchbay.Rdp.Interop;

/// <summary>
/// The control's outgoing interface — what it calls when something happens
/// (M4-06). Patchbay implements this one rather than consuming it, which makes
/// it the exact opposite of every other declaration in this folder.
///
/// The others are empty because calls go out by name and a vtable Patchbay
/// never transcribes is a vtable Patchbay cannot get wrong. Here the calls
/// come *in*, and the control picks the member by DISPID, so the numbers below
/// are the contract. They are not sequential and they are not in declaration
/// order in the type library either — <c>OnLogonError</c> is 22 and sits
/// between two members numbered 21 and 29 — so leaving them implicit would
/// wire the control's disconnect notice to whatever member happened to be
/// declared fourth. Every number was read from the type library in
/// <c>mstscax.dll</c> (10.0.26100.8875).
///
/// Eleven of the thirty-two are declared, and the rest deliberately are not:
/// an unimplemented member answers <c>DISP_E_MEMBERNOTFOUND</c>, which the
/// control ignores. They arrive with their own tasks —
/// <c>OnNetworkStatusChanged</c> (which carries the round trip in
/// milliseconds) with M5-18, the full-screen pair with M5-05,
/// <c>OnRequestContainerMinimize</c> and <c>OnConfirmClose</c> with the tab
/// work in M5.
///
/// Two are left out for a stronger reason than "not yet", and they are the
/// same reason twice: <b>a member with an <c>[out]</c> parameter is a member
/// that decides something</b>, and neither decision can be tested without a
/// server. <c>OnAutoReconnecting</c> is DISPID 17 and its third parameter is
/// an <c>[out]</c> the control reads back to decide whether to keep trying, so
/// implementing it makes Patchbay responsible for an answer it has no way to
/// test without a server that can be made to drop a live session. Answering it
/// wrongly stops the reconnect silently. Leaving the member undeclared leaves
/// the control's own default in place, and <c>OnAutoReconnecting2</c> below
/// carries strictly more information with nothing to answer.
///
/// <c>OnReceivedTSPublicKey</c> is DISPID 16, and it is the one member that
/// carries anything about who the server is — the raw public key, with an
/// <c>[out]</c> the control reads back to decide whether to go on. It is the
/// obvious place to hang M4-09's certificate warning and it is not taken.
/// Answering it wrongly refuses every server or waves every server through,
/// both without a sound, and the key alone is not a certificate: it has no
/// subject, no expiry and no chain, so the dialog it would let Patchbay draw
/// could not say more than the control's own already does. Undeclared, the
/// control keeps checking certificates the way <c>mstsc.exe</c> does, which is
/// the behaviour to want by default. What Patchbay takes instead is the pair
/// below, which only report.
///
/// The parameters are <c>long</c> in IDL, which is a 32-bit integer in COM.
/// </summary>
[ComImport]
[Guid(RdpIids.IMsTscAxEvents)]
[InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
internal interface IMsTscAxEvents
{
    /// <summary>An attempt has begun.</summary>
    [DispId(1)]
    void OnConnecting();

    /// <summary>
    /// The transport is up. Not the same as being signed in: what is on screen
    /// at this point is usually a logon prompt.
    /// </summary>
    [DispId(2)]
    void OnConnected();

    /// <summary>Someone is signed in.</summary>
    [DispId(3)]
    void OnLoginComplete();

    /// <summary>
    /// The session ended, for one of about fifty documented reasons and a
    /// number of undocumented ones. See <c>SessionSignalRouter</c> for what
    /// separates an ordinary ending from a failure.
    /// </summary>
    [DispId(4)]
    void OnDisconnected(int discReason);

    /// <summary>The control itself has broken.</summary>
    [DispId(10)]
    void OnFatalError(int errorCode);

    /// <summary>
    /// A logon attempt failed, or winlogon is showing a dialog. The session is
    /// still up either way — this is a notice, not an ending.
    /// </summary>
    [DispId(22)]
    void OnLogonError(int lError);

    /// <summary>
    /// The idle timeout given to <c>MinutesToIdleTimeout</c> has run out
    /// (M4-15). DISPID 13, read from the type library, and no parameters.
    ///
    /// <para>
    /// It sits between <c>OnLoginComplete</c> and the full-screen pair, which
    /// is exactly the sort of neighbourhood that makes implicit ordering
    /// dangerous: a method declared one slot out would wire the idle timeout to
    /// a request to go full screen, and both take no arguments, so nothing
    /// would fail — the session would simply do the wrong thing.
    /// </para>
    /// </summary>
    [DispId(13)]
    void OnIdleTimeoutNotification();

    /// <summary>
    /// The control got a session back on its own (M4-08). Nothing ended, so
    /// nothing has to be restarted.
    /// </summary>
    [DispId(33)]
    void OnAutoReconnected();

    /// <summary>
    /// The control lost the transport and is rejoining the session it already
    /// has, using the cookie the server issued when it was established (M4-08).
    ///
    /// <paramref name="networkAvailable"/> is about <em>this</em> computer, and
    /// it is the useful half: it separates a server that went away from a
    /// laptop whose wireless dropped, and only one of those is something the
    /// person in front of it can fix.
    /// </summary>
    [DispId(34)]
    void OnAutoReconnecting2(
        int disconnectReason,
        [MarshalAs(UnmanagedType.VariantBool)] bool networkAvailable,
        int attemptCount,
        int maxAttemptCount);

    /// <summary>
    /// The control could not prove the server and has put its own warning up
    /// over the session (M4-09). DISPID 18, read from the type library, and no
    /// parameters — deliberately so: this announces a dialog, it does not ask
    /// anything.
    ///
    /// <para>
    /// Nothing has failed and nothing has ended. The attempt is paused on a
    /// person, which is a state no other event produces and the reason this
    /// one is worth sinking at all: without it a session waits in Connecting
    /// with nothing to say while a dialog waits inside a window that may be
    /// behind something else.
    /// </para>
    /// </summary>
    [DispId(18)]
    void OnAuthenticationWarningDisplayed();

    /// <summary>
    /// The warning has gone (M4-09). DISPID 19, no parameters, and — the part
    /// worth knowing — <b>no answer</b>. The control does not say which way it
    /// was dismissed and nothing else on it does either, so what happens next
    /// is the only evidence: a connection, or a disconnect.
    /// </summary>
    [DispId(19)]
    void OnAuthenticationWarningDismissed();
}
