namespace Patchbay.Core.Sessions;

/// <summary>
/// One announcement from the control, on its way to a
/// <see cref="SessionSignalRouter"/>.
///
/// It lives in <c>Core</c> so that the hosting control and the thing that
/// interprets it agree on a vocabulary without the interpreter having to know
/// what an ActiveX control is.
/// </summary>
public sealed class SessionSignalEventArgs : EventArgs
{
    public required SessionSignal Signal { get; init; }

    /// <summary>
    /// The number that came with it — a disconnect reason, a logon error, or a
    /// control error code, depending on <see cref="Signal"/>. Zero for the
    /// signals that carry nothing.
    ///
    /// Three separate number spaces sharing one field is not tidy, but it is
    /// what the control does, and inventing a union here would only move the
    /// ambiguity somewhere less obvious.
    /// </summary>
    public int Code { get; init; }

    /// <summary>
    /// The detail that comes with <see cref="SessionSignal.Reconnecting"/>,
    /// and with nothing else. Four numbers would not fit in
    /// <see cref="Code"/>, and the alternative — a second event, a second
    /// delegate, a second subscription — would be more plumbing than the one
    /// signal that needs it is worth.
    /// </summary>
    public SessionReconnectNotice? Reconnect { get; init; }
}
