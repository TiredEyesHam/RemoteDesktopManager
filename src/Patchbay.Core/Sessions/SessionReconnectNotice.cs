namespace Patchbay.Core.Sessions;

/// <summary>
/// The control rejoining a session it briefly lost, as it describes itself
/// (M4-08).
///
/// This is the RDP control's <em>own</em> reconnect, and it is a different and
/// better thing from Patchbay's. It holds an auto-reconnect cookie issued when
/// the session was established, so what it rejoins is the same session —
/// desktop, open windows, half-typed command and all — where a fresh connect
/// would get a new one. It only reaches the case where the transport went away
/// for a moment and came back, which is why there is a second layer above it
/// for a reboot, a gateway restart, or a laptop that was shut for an hour.
///
/// Nothing here changes what state a session is in, and that is the point: the
/// session has not ended. It is being held open, and this is what to say to
/// somebody watching a picture that has stopped moving.
/// </summary>
public readonly record struct SessionReconnectNotice
{
    /// <summary>Which attempt the control is on, counting from one.</summary>
    public required int Attempt { get; init; }

    /// <summary>
    /// How many it will make before giving up and reporting an ordinary
    /// disconnect. The control's own <c>MaxReconnectAttempts</c>, which
    /// defaults to five.
    /// </summary>
    public required int MaxAttempts { get; init; }

    /// <summary>
    /// Whether <em>this</em> computer has no network. Stated the way round
    /// that makes the ordinary case the default, and worth showing because it
    /// is the one form of the problem the person can do something about.
    /// </summary>
    public bool NetworkLost { get; init; }

    /// <summary>Why the session dropped, in the disconnect-reason numbering.</summary>
    public int DisconnectReason { get; init; }

    public override string ToString() => $"attempt {Attempt} of {MaxAttempts}";
}
