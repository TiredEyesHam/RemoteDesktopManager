namespace Patchbay.Core.Sessions;

/// <summary>
/// How much attention a status field's value is asking for (M5-17).
///
/// <para>
/// <see cref="Muted"/> carries meaning and is not merely a lighter colour: it
/// says <em>this is what was configured, not what happened</em>. A resolution
/// nobody has negotiated yet and a gateway that may or may not have been used
/// are both shown muted, so that the moment a value becomes a fact is visible
/// without reading the label.
/// </para>
/// </summary>
public enum SessionStatusTone
{
    /// <summary>A fact about the live session, and nothing to say about it.</summary>
    Normal = 0,

    /// <summary>Not known, or known only as an intention.</summary>
    Muted = 1,

    /// <summary>Worth noticing. Weaker than asked for, or slow enough to feel.</summary>
    Warn = 2,

    /// <summary>Worth doing something about.</summary>
    Bad = 3,
}

/// <summary>
/// One thing the status bar says about a session: a label, a value, how much
/// attention it wants, and a sentence explaining it for whoever hovers.
///
/// The detail is not decoration. "TLS" means nothing to most people looking at
/// it, and a status bar that shows a security layer without ever explaining
/// what a weaker one costs has told them a word rather than a fact.
/// </summary>
public sealed record SessionStatusField
{
    /// <summary>What the value is. Short: this sits in a 28-pixel strip.</summary>
    public required string Label { get; init; }

    /// <summary>The value, already formatted. Never empty — see <see cref="SessionStatusLine"/>.</summary>
    public required string Value { get; init; }

    /// <summary>How much attention it is asking for.</summary>
    public SessionStatusTone Tone { get; init; } = SessionStatusTone.Normal;

    /// <summary>A sentence for the tooltip. Null when the value speaks for itself.</summary>
    public string? Detail { get; init; }

    public override string ToString() => $"{Label}: {Value}";
}
