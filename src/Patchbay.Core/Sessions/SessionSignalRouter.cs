using System.Globalization;

namespace Patchbay.Core.Sessions;

/// <summary>
/// Turns what the RDP control announces into what the session is (M4-06).
///
/// The control does not report states, it reports events, and the two do not
/// line up. Three things in particular have to be got right, and none of them
/// is visible from the event names alone:
///
/// <list type="number">
///   <item><b>A disconnect is not news of a failure, or of a success.</b> The
///   same <c>OnDisconnected</c> arrives when someone logs off, when the
///   password was wrong, when the cable is out, and when Patchbay itself asked
///   for the session to end. What separates them is the reason code and what
///   the session was doing at the time.</item>
///   <item><b>A logon error does not end anything.</b> The control keeps the
///   connection up and puts a logon screen in front of the user. Treating it
///   as a failure would tear down a tab that is still perfectly usable, and
///   would make re-prompting for credentials (M4-10) impossible.</item>
///   <item><b>The failure arrives before the disconnect that carries it.</b>
///   <c>OnLogonError</c> or <c>OnFatalError</c> comes first, then a plain
///   <c>OnDisconnected</c>. Whoever handles the disconnect on its own reports
///   "Disconnected" to someone whose password was rejected.</item>
/// </list>
///
/// So this remembers. It is a small amount of state, and it is the reason this
/// type exists rather than a static method.
///
/// <para>
/// <b>Threading.</b> Belongs to the thread the control's events arrive on. The
/// state machine underneath is thread-safe; this is not, and does not need to
/// be.
/// </para>
/// </summary>
public sealed class SessionSignalRouter
{
    // Disconnect reasons. Documented on IMsTscAxEvents::OnDisconnected; 1, 2
    // and 3 are the three the documentation explicitly calls "not an error
    // code", and they are the whole of the ordinary-ending set.
    private const int DisconnectNoInformation = 0;
    private const int DisconnectLocal = 1;
    private const int DisconnectRemoteByUser = 2;
    private const int DisconnectByServer = 3;

    // Logon errors. ARBITRATION_CODE_* run from -7 to -2 and mean winlogon is
    // showing a dialog, not that anything went wrong.
    private const int ArbitrationRefusedDialog = -7;
    private const int ArbitrationContinueLogon = -2;
    private const int LogonWarning = 3;

    private readonly SessionStateMachine _machine;
    private readonly string _endpoint;
    private readonly Func<SessionSignal, int, string> _describe;

    private SessionSignal? _problemSignal;
    private int _problemCode;

    /// <param name="machine">The session's state machine (M4-05).</param>
    /// <param name="endpoint">
    /// What to call the far end in messages — <see cref="SessionRequest.Endpoint"/>.
    /// </param>
    /// <param name="describe">
    /// Turns a code into a sentence. The default says no more than the number,
    /// because saying what 2825 means is the disconnect-reason table (M4-07)
    /// and it is a table, not a line of code. This is the seam it plugs into.
    /// </param>
    public SessionSignalRouter(
        SessionStateMachine machine,
        string endpoint,
        Func<SessionSignal, int, string>? describe = null)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);

        _machine = machine;
        _endpoint = endpoint;
        _describe = describe ?? DescribeByNumber;
    }

    /// <summary>
    /// Whether anyone has signed in. Distinct from being connected: a session
    /// showing a logon screen is connected and nobody is signed in.
    ///
    /// Kept here rather than in the state machine because it does not change
    /// what the session may do next, and adding a seventh state for it would
    /// make every consumer handle a case that looks exactly like Connected.
    /// </summary>
    public bool HasLoggedOn { get; private set; }

    /// <summary>The last logon error code, notices included. Null until one arrives.</summary>
    public int? LastLogonError { get; private set; }

    /// <summary>The last disconnect reason. Null until the session ends.</summary>
    public int? LastDisconnectReason { get; private set; }

    /// <summary>
    /// Whether the control has said the session sat idle past its timeout
    /// (M4-15). Cleared by the next attempt, like everything else here that
    /// describes one connection rather than the session.
    /// </summary>
    public bool IsIdle { get; private set; }

    /// <summary>
    /// Whether the control is showing its own warning about the server's
    /// identity and waiting for an answer (M4-09).
    ///
    /// <para>
    /// Worth having as a fact about the session rather than only as a
    /// sentence, because it is the one pause that is nobody's fault and has no
    /// timeout: everything else that stops progress either fails or comes
    /// back, and this waits for as long as it takes somebody to notice a
    /// dialog. Cleared when the warning goes and by the next attempt, like
    /// everything else here that describes one connection.
    /// </para>
    /// </summary>
    public bool IsAwaitingTrustDecision { get; private set; }

    /// <summary>
    /// Whether the far end has refused a sign-in that a different one might
    /// fix, and the session is still open behind its own logon screen (M4-10).
    ///
    /// <para>
    /// <b>This is the fact the re-prompt hangs off, and its timing is the
    /// whole point.</b> A logon error ends nothing — the control keeps the
    /// connection and puts a logon screen up — so the moment to ask is now,
    /// while the tab is alive and the session can simply be given a different
    /// password. Waiting for the disconnect and offering a retry afterwards is
    /// the behaviour this item exists to avoid: by then the tab is gone and
    /// what is on offer is a fresh connection wearing a retry button.
    /// </para>
    ///
    /// <para>
    /// False for a refusal no password can fix — a locked, disabled or expired
    /// account (<see cref="LogonFailure"/>) — because offering there is how
    /// somebody types their correct password three more times into a door that
    /// will not open, and on a locked account every attempt extends the
    /// lockout they are already serving.
    /// </para>
    ///
    /// <para>
    /// Cleared by signing in, by the next attempt and by the end of the
    /// session, like everything else here that describes one connection.
    /// </para>
    /// </summary>
    public bool IsAwaitingCredentials { get; private set; }

    /// <summary>
    /// Whether a failure has been announced that no disconnect has yet
    /// delivered. True between <c>OnLogonError</c> and the <c>OnDisconnected</c>
    /// that follows it.
    /// </summary>
    public bool HasUnreportedProblem => _problemSignal is not null;

    /// <summary>
    /// Whether a disconnect reason means the session simply ended. Only the
    /// three the documentation calls "not an error code" qualify: a local
    /// disconnect, the remote user logging off, and the server closing the
    /// session. Everything else is something going wrong, and that is the
    /// distinction M4-05's rule rests on — a drop is not a failure, but a
    /// break is.
    /// </summary>
    public static bool IsOrdinaryDisconnect(int reason)
        => reason is DisconnectLocal or DisconnectRemoteByUser or DisconnectByServer;

    /// <summary>
    /// Whether a logon error code is winlogon narrating itself rather than a
    /// problem. The trap is that this is not simply "negative": the
    /// ARBITRATION_CODE_* values from -7 to -2 are notices, but -1 is access
    /// denied and STATUS_LOGON_FAILURE is -1073741715. Reading the sign alone
    /// swallows the two failures people actually hit.
    /// </summary>
    public static bool IsWinlogonNotice(int logonError)
        => logonError is (>= ArbitrationRefusedDialog and <= ArbitrationContinueLogon) or LogonWarning;

    /// <summary>
    /// Applies one announcement. Anything that does not make sense from where
    /// the session is — a second disconnect, a connect notice on a session that
    /// is already up — is dropped rather than argued with; the control reports
    /// the world, and the world repeats itself.
    /// </summary>
    /// <param name="signal">What the control said.</param>
    /// <param name="code">The number it came with, if any.</param>
    /// <param name="notice">
    /// The detail that comes with <see cref="SessionSignal.Reconnecting"/> and
    /// with nothing else (M4-08).
    /// </param>
    public void Report(SessionSignal signal, int code = 0, SessionReconnectNotice? notice = null)
    {
        switch (signal)
        {
            case SessionSignal.Connecting:
                // A fresh attempt: whatever went wrong last time is not this
                // attempt's problem, and leaving it behind would report the
                // previous failure against this connection.
                Forget();
                HasLoggedOn = false;
                IsIdle = false;
                IsAwaitingTrustDecision = false;
                IsAwaitingCredentials = false;
                LastDisconnectReason = null;
                _machine.TryMoveTo(SessionState.Connecting, $"Connecting to {_endpoint}…");
                break;

            case SessionSignal.Connected:
                _machine.TryMoveTo(SessionState.Connected, $"Connected to {_endpoint}.");
                break;

            case SessionSignal.LoggedOn:
                // No transition. The session was already live — pixels arrive
                // with Connected — so there is nothing here the state machine
                // can express, and asking it to would be refused anyway.
                HasLoggedOn = true;

                // Somebody got in. Whatever was refused on the way is no
                // longer a question worth putting to them.
                IsAwaitingCredentials = false;
                break;

            case SessionSignal.LogonError:
                LastLogonError = code;

                if (!IsWinlogonNotice(code))
                {
                    // Held, not acted on. The session is still up and the user
                    // is looking at a logon screen; this only matters if the
                    // connection then goes away, and then it is the reason.
                    Remember(signal, code);

                    // …and separately, whether it is worth asking again while
                    // the tab is still alive (M4-10). No transition either
                    // way: the session has not failed and has not progressed,
                    // and a state for it would look exactly like Connected to
                    // everyone who did not need to know.
                    IsAwaitingCredentials = LogonFailure.IsWorthAskingAgain(code);

                    _machine.Announce(IsAwaitingCredentials
                        ? $"{_endpoint} did not accept that sign-in. The session is still "
                            + "open and can be given a different one."
                        : $"{_endpoint} refused the account: {_describe(signal, code)}");
                }

                break;

            case SessionSignal.FatalError:
                // Unlike a logon error this one is terminal: the control says
                // it has broken, and a broken control has nothing left to show.
                Remember(signal, code);
                Fail($"The Remote Desktop control failed: {_describe(signal, code)}");
                break;

            case SessionSignal.Reconnecting:
                // No transition, deliberately (M4-08). The control has not lost
                // the session — it is holding it open and rejoining it with the
                // cookie it was issued, and calling that a disconnect would tear
                // down a tab that is about to come back with its desktop intact.
                // Announced rather than moved, so the person watching a frozen
                // picture is told why it is frozen.
                _machine.Announce(Rejoining(notice));
                break;

            case SessionSignal.Reconnected:
                _machine.Announce($"Reconnected to {_endpoint}.");
                break;

            case SessionSignal.IdleTimedOut:
                // No transition either, and for a different reason from the
                // reconnect above: the session is still up and still usable,
                // and the control is asking whether to keep it. Ending it is
                // the host's call and it goes out through Disconnect, which is
                // what makes it an ending nobody chases (M4-08).
                IsIdle = true;
                _machine.Announce($"The session to {_endpoint} has been idle and is being closed.");
                break;

            case SessionSignal.AuthenticationWarningDisplayed:
                // No transition, and nothing has gone wrong: the control could
                // not prove the server and is asking a person what to do about
                // it (M4-09). The attempt is neither failing nor progressing
                // until somebody answers, and a connection that appears to
                // have stalled with no explanation is the thing this exists to
                // prevent. Where the dialog is matters as much as that it is
                // there — it belongs to the control, so it is inside the
                // session's own window and not over the shell.
                IsAwaitingTrustDecision = true;
                _machine.Announce(
                    $"{_endpoint} could not be proved to be the computer it says it is. "
                    + "The session is waiting for you to answer the warning on it.");
                break;

            case SessionSignal.AuthenticationWarningDismissed:
                // Which way it was answered is not said, and nothing on the
                // control says it either. Guessing here would be a status bar
                // reporting a decision that had not been made, so this only
                // takes the sentence back down; whatever was chosen arrives
                // next, as a connection or as a disconnect.
                IsAwaitingTrustDecision = false;

                if (_machine.State is SessionState.Connecting)
                {
                    _machine.Announce($"Connecting to {_endpoint}…");
                }

                break;

            case SessionSignal.Disconnected:
                LastDisconnectReason = code;
                ApplyDisconnect(code);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(signal), signal, "Unknown session signal.");
        }
    }

    private void ApplyDisconnect(int reason)
    {
        // Asked for. Whatever the control says on the way out, a disconnect
        // Patchbay started is a disconnect, and dressing it up as a failure
        // would offer a reconnect to someone who just closed the tab.
        if (_machine.State is SessionState.Disconnecting)
        {
            _machine.TryMoveTo(SessionState.Disconnected, "Disconnected.");
            Forget();
            return;
        }

        // The failure that was announced a moment ago, finally arriving as an
        // end of session. This is the case that reports "the password was
        // wrong" instead of "disconnected".
        if (_problemSignal is { } problem)
        {
            Fail(DescribeProblem(problem, _problemCode));
            Forget();
            return;
        }

        switch (reason)
        {
            case DisconnectLocal:
                _machine.TryMoveTo(SessionState.Disconnected, "Disconnected.");
                return;

            case DisconnectRemoteByUser:
            case DisconnectByServer:
                _machine.TryMoveTo(SessionState.Disconnected, "The remote computer ended the session.");
                return;

            default:
                break;
        }

        // Never got up. Any reason at all is a failed attempt at this point,
        // including "no information": whatever the control does or does not
        // know, the connection plainly did not happen.
        if (_machine.State is SessionState.Connecting)
        {
            Fail($"Could not connect to {_endpoint}: {_describe(SessionSignal.Disconnected, reason)}");
            return;
        }

        // A live session that ended for no stated reason. Left as an ordinary
        // end rather than a failure — there is no evidence anything broke, and
        // crying wolf here is what makes people ignore the times it did.
        if (reason == DisconnectNoInformation)
        {
            _machine.TryMoveTo(SessionState.Disconnected, "The session ended.");
            return;
        }

        Fail($"The connection to {_endpoint} was lost: {_describe(SessionSignal.Disconnected, reason)}");
    }

    /// <summary>
    /// What to say while the control rejoins a session (M4-08). The offline
    /// case is called out because it is the one form of the problem the person
    /// can act on: the far end is fine and the computer in front of them is not.
    /// </summary>
    private string Rejoining(SessionReconnectNotice? notice)
    {
        if (notice is not { } detail)
        {
            return $"Reconnecting to {_endpoint}…";
        }

        string counted = string.Create(
            CultureInfo.InvariantCulture,
            $"Reconnecting to {_endpoint} — attempt {detail.Attempt} of {detail.MaxAttempts}");

        return detail.NetworkLost ? counted + " (this computer is offline)" : counted + "…";
    }

    private string DescribeProblem(SessionSignal signal, int code) => signal switch
    {
        SessionSignal.LogonError => $"Could not sign in to {_endpoint}: {_describe(signal, code)}",
        _ => $"The Remote Desktop control failed: {_describe(signal, code)}",
    };

    private void Fail(string message)
        => _machine.TryMoveTo(SessionState.Failed, message, new RemoteSessionException(message));

    private void Remember(SessionSignal signal, int code)
    {
        _problemSignal = signal;
        _problemCode = code;
    }

    private void Forget()
    {
        _problemSignal = null;
        _problemCode = 0;

        // The offer goes with the session. A prompt still showing over a tab
        // that has ended is asking for a password nothing will be done with.
        IsAwaitingCredentials = false;
    }

    private static string DescribeByNumber(SessionSignal signal, int code)
        => string.Create(CultureInfo.InvariantCulture, $"error code {code}");
}
