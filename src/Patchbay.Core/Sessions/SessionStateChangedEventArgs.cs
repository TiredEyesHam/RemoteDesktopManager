namespace Patchbay.Core.Sessions;

/// <summary>
/// A session moved. Carries where it came from as well as where it is, because
/// the interesting transitions are pairs: Connecting → Failed wants an error
/// on screen, Connected → Disconnected wants a reconnect offer, and
/// Connecting → Disconnected is someone changing their mind and wants neither.
/// </summary>
public sealed class SessionStateChangedEventArgs : EventArgs
{
    public required SessionState PreviousState { get; init; }

    public required SessionState State { get; init; }

    /// <summary>
    /// A sentence for the status bar, already fit to show someone. Null when
    /// the transition speaks for itself.
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// What went wrong, when <see cref="State"/> is
    /// <see cref="SessionState.Failed"/>. For logging and the details
    /// expander — <see cref="Message"/> is what gets shown first.
    /// </summary>
    public Exception? Error { get; init; }
}
