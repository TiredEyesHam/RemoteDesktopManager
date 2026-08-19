using System.Globalization;
using System.Text;

namespace Patchbay.Rdp.Interop;

/// <summary>
/// The RDP control Patchbay settled on, and what it turned out to support.
///
/// Produced by <see cref="RdpEngineProbe"/> by creating the thing and asking
/// it, never by reading a version number off the registry. On the machine this
/// was written on, the newest registered coclass could not be created at all
/// while four older ones all handed out the same interfaces — so neither
/// "highest number wins" nor "the name tells you the generation" survives
/// contact with a real installation.
/// </summary>
public sealed record RdpEngineInfo
{
    /// <summary>Programmatic id of the coclass that worked, e.g. <c>MsTscAx.MsTscAx.12</c>.</summary>
    public required string ProgId { get; init; }

    /// <summary>Class id actually passed to COM.</summary>
    public required Guid ClassId { get; init; }

    /// <summary>The newest scriptable interface the control answered to.</summary>
    public required RdpClientLevel Level { get; init; }

    /// <summary>The newest non-scriptable interface it answered to.</summary>
    public required RdpNonScriptableLevel NonScriptableLevel { get; init; }

    /// <summary>The DLL behind the registration, when it could be read.</summary>
    public string? ModulePath { get; init; }

    /// <summary>File version of <see cref="ModulePath"/>, when it could be read.</summary>
    public string? ModuleVersion { get; init; }

    /// <summary>
    /// Whether credentials can be handed to this control at all. False means
    /// every connection will prompt for a password itself, which is worth
    /// telling someone before they wonder why their saved credentials are
    /// being ignored (M3-02, M4-10).
    /// </summary>
    public bool SupportsCredentialInjection => NonScriptableLevel != RdpNonScriptableLevel.None;

    /// <summary>One line for a title bar, an about box or a log.</summary>
    public string Description => string.Create(
        CultureInfo.InvariantCulture,
        $"{ProgId} (IMsRdpClient level {(int)Level}{(ModuleVersion is null ? string.Empty : $", mstscax {ModuleVersion}")})");

    public override string ToString() => Description;
}

/// <summary>What happened when the probe tried one particular coclass.</summary>
public sealed record RdpProbeAttempt
{
    public required string ProgId { get; init; }

    public required Guid ClassId { get; init; }

    /// <summary>Null when the attempt succeeded; otherwise why it did not.</summary>
    public string? Failure { get; init; }

    /// <summary>The level reached, which is <see cref="RdpClientLevel.None"/> if it never got that far.</summary>
    public RdpClientLevel Level { get; init; }

    public bool Succeeded => Failure is null;
}

/// <summary>
/// The full record of a probe: what was chosen, and what every rejected
/// candidate did.
///
/// The rejects are kept because this is the failure people will report from
/// machines nobody here can log into, and "no RDP client found" on its own is
/// not something anyone can act on. Six lines saying which class ids were
/// tried and which HRESULT each returned usually names the cause outright —
/// a policy-blocked ActiveX registration, a 32-bit-only install, a control
/// present but not creatable.
/// </summary>
public sealed record RdpProbeResult
{
    /// <summary>The control that will be used, or null when none was usable.</summary>
    public required RdpEngineInfo? Engine { get; init; }

    /// <summary>Every candidate tried, newest first, in the order tried.</summary>
    public required IReadOnlyList<RdpProbeAttempt> Attempts { get; init; }

    public bool IsAvailable => Engine is not null;

    /// <summary>
    /// A multi-line report for the log (M0-07) and for a support bundle.
    /// Safe to include verbatim: it names class ids and file versions, never
    /// a hostname or a credential.
    /// </summary>
    public string Describe()
    {
        StringBuilder report = new();

        report.AppendLine(Engine is null
            ? "No usable RDP control was found. Tried, newest first:"
            : string.Create(CultureInfo.InvariantCulture, $"Using {Engine.Description}. Tried, newest first:"));

        foreach (RdpProbeAttempt attempt in Attempts)
        {
            report.AppendLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  {attempt.ProgId} {{{attempt.ClassId}}} — {attempt.Failure ?? $"ok, level {(int)attempt.Level}"}"));
        }

        if (Engine is { SupportsCredentialInjection: false })
        {
            report.AppendLine(
                "  note: this control exposes no non-scriptable interface, so it will ask for "
                + "passwords itself and ignore any Patchbay supplies.");
        }

        return report.ToString().TrimEnd();
    }
}
