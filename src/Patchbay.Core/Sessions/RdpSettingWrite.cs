namespace Patchbay.Core.Sessions;

/// <summary>
/// Which object on the RDP control a setting is written to (M4-04).
///
/// The control is not one object with one property bag. It is a control with a
/// handful of settings objects hanging off it, each a different generation of a
/// different interface, and putting a property on the wrong one is a run-time
/// miss rather than a compile error. Naming the target is how that mistake is
/// made visible in a table instead of buried in a call.
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
/// One property to write on the control, and everything needed to explain it
/// if the write does not happen (M4-04).
///
/// <para>
/// <b>Why a description rather than a call.</b> Every write goes out
/// late-bound, so nothing here can be checked by the compiler; what can be
/// checked is a list. A plan can be built, inspected and tested on a machine
/// with no RDP control on it at all, which is where the interesting mistakes
/// live — a setting mapped to the wrong object, a gateway mode written as the
/// number that means the opposite, a redirection that quietly never gets sent.
/// </para>
/// </summary>
public sealed record RdpSettingWrite
{
    /// <summary>Which settings object this goes on.</summary>
    public required RdpSettingTarget Target { get; init; }

    /// <summary>The property name to try first.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// Older names for the same idea, tried in order when <see cref="Name"/> is
    /// not there. Microsoft renamed a few settings between generations and kept
    /// both, so this is how one entry covers a control from 2006 and one from
    /// last year without a per-generation table.
    /// </summary>
    public IReadOnlyList<string> Alternatives { get; init; } = [];

    /// <summary>What to write. Already the shape the control wants.</summary>
    public required object Value { get; init; }

    /// <summary>
    /// The model property this came from, so a failure can be traced back to
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
    /// Whether failing to write this leaves the session <em>less restricted</em>
    /// or <em>pointed somewhere else</em> than was asked for.
    ///
    /// <para>
    /// This is the distinction the whole report turns on, and it is not the
    /// same as "important". A resolution that did not apply is visible the
    /// instant the session draws. A clipboard redirection that did not get
    /// turned off is invisible, and the person carries on believing the
    /// opposite of what is true. So turning a redirection <b>off</b> is
    /// material and turning it <b>on</b> is not; a gateway that did not apply
    /// is material, because the session either fails or quietly goes direct to
    /// a machine somebody meant to reach through a gateway.
    /// </para>
    /// </summary>
    public bool IsMaterial { get; init; }

    /// <summary>
    /// Whether <see cref="Value"/> is a secret, and must not be printed
    /// (M4-10).
    ///
    /// <para>
    /// A plan is a diagnostic object. It is built to be inspected, printed in
    /// a harness, shown when the control refuses something and — once M4-16
    /// lands — written to a log file that gets attached to support tickets.
    /// Every one of those is a place a password must not appear, and the
    /// mistake is not one anybody would make deliberately: it is made by
    /// adding one more entry to a table where every other entry is safe to
    /// print. So the entry carries the answer rather than each printer having
    /// to know which names are dangerous.
    /// </para>
    ///
    /// <para>
    /// This hides the value from anything that formats a write. It does not
    /// make the value safe in memory — the control takes its password as a
    /// BSTR, so cleartext exists at connect time whatever anyone does, which
    /// is M3-03's problem and not solvable here.
    /// </para>
    /// </summary>
    public bool IsSecret { get; init; }

    /// <summary>Every name to try, best first.</summary>
    public IEnumerable<string> Candidates => [Name, .. Alternatives];

    /// <summary>What to print instead of a secret. Fixed width, so the length does not leak either.</summary>
    private const string Redacted = "••••••••";

    public override string ToString() => $"{Target}.{Name} = {(IsSecret ? Redacted : Value)}";
}
