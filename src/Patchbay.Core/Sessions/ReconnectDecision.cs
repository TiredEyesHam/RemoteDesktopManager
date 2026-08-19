namespace Patchbay.Core.Sessions;

/// <summary>
/// What became of a chance to reconnect (M4-08).
///
/// Five of the six are ways of saying no, and they are kept apart rather than
/// collapsed into a bool because the person is owed different sentences. "The
/// sign-in was refused" and "gave up after ten attempts" describe the same
/// silence and call for opposite next moves.
/// </summary>
public enum ReconnectVerdict
{
    /// <summary>
    /// Nothing about this ending calls for a reconnect: it was ordinary, or
    /// somebody asked for it. The resting answer, and deliberately the zero.
    /// </summary>
    NotAnInterruption = 0,

    /// <summary>Wait, then try again.</summary>
    Retry = 1,

    /// <summary>Auto-reconnect is switched off for this connection.</summary>
    Disabled = 2,

    /// <summary>
    /// The far end refused the sign-in. Trying again submits the same wrong
    /// credentials to the same account, and enough of that locks it out —
    /// which is a failure Patchbay would have caused rather than reported.
    /// </summary>
    Refused = 3,

    /// <summary>The attempt limit is used up.</summary>
    Exhausted = 4,

    /// <summary>
    /// Someone stopped the countdown. Never returned by
    /// <see cref="ReconnectRules"/>, which only ever looks at an ending —
    /// this one belongs to <see cref="ReconnectController"/>, because a person
    /// interrupting is the fifth way a sequence ends and the status line still
    /// has to say what happened.
    /// </summary>
    Cancelled = 5,
}

/// <summary>
/// The answer, and what to do with it (M4-08).
/// </summary>
public readonly record struct ReconnectDecision
{
    public required ReconnectVerdict Verdict { get; init; }

    /// <summary>
    /// Which attempt this would be, counting from one. Zero when there is not
    /// going to be one.
    /// </summary>
    public int Attempt { get; init; }

    /// <summary>How long to wait first. Zero when there is nothing to wait for.</summary>
    public TimeSpan Delay { get; init; }

    public bool ShouldRetry => Verdict is ReconnectVerdict.Retry;

    /// <summary>A refusal, with nothing to wait for and nothing to count.</summary>
    public static ReconnectDecision No(ReconnectVerdict verdict) => new() { Verdict = verdict };

    public override string ToString() => ShouldRetry
        ? $"{Verdict} #{Attempt} in {Delay}"
        : Verdict.ToString();
}
