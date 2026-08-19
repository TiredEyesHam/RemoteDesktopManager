namespace Patchbay.Core.Sessions;

/// <summary>
/// Whether a session that has just ended should be brought back (M4-08).
///
/// A pure function of an ending, a policy and a count, which is the whole
/// point: the timing lives in <see cref="ReconnectController"/> and the
/// connecting lives in the shell, and neither of those can be tested without a
/// clock or a window. This can, and it is the half where being wrong is
/// expensive.
///
/// <para>
/// <b>What starts a sequence is narrow, and what continues one is wide.</b>
/// Only a working session breaking starts one — see
/// <see cref="SessionEnding.IsBreak"/> for why that and nothing else. Once one
/// is running, an attempt that fails outright continues it, because a machine
/// that is rebooting refuses connections for a minute or two before it starts
/// accepting them, and a sequence that stopped at the first refusal would give
/// up precisely where it was needed. Getting these two the same way round is
/// the difference between an application that survives a reboot and one that
/// retries a hostname that has never resolved.
/// </para>
///
/// <para>
/// <b>Nothing that a person decided is ever chased.</b> A disconnect somebody
/// asked for, an attempt somebody called off, a sign-in the far end refused —
/// all three stop the sequence wherever it had got to.
/// </para>
/// </summary>
public static class ReconnectRules
{
    /// <summary>
    /// Decides what happens after <paramref name="ending"/>.
    /// </summary>
    /// <param name="policy">The connection's settings.</param>
    /// <param name="ending">How the session stopped.</param>
    /// <param name="attempts">
    /// How many automatic attempts this sequence has already made. Zero means
    /// there is no sequence yet, and the ending has to earn one.
    /// </param>
    /// <param name="sample">Where in the jitter range to land — see <see cref="ReconnectPolicy.Delay"/>.</param>
    public static ReconnectDecision Decide(
        ReconnectPolicy policy,
        SessionEnding ending,
        int attempts,
        double sample = 0.5)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentOutOfRangeException.ThrowIfNegative(attempts);

        // Not over. A session on its way down has not ended yet, and one that
        // is connecting has not either.
        if (!ending.IsEnded)
        {
            return ReconnectDecision.No(ReconnectVerdict.NotAnInterruption);
        }

        // Checked before anything else, so that turning it off is exactly as
        // absolute as it sounds.
        if (!policy.Enabled)
        {
            return ReconnectDecision.No(ReconnectVerdict.Disabled);
        }

        if (ending.WasCalledOff)
        {
            return ReconnectDecision.No(ReconnectVerdict.NotAnInterruption);
        }

        // Ahead of the counting, so that a sequence already in flight stops
        // here too. That is the case this rule exists for: the first reconnect
        // after a drop reaches a machine whose password has since changed, and
        // without this the remaining nine attempts lock the account out
        // without anybody touching a keyboard.
        if (ending.IsRefusal)
        {
            return ReconnectDecision.No(ReconnectVerdict.Refused);
        }

        bool worthChasing = attempts == 0
            ? ending.IsBreak
            : ending.IsBreak || ending.IsAttemptFailure;

        if (!worthChasing)
        {
            return ReconnectDecision.No(ReconnectVerdict.NotAnInterruption);
        }

        if (attempts >= policy.AttemptLimit)
        {
            return ReconnectDecision.No(ReconnectVerdict.Exhausted);
        }

        int attempt = attempts + 1;

        return new ReconnectDecision
        {
            Verdict = ReconnectVerdict.Retry,
            Attempt = attempt,
            Delay = policy.Delay(attempt, sample),
        };
    }
}
