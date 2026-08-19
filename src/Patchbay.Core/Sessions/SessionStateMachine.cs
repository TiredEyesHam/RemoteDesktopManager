namespace Patchbay.Core.Sessions;

/// <summary>
/// The rules about what a session may do next, in one place (M4-05).
///
/// Until now these lived as scattered <c>if</c> statements inside the one
/// implementation that had them. That does not survive a second
/// implementation: the real control reports its own state changes, some of
/// them unasked for and some of them out of order, and every one of those
/// arrives as a claim that has to be checked against what Patchbay believes.
/// Holding the transition table in a single tested object is what stops the
/// answer differing between the fake and the real thing.
///
/// Two ways in, deliberately:
///
/// <list type="bullet">
///   <item><see cref="MoveTo"/> throws. Use it where Patchbay is the one
///   deciding, because an illegal move there is a bug and should be loud.</item>
///   <item><see cref="TryMoveTo"/> returns false. Use it for anything the RDP
///   control tells us (M4-06), because a control that announces a disconnect
///   twice, or announces one after we already tore the session down, is
///   reporting the world rather than making a mistake.</item>
/// </list>
///
/// Thread-safe, because the two do not arrive on the same thread: the control
/// raises its events on the UI thread while a close or a cancel can come from
/// anywhere. Handlers run outside the lock so that one which turns round and
/// asks for another transition cannot deadlock.
/// </summary>
public sealed class SessionStateMachine
{
    private readonly Lock _gate = new();

    private SessionState _state = SessionState.Idle;
    private string? _statusMessage;

    /// <summary>Raised after each accepted transition, outside the lock.</summary>
    public event EventHandler<SessionStateChangedEventArgs>? Changed;

    /// <summary>Where the session is now.</summary>
    public SessionState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    /// <summary>The message that came with the current state, if any.</summary>
    public string? StatusMessage
    {
        get
        {
            lock (_gate)
            {
                return _statusMessage;
            }
        }
    }

    /// <summary>
    /// Whether connecting is allowed from here. Bind a connect command to this
    /// rather than testing for a particular state; retry after a failure and
    /// reconnect after a drop are both meant to work (M4-08).
    /// </summary>
    public bool CanConnect => State is SessionState.Idle or SessionState.Disconnected or SessionState.Failed;

    /// <summary>
    /// Whether there is anything to disconnect. True while connecting as well
    /// as while connected, because abandoning an attempt that is taking too
    /// long is the same gesture to the person making it.
    /// </summary>
    public bool CanDisconnect => State is SessionState.Connecting or SessionState.Connected;

    /// <summary>Whether something is in flight and the UI should say so.</summary>
    public bool IsBusy => State is SessionState.Connecting or SessionState.Disconnecting;

    /// <summary>Whether there is a live session behind this.</summary>
    public bool IsLive => State is SessionState.Connected;

    /// <summary>
    /// Whether a move is allowed, independent of any particular session. The
    /// whole table is here, and it is the only place it appears.
    /// </summary>
    public static bool IsLegal(SessionState from, SessionState to) => (from, to) switch
    {
        // Nothing has happened yet; the only way out is to try.
        (SessionState.Idle, SessionState.Connecting) => true,

        // An attempt ends one of four ways: it works, it is called off, it
        // breaks, or someone asks for it to be stopped and it winds down
        // properly rather than being abandoned.
        (SessionState.Connecting, SessionState.Connected) => true,
        (SessionState.Connecting, SessionState.Disconnected) => true,
        (SessionState.Connecting, SessionState.Disconnecting) => true,
        (SessionState.Connecting, SessionState.Failed) => true,

        // A live session ends when asked, when the far end goes away, or when
        // it breaks. The middle one is not a failure and must not be reported
        // as one: logging off is a disconnect.
        (SessionState.Connected, SessionState.Disconnecting) => true,
        (SessionState.Connected, SessionState.Disconnected) => true,
        (SessionState.Connected, SessionState.Failed) => true,

        (SessionState.Disconnecting, SessionState.Disconnected) => true,
        (SessionState.Disconnecting, SessionState.Failed) => true,

        // Both resting states can be left again, which is what lets a retry
        // reuse its tab instead of building a new one.
        (SessionState.Disconnected, SessionState.Connecting) => true,
        (SessionState.Failed, SessionState.Connecting) => true,

        _ => false,
    };

    /// <summary>
    /// Moves the session on, or throws if that is not a thing that can happen.
    /// </summary>
    /// <exception cref="InvalidOperationException">The move is not legal.</exception>
    public void MoveTo(SessionState next, string? message = null, Exception? error = null)
    {
        if (TryMoveTo(next, message, error, out SessionState from))
        {
            return;
        }

        throw new InvalidOperationException(
            from == next
                ? $"This session is already {next}."
                : $"A session cannot go from {from} to {next}.");
    }

    /// <summary>
    /// News that does not change where the session is (M4-08).
    ///
    /// There is exactly one thing that needs this and it is the control
    /// rejoining a session it briefly lost: the session has not ended, so no
    /// transition is honest, and yet somebody is looking at a picture that has
    /// stopped moving and is owed a sentence. Raised through the same event as
    /// everything else, with <see cref="SessionStateChangedEventArgs.PreviousState"/>
    /// equal to <see cref="SessionStateChangedEventArgs.State"/>, which is what
    /// marks it as news rather than a move — and which the reconnect rules read
    /// as no ending at all, because it is not one.
    ///
    /// Returns false when the message is the one already showing, so a control
    /// repeating itself does not repaint anything.
    /// </summary>
    public bool Announce(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        SessionStateChangedEventArgs args;

        lock (_gate)
        {
            if (string.Equals(_statusMessage, message, StringComparison.Ordinal))
            {
                return false;
            }

            _statusMessage = message;

            args = new SessionStateChangedEventArgs
            {
                PreviousState = _state,
                State = _state,
                Message = message,
            };
        }

        Changed?.Invoke(this, args);
        return true;
    }

    /// <summary>
    /// Moves the session on if that is legal, and reports whether it was.
    /// Asking for the state it is already in is not an error and not a change:
    /// it returns false and raises nothing.
    /// </summary>
    public bool TryMoveTo(SessionState next, string? message = null, Exception? error = null)
        => TryMoveTo(next, message, error, out _);

    private bool TryMoveTo(SessionState next, string? message, Exception? error, out SessionState from)
    {
        SessionStateChangedEventArgs args;

        lock (_gate)
        {
            from = _state;

            if (from == next || !IsLegal(from, next))
            {
                return false;
            }

            _state = next;
            _statusMessage = message;

            args = new SessionStateChangedEventArgs
            {
                PreviousState = from,
                State = next,
                Message = message,
                Error = error,
            };
        }

        // Outside the lock. A handler that closes the tab it was told about
        // will come straight back in here, and it should not find the door
        // held by the thread that called it.
        Changed?.Invoke(this, args);
        return true;
    }
}
