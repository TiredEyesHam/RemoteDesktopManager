namespace Patchbay.Core.Sessions;

/// <summary>
/// What the engine knows about a session that the configuration cannot say
/// (M5-17): the resolution actually negotiated, the security layer actually
/// agreed to, the gateway actually in use, and how long a round trip takes.
///
/// <para>
/// Every field here has an "I do not know" value, and every one of them means
/// it. A request holds what Patchbay asked for; this holds what happened, and
/// the two are routinely different — a server with a session-size policy hands
/// back a resolution nobody asked for, a connection configured for network
/// level authentication comes up without it, and a gateway set to be used only
/// when a direct attempt fails may or may not have been. Filling a gap here
/// with the corresponding request value would erase precisely the difference
/// worth showing.
/// </para>
///
/// <para>
/// Vitals describe a live connection and nothing else. A session that drops
/// goes back to <see cref="Unknown"/> rather than keeping its last readings,
/// because a status bar still reporting 1920 × 1080 and 24 ms about a
/// connection that ended two minutes ago is not stale, it is wrong.
/// </para>
/// </summary>
public readonly record struct SessionVitals
{
    /// <summary>Nothing known. What an unconnected session reports.</summary>
    public static SessionVitals Unknown => default;

    /// <summary>
    /// The resolution the session is running at, as negotiated.
    /// <see cref="PixelSize.Empty"/> until there is one.
    /// </summary>
    public PixelSize Resolution { get; init; }

    /// <summary>The security layer that was agreed to.</summary>
    public SessionSecurity Security { get; init; }

    /// <summary>
    /// The gateway the session is actually going through, or null for a direct
    /// connection. Null is a fact, not an absence of one — but only once
    /// connected, which is why <see cref="SessionStatusLine"/> reads it
    /// together with the state.
    /// </summary>
    public string? GatewayHostName { get; init; }

    /// <summary>
    /// Round-trip time to the far end, or null when nothing has measured it.
    /// The measuring is M5-18; this is where the answer arrives.
    /// </summary>
    public TimeSpan? Latency { get; init; }

    /// <summary>True when nothing at all is known, so a caller can skip the lot.</summary>
    public bool IsUnknown =>
        Resolution.IsEmpty
        && Security is SessionSecurity.Unknown
        && GatewayHostName is null
        && Latency is null;
}
