using Patchbay.Core.Sessions;
using Patchbay.Rdp.Interop;

namespace Patchbay.Rdp.Hosting;

/// <summary>
/// What the control calls (M4-06). One method per event, each of which does
/// nothing but name the signal and pass it on.
///
/// It is this thin on purpose. Every method here runs on a stack owned by
/// native code, inside the control's own call frame, and anything that happens
/// on that stack happens while the control is waiting. Deciding what a
/// disconnect means belongs to <see cref="SessionSignalRouter"/>, which runs
/// in managed code and is testable without a server; this end of the wire only
/// has to be right about which event is which.
/// </summary>
internal sealed class RdpEventSink : IMsTscAxEvents
{
    private readonly Action<SessionSignalEventArgs> _report;

    internal RdpEventSink(Action<SessionSignalEventArgs> report)
    {
        ArgumentNullException.ThrowIfNull(report);
        _report = report;
    }

    public void OnConnecting() => Report(SessionSignal.Connecting);

    public void OnConnected() => Report(SessionSignal.Connected);

    public void OnLoginComplete() => Report(SessionSignal.LoggedOn);

    public void OnDisconnected(int discReason) => Report(SessionSignal.Disconnected, discReason);

    public void OnFatalError(int errorCode) => Report(SessionSignal.FatalError, errorCode);

    public void OnLogonError(int lError) => Report(SessionSignal.LogonError, lError);

    public void OnAutoReconnected() => Report(SessionSignal.Reconnected);

    /// <summary>
    /// The control asking whether to keep an idle session (M4-15). It does not
    /// act on the timeout itself, so something above has to.
    /// </summary>
    public void OnIdleTimeoutNotification() => Report(SessionSignal.IdleTimedOut);

    /// <summary>
    /// The control's own warning about the server's identity, appearing and
    /// then going (M4-09). Neither says what was answered, and neither is
    /// asking Patchbay anything — see <see cref="IMsTscAxEvents"/> for the
    /// member that would have, and why it is not declared.
    /// </summary>
    public void OnAuthenticationWarningDisplayed()
        => Report(SessionSignal.AuthenticationWarningDisplayed);

    /// <inheritdoc cref="OnAuthenticationWarningDisplayed" />
    public void OnAuthenticationWarningDismissed()
        => Report(SessionSignal.AuthenticationWarningDismissed);

    /// <summary>
    /// The one event that carries more than a single number (M4-08), which is
    /// why it goes through the args rather than through a code. Inverted on the
    /// way past: the control states what is available and the rest of Patchbay
    /// asks what is missing, because the ordinary case should be the one that
    /// needs no thought.
    /// </summary>
    public void OnAutoReconnecting2(
        int disconnectReason,
        bool networkAvailable,
        int attemptCount,
        int maxAttemptCount)
        => _report(new SessionSignalEventArgs
        {
            Signal = SessionSignal.Reconnecting,
            Code = disconnectReason,
            Reconnect = new SessionReconnectNotice
            {
                Attempt = attemptCount,
                MaxAttempts = maxAttemptCount,
                NetworkLost = !networkAvailable,
                DisconnectReason = disconnectReason,
            },
        });

    private void Report(SessionSignal signal, int code = 0)
        => _report(new SessionSignalEventArgs { Signal = signal, Code = code });
}
