using Patchbay.Core.Model;

namespace Patchbay.Core.Sessions;

/// <summary>
/// How hard, and how patiently, Patchbay tries to bring a dropped session back
/// (M4-08).
///
/// The shape of the answer is a backoff rather than a fixed interval, and the
/// reason is not politeness to the server: it is that the two things that
/// break a session recover on wildly different timescales. A wireless network
/// that dropped a packet is back in a second; a server that is rebooting is
/// back in three minutes; a gateway that is being patched is back in ten. A
/// fixed one-second retry answers the first case and spends six hundred
/// pointless attempts on the third, and a fixed one-minute retry answers the
/// third while making the first feel broken.
///
/// <para>
/// <b>Jitter is not decoration.</b> A gateway restarting takes every session
/// through it down at the same instant, and without a spread they all come
/// back at the same instant too — repeatedly, in lockstep, because the
/// backoff is deterministic and they all started together. That is a small
/// denial of service aimed at the machine that has just come back up. The
/// spread is supplied as a sample rather than drawn here so that the whole of
/// this type stays a function of its inputs and can be tested at both ends of
/// its range.
/// </para>
/// </summary>
public sealed record ReconnectPolicy
{
    private readonly TimeSpan _firstDelay = TimeSpan.FromSeconds(5);
    private readonly TimeSpan _maxDelay = TimeSpan.FromSeconds(60);
    private readonly double _factor = 2.0;
    private readonly int _attemptLimit = 10;
    private readonly double _jitter = 0.2;

    /// <summary>Patchbay's own settings, as shipped.</summary>
    public static ReconnectPolicy Default { get; } = new();

    /// <summary>Never reconnect anything. What a connection with the setting turned off gets.</summary>
    public static ReconnectPolicy Off { get; } = new() { Enabled = false };

    /// <summary>Whether to reconnect at all.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// How long to wait before the first attempt. Long enough that a session
    /// dropped by a deliberate reboot is not chased into a machine that is
    /// still shutting down, short enough that a passing blip is invisible.
    /// </summary>
    public TimeSpan FirstDelay
    {
        get => _firstDelay;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
            _firstDelay = value;
        }
    }

    /// <summary>
    /// The ceiling on the wait. Bounds the <em>base</em> delay rather than the
    /// final one: jitter is applied afterwards and is allowed to carry a
    /// session past the ceiling, because clamping at the top is precisely
    /// where the spread is most needed and least present.
    /// </summary>
    public TimeSpan MaxDelay
    {
        get => _maxDelay;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
            _maxDelay = value;
        }
    }

    /// <summary>What each wait is multiplied by. One means a fixed interval.</summary>
    public double Factor
    {
        get => _factor;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1.0);
            _factor = value;
        }
    }

    /// <summary>
    /// How many attempts a single sequence may make before giving up. Zero
    /// means none, which is a different thing from <see cref="Enabled"/> being
    /// false only in that it is reported as having given up rather than as
    /// having been switched off.
    ///
    /// Ten attempts at the shipped delays is a little over seven minutes,
    /// which covers a reboot with room to spare and stops well short of an
    /// application that hammers a decommissioned server until somebody
    /// notices.
    /// </summary>
    public int AttemptLimit
    {
        get => _attemptLimit;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _attemptLimit = value;
        }
    }

    /// <summary>
    /// How far either side of the base delay a wait may be moved, as a
    /// fraction of it. Zero is exact and reproducible; the shipped fifth is
    /// enough to take a room full of sessions out of step with each other.
    /// </summary>
    public double Jitter
    {
        get => _jitter;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 1.0);
            _jitter = value;
        }
    }

    /// <summary>
    /// The policy for one connection. Only the switch comes from the document:
    /// how long to wait and how often is a preference about Patchbay rather
    /// than a fact about a server, and putting it in the connection file would
    /// mean answering it once per machine.
    /// </summary>
    public static ReconnectPolicy For(ConnectionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        // Null is inherit, and by the time a request exists the resolver has
        // been through it — but a request built by hand has not, and defaulting
        // to on matches ConnectionSettings.Defaults.
        return Default with { Enabled = settings.AutoReconnect ?? true };
    }

    /// <summary>
    /// How long to wait before <paramref name="attempt"/>, counting from one.
    /// </summary>
    /// <param name="sample">
    /// Where in the jitter range to land, from 0 for the earliest to 1 for the
    /// latest. The default sits exactly on the base delay, so a caller that
    /// does not care about spreading gets the arithmetic and nothing else.
    /// </param>
    public TimeSpan Delay(int attempt, double sample = 0.5)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(attempt, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(sample);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(sample, 1.0);

        // Clamped before anything else is done with it. A factor of two at the
        // fortieth attempt overflows a double to infinity, and
        // TimeSpan.FromSeconds(infinity) throws — which would turn a session
        // nobody was watching into a crash.
        double seconds = Math.Min(
            FirstDelay.TotalSeconds * Math.Pow(Factor, attempt - 1),
            MaxDelay.TotalSeconds);

        double offset = seconds * Jitter * ((sample * 2.0) - 1.0);

        return TimeSpan.FromSeconds(Math.Max(0.0, seconds + offset));
    }
}
