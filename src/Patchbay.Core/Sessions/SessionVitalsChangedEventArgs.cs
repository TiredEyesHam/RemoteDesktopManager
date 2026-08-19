namespace Patchbay.Core.Sessions;

/// <summary>
/// A session's readings changed (M5-17).
///
/// Separate from <see cref="SessionStateChangedEventArgs"/> because the two
/// keep different time. A state change is rare and always interesting;
/// latency moves while nothing else does, and once the probe lands (M5-18) it
/// will move often. Folding it into the state event would mean announcing a
/// transition that did not happen every time a number ticked.
/// </summary>
public sealed class SessionVitalsChangedEventArgs : EventArgs
{
    public required SessionVitals Vitals { get; init; }
}
