namespace Patchbay.Core.Sessions;

/// <summary>
/// Which object on the RDP control a setting is written to (M4-04).
///
/// The control is not one property bag. It has several settings objects
/// hanging off it, each a different generation of a different interface, and
/// putting a property on the wrong one is a run-time miss rather than a
/// compile error.
/// </summary>
public enum RdpSettingTarget
{
    /// <summary>The control itself — <c>Server</c>, <c>UserName</c>, the desktop size.</summary>
    Client = 0,

    /// <summary>
    /// <c>AdvancedSettings</c> through <c>AdvancedSettings9</c>. Most of the
    /// property surface, and the numbering is off by one from the interfaces.
    /// </summary>
    AdvancedSettings = 1,

    /// <summary><c>SecuredSettings</c> through <c>SecuredSettings3</c>. Audio and the keyboard hook.</summary>
    SecuredSettings = 2,

    /// <summary><c>TransportSettings</c> through <c>TransportSettings4</c>. The gateway lives here and nowhere else.</summary>
    TransportSettings = 3,
}

/// <summary>
/// One property to write on the control, and enough context to explain it if
/// the write does not happen (M4-04).
///
/// Every write goes out late-bound, so the compiler cannot check any of it. A
/// list can be checked, and built and tested on a machine with no RDP control
/// on it — which is where the interesting mistakes live.
/// </summary>
public sealed record RdpSettingWrite
{
    /// <summary>Which settings object this goes on.</summary>
    public required RdpSettingTarget Target { get; init; }

    /// <summary>The property name to try first.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// Older names for the same idea, tried in order when <see cref="Name"/>
    /// is not there. Microsoft renamed a few settings between generations and
    /// kept both.
    /// </summary>
    public IReadOnlyList<string> Alternatives { get; init; } = [];

    /// <summary>What to write. Already the shape the control wants.</summary>
    public required object Value { get; init; }

    /// <summary>
    /// The model property this came from, so a failure traces back to
    /// something someone typed rather than to a name only Patchbay knows.
    /// </summary>
    public required string Setting { get; init; }

    /// <summary>
    /// What the setting does, as a capitalised noun phrase that reads in
    /// "&lt;Purpose&gt; could not be applied". Written out rather than derived
    /// from the property name, because "RDPPort" is not a sentence.
    /// </summary>
    public required string Purpose { get; init; }

    /// <summary>
    /// Whether failing to write this leaves the session less restricted, or
    /// pointed somewhere else, than was asked for.
    ///
    /// Not the same as "important". A resolution that did not apply is visible
    /// the instant the session draws; a clipboard redirection that did not get
    /// turned off is invisible, and the person carries on believing the
    /// opposite of what is true. So turning a redirection off is material and
    /// turning it on is not.
    /// </summary>
    public bool IsMaterial { get; init; }

    /// <summary>
    /// Whether <see cref="Value"/> is a secret and must not be printed
    /// (M4-10).
    ///
    /// A plan gets inspected, printed in a harness, shown when the control
    /// refuses something, and eventually written to a log. The entry carries
    /// the answer so that no printer has to know which names are dangerous.
    ///
    /// This hides the value from anything that formats a write. It does not
    /// make the value safe in memory — the control takes its password as a
    /// BSTR, so cleartext exists at connect time regardless. That is M3-03.
    /// </summary>
    public bool IsSecret { get; init; }

    /// <summary>Every name to try, best first.</summary>
    public IEnumerable<string> Candidates => [Name, .. Alternatives];

    /// <summary>Fixed width, so the length does not leak either.</summary>
    private const string Redacted = "••••••••";

    public override string ToString() => $"{Target}.{Name} = {(IsSecret ? Redacted : Value)}";
}
