using System.Globalization;

namespace Patchbay.Core.Sessions;

/// <summary>
/// One session's reconnect sequence: how many attempts it has made, how long
/// until the next, and what to say about it (M4-08).
///
/// <para>
/// <b>It does not own a clock.</b> Time arrives through <see cref="Tick"/>,
/// which is what makes the countdown testable at all — every interesting case
/// here involves waiting a minute, and a test suite that actually waited would
/// take an hour. It also happens to be what the shell wants: a visible
/// countdown has to be redrawn on the thread that draws, so the timer belongs
/// where the drawing is and the arithmetic belongs here.
/// </para>
///
/// <para>
/// <b>The counter resets on success, not on time.</b> A session that drops
/// once a fortnight and comes back every time has made one attempt, over and
/// over, not thirty-six — and a counter that only ever went up would
/// eventually declare a perfectly healthy connection exhausted.
/// </para>
///
/// <para>
/// <b>Threading.</b> Belongs to the thread that made it, like the session it
/// follows.
/// </para>
/// </summary>
public sealed class ReconnectController
{
    private readonly Func<double> _spread;

    /// <param name="policy">The connection's settings. Defaults to the shipped ones.</param>
    /// <param name="spread">
    /// Where in the jitter range each wait should land, from 0 to 1. Random by
    /// default, which is the entire point of it; tests pin it.
    /// </param>
    public ReconnectController(ReconnectPolicy? policy = null, Func<double>? spread = null)
    {
        Policy = policy ?? ReconnectPolicy.Default;
        _spread = spread ?? Random.Shared.NextDouble;
    }

    /// <summary>The connection's settings. Reread on every ending.</summary>
    public ReconnectPolicy Policy { get; set; }

    /// <summary>How many automatic attempts this sequence has made.</summary>
    public int Attempts { get; private set; }

    /// <summary>
    /// Which attempt is being waited for, counting from one. Zero when nothing
    /// is pending.
    /// </summary>
    public int Attempt { get; private set; }

    /// <summary>How long until that attempt.</summary>
    public TimeSpan Remaining { get; private set; }

    /// <summary>What was decided about the last ending.</summary>
    public ReconnectVerdict Verdict { get; private set; } = ReconnectVerdict.NotAnInterruption;

    /// <summary>Whether a countdown is running.</summary>
    public bool IsWaiting => Verdict is ReconnectVerdict.Retry && Remaining > TimeSpan.Zero;

    /// <summary>
    /// Whether a sequence is under way — either counting down, or waiting on
    /// the attempt it has just released. What the cancel button hangs off.
    /// </summary>
    public bool IsRunning => Verdict is ReconnectVerdict.Retry;

    /// <summary>
    /// What to tell the person, or null when there is nothing to add to what
    /// the session itself already said.
    /// </summary>
    public string? Summary => Verdict switch
    {
        ReconnectVerdict.Retry when IsWaiting => string.Create(
            CultureInfo.InvariantCulture,
            $"Reconnecting in {Seconds()} s — attempt {Attempt} of {Policy.AttemptLimit}"),

        ReconnectVerdict.Retry => string.Create(
            CultureInfo.InvariantCulture,
            $"Reconnecting — attempt {Attempt} of {Policy.AttemptLimit}"),

        ReconnectVerdict.Exhausted when Attempts == 0 =>
            "Not reconnecting: no attempts are allowed.",

        ReconnectVerdict.Exhausted => string.Create(
            CultureInfo.InvariantCulture,
            $"Gave up reconnecting after {Attempts} attempts."),

        ReconnectVerdict.Refused => "Not reconnecting: the sign-in was refused.",

        ReconnectVerdict.Cancelled => "Reconnecting cancelled.",

        _ => null,
    };

    /// <summary>
    /// Takes an ending and starts a countdown if one is called for. Returns
    /// what was decided, so a caller that wants to log or explain it can.
    /// </summary>
    public ReconnectDecision Ended(SessionEnding ending)
    {
        ReconnectDecision decision = ReconnectRules.Decide(Policy, ending, Attempts, _spread());

        Verdict = decision.Verdict;
        Attempt = decision.Attempt;
        Remaining = decision.Delay;

        return decision;
    }

    /// <summary>
    /// Moves the countdown on. Returns true exactly once per attempt, at the
    /// moment the caller should connect — and counts the attempt as made,
    /// because from here on nothing else will.
    /// </summary>
    /// <param name="elapsed">Real time since the last tick.</param>
    public bool Tick(TimeSpan elapsed)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(elapsed, TimeSpan.Zero);

        if (!IsWaiting)
        {
            return false;
        }

        Remaining -= elapsed;

        if (Remaining > TimeSpan.Zero)
        {
            return false;
        }

        Remaining = TimeSpan.Zero;
        Attempts = Attempt;

        return true;
    }

    /// <summary>
    /// Stops the sequence because somebody said so. Leaves the count alone:
    /// what ends the sequence is <see cref="Reset"/>, and a person stopping a
    /// countdown has not said the session is healthy again.
    /// </summary>
    public void Cancel()
    {
        if (!IsRunning)
        {
            return;
        }

        Verdict = ReconnectVerdict.Cancelled;
        Attempt = 0;
        Remaining = TimeSpan.Zero;
    }

    /// <summary>
    /// Back to nothing having happened. For a session that has connected, and
    /// for one somebody has just connected by hand — either way the sequence
    /// that was running is over and the next drop starts a fresh one.
    /// </summary>
    public void Reset()
    {
        Verdict = ReconnectVerdict.NotAnInterruption;
        Attempts = 0;
        Attempt = 0;
        Remaining = TimeSpan.Zero;
    }

    public override string ToString() => Summary ?? Verdict.ToString();

    /// <summary>
    /// The countdown, rounded up. Up rather than to nearest so that it never
    /// reads zero while it is still waiting, which looks like a stuck clock.
    /// </summary>
    private int Seconds() => (int)Math.Ceiling(Remaining.TotalSeconds);
}
