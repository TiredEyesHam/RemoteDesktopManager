using System.Runtime.InteropServices;

namespace Patchbay.Rdp.Interop;

/// <summary>
/// The control's outgoing interface, what it calls when something happens
/// (M4-06). Patchbay implements this one rather than consuming it.
///
/// The other declarations in this folder are empty because calls go out by
/// name. Here the calls come in and the control picks the member by DISPID, so
/// the numbers are the contract. They are not sequential and not in
/// declaration order in the type library — <c>OnLogonError</c> is 22 and sits
/// between members numbered 21 and 29 — so leaving them implicit would wire
/// the disconnect notice to whatever happened to be declared fourth. Every
/// number was read from the type library in <c>mstscax.dll</c>
/// (10.0.26100.8875).
///
/// Eleven of the thirty-two are declared. An unimplemented member answers
/// <c>DISP_E_MEMBERNOTFOUND</c>, which the control ignores, so the rest arrive
/// with their own tasks: <c>OnNetworkStatusChanged</c> with M5-18, the
/// full-screen pair with M5-05, <c>OnRequestContainerMinimize</c> and
/// <c>OnConfirmClose</c> with the tab work.
///
/// Two are left out for a stronger reason. A member with an <c>[out]</c>
/// parameter decides something, and neither decision can be tested without a
/// server. <c>OnAutoReconnecting</c> (17) has an <c>[out]</c> the control reads
/// back to decide whether to keep trying, and answering it wrongly stops the
/// reconnect silently; <c>OnAutoReconnecting2</c> below carries more with
/// nothing to answer. <c>OnReceivedTSPublicKey</c> (16) is the only member
/// carrying anything about who the server is, and would be the obvious place
/// for M4-09's certificate warning, but a raw public key has no subject, expiry
/// or chain, so the dialog it would allow could say no more than the control's
/// own. Undeclared, the control keeps checking certificates the way
/// <c>mstsc.exe</c> does.
///
/// Parameters are <c>long</c> in IDL, which is a 32-bit integer in COM.
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
    /// number of undocumented ones. <c>SessionSignalRouter</c> has what
    /// separates an ordinary ending from a failure.
    /// </summary>
    [DispId(4)]
    void OnDisconnected(int discReason);

    /// <summary>The control itself has broken.</summary>
    [DispId(10)]
    void OnFatalError(int errorCode);

    /// <summary>
    /// A logon attempt failed, or winlogon is showing a dialog. The session is
    /// still up either way.
    /// </summary>
    [DispId(22)]
    void OnLogonError(int lError);

    /// <summary>
    /// The idle timeout given to <c>MinutesToIdleTimeout</c> has run out
    /// (M4-15).
    ///
    /// It sits between <c>OnLoginComplete</c> and the full-screen pair, which
    /// is the sort of neighbourhood that makes implicit ordering dangerous: one
    /// slot out and the idle timeout wires to a request to go full screen. Both
    /// take no arguments, so nothing would fail.
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
    /// has, using the cookie the server issued (M4-08).
    ///
    /// <paramref name="networkAvailable"/> is about this computer, and is the
    /// useful half: it separates a server that went away from a laptop whose
    /// wireless dropped, and only one of those is fixable by the person in
    /// front of it.
    /// </summary>
    [DispId(34)]
    void OnAutoReconnecting2(
        int disconnectReason,
        [MarshalAs(UnmanagedType.VariantBool)] bool networkAvailable,
        int attemptCount,
        int maxAttemptCount);

    /// <summary>
    /// The control could not prove the server and has put its own warning up
    /// over the session (M4-09). No parameters: this announces a dialog, it
    /// does not ask anything.
    ///
    /// Nothing has failed and nothing has ended. Without it a session waits in
    /// Connecting with nothing to say while a dialog waits inside a window that
    /// may be behind something else.
    /// </summary>
    [DispId(18)]
    void OnAuthenticationWarningDisplayed();

    /// <summary>
    /// The warning has gone (M4-09). It does not say which way it was
    /// dismissed and nothing else on the control does either, so what happens
    /// next is the only evidence: a connection, or a disconnect.
    /// </summary>
    [DispId(19)]
    void OnAuthenticationWarningDismissed();
}
