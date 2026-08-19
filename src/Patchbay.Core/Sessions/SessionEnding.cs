namespace Patchbay.Core.Sessions;

/// <summary>
/// How a session stopped, in the terms the reconnect rules need (M4-08).
///
/// The state a session lands in is not enough on its own, and neither is the
/// state it came from. What matters is the pair, because the same
/// <see cref="SessionState.Disconnected"/> is reached by someone closing a tab,
/// by someone signing out at the far end, and by an attempt being called off
/// half-way — and only one of those three is a reason to do anything at all.
/// The pair separates them, which is why
/// <see cref="SessionStateChangedEventArgs"/> has carried both since M4-05.
///
/// The one fact the pair cannot supply is whether a sign-in was refused, and
/// that is the fact the whole safety of automatic retrying rests on. It comes
/// from <see cref="SessionSignalRouter.LastLogonError"/>, which is the only
/// thing that knows.
/// </summary>
public readonly record struct SessionEnding
{
    /// <summary>Where the session was.</summary>
    public required SessionState From { get; init; }

    /// <summary>Where it is now.</summary>
    public required SessionState To { get; init; }

    /// <summary>
    /// The last logon error the control reported, or null if it never
    /// reported one. Winlogon notices count as errors here and are sorted out
    /// by <see cref="IsRefusal"/>, because the router hands over what it was
    /// told rather than an opinion about it.
    /// </summary>
    public int? LogonError { get; init; }

    /// <summary>Whether the session is actually over. Nothing else is an ending.</summary>
    public bool IsEnded => To is SessionState.Disconnected or SessionState.Failed;

    /// <summary>
    /// Whether a person ended this, rather than the world. Two shapes: a
    /// disconnect that was asked for and wound down properly, and an attempt
    /// abandoned before it finished — which is what
    /// <see cref="SessionState.Connecting"/> to
    /// <see cref="SessionState.Disconnected"/> means and has meant since M4-05.
    /// Both are somebody changing their mind, and chasing either would be
    /// arguing with them.
    /// </summary>
    public bool WasCalledOff =>
        From is SessionState.Disconnecting
        || (From is SessionState.Connecting && To is SessionState.Disconnected);

    /// <summary>
    /// Whether a working session broke. This is the one ending worth
    /// reconnecting from, and the reason is that it is the only one nobody
    /// chose: the session was up, it was doing its job, and it stopped.
    ///
    /// Note what is deliberately not here. A live session that ends with no
    /// stated reason is a plain disconnect by M4-06's reckoning, not a break,
    /// and it stays that way — an application that reconnects on no evidence
    /// is one whose reconnects nobody believes.
    /// </summary>
    public bool IsBreak => From is SessionState.Connected && To is SessionState.Failed;

    /// <summary>
    /// Whether an attempt failed without getting anywhere. On its own this is
    /// not a reason to start reconnecting — somebody is watching, and the
    /// failure is already on screen with a button under it. Inside a sequence
    /// that a break has already started it is the ordinary case, because a
    /// server that is rebooting refuses connections for a while before it
    /// accepts one.
    /// </summary>
    public bool IsAttemptFailure => From is SessionState.Connecting && To is SessionState.Failed;

    /// <summary>
    /// Whether the far end refused the sign-in. Winlogon narrating itself does
    /// not count — see <see cref="SessionSignalRouter.IsWinlogonNotice"/>,
    /// which is where the trap about negative codes is written down.
    /// </summary>
    public bool IsRefusal =>
        LogonError is { } code && !SessionSignalRouter.IsWinlogonNotice(code);

    /// <summary>
    /// Reads an ending off a transition. The logon error has to be supplied
    /// because a transition does not carry one.
    /// </summary>
    public static SessionEnding For(SessionStateChangedEventArgs change, int? logonError = null)
    {
        ArgumentNullException.ThrowIfNull(change);

        return new SessionEnding
        {
            From = change.PreviousState,
            To = change.State,
            LogonError = logonError,
        };
    }
}
