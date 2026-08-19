using System.Globalization;

namespace Patchbay.Core.Sessions;

/// <summary>What became of one write (M4-04).</summary>
public enum RdpSettingOutcome
{
    /// <summary>Written.</summary>
    Applied = 0,

    /// <summary>
    /// This control generation has no such property, or no such settings
    /// object. Not a fault: a control from 2008 was never going to have every
    /// setting a control from 2022 has.
    /// </summary>
    Unsupported = 1,

    /// <summary>
    /// The property is there and the control refused the value. A different
    /// thing entirely, and usually a sign the value is wrong rather than the
    /// control being old.
    /// </summary>
    Rejected = 2,
}

/// <summary>One line of the report: a write, and what happened to it.</summary>
public sealed record RdpSettingReport
{
    public required RdpSettingWrite Write { get; init; }

    public required RdpSettingOutcome Outcome { get; init; }

    /// <summary>What the control said. Null when nothing went wrong.</summary>
    public string? Message { get; init; }

    /// <summary>The name that was actually used, which may be an older alias.</summary>
    public string? UsedName { get; init; }

    public bool IsFailure => Outcome is not RdpSettingOutcome.Applied;

    /// <summary>
    /// Whether this failure changed what the session is. See
    /// <see cref="RdpSettingWrite.IsMaterial"/> — most failures do not, and
    /// treating them all alike buries the ones that do.
    /// </summary>
    public bool Matters => IsFailure && Write.IsMaterial;

    public override string ToString() => $"{Outcome}: {Write}";
}

/// <summary>
/// What happened when a plan met a control (M4-04).
///
/// <para>
/// Applying settings is not all-or-nothing and should not pretend to be. A
/// control that will not take a colour depth is still a control worth
/// connecting, and refusing to open the session over it would be a worse
/// outcome than the one being avoided. So every write is attempted, every
/// result is kept, and the question the caller actually has —
/// <see cref="IsSafe"/> — is answered separately from "did everything work".
/// </para>
///
/// <para>
/// The distinction is the point. A resolution that did not apply announces
/// itself the moment the session draws. A clipboard redirection that did not
/// get turned off announces itself never, and somebody carries on believing
/// the opposite of what is true.
/// </para>
/// </summary>
public sealed record RdpSettingsReport
{
    /// <summary>Nothing attempted. What an empty plan produces.</summary>
    public static RdpSettingsReport Empty { get; } = new() { Entries = [] };

    public required IReadOnlyList<RdpSettingReport> Entries { get; init; }

    /// <summary>Every write landed.</summary>
    public bool IsClean => !Entries.Any(e => e.IsFailure);

    /// <summary>
    /// True when nothing that failed changed what the session is. A report can
    /// be unclean and still safe, and that is the ordinary case on an older
    /// control.
    /// </summary>
    public bool IsSafe => !Entries.Any(e => e.Matters);

    /// <summary>The failures worth showing somebody, in plan order.</summary>
    public IReadOnlyList<RdpSettingReport> Concerns => [.. Entries.Where(e => e.Matters)];

    /// <summary>Everything that failed, including the failures nobody needs to hear about.</summary>
    public IReadOnlyList<RdpSettingReport> Failures => [.. Entries.Where(e => e.IsFailure)];

    /// <summary>
    /// A sentence for the notice bar, or null when there is nothing to say.
    /// Deliberately silent about the failures that do not matter: a warning
    /// shown every time an older control declines a colour depth is a warning
    /// people learn to dismiss without reading, and then the one that mattered
    /// goes with it.
    /// </summary>
    public string? Notice
    {
        get
        {
            IReadOnlyList<RdpSettingReport> concerns = Concerns;

            if (concerns.Count == 0)
            {
                return null;
            }

            string what = string.Join(", ", concerns.Select(c => Uncapitalise(c.Write.Purpose)));

            return string.Create(
                CultureInfo.CurrentCulture,
                $"This RDP control would not accept {concerns.Count} of the settings for this "
                + $"connection, and the session is running without them: {what}.");
        }
    }

    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture,
        $"{nameof(RdpSettingsReport)} {{ Applied = {Entries.Count - Failures.Count}, "
        + $"Failed = {Failures.Count}, Concerns = {Concerns.Count} }}");

    /// <summary>
    /// Drops a purpose into the middle of a sentence. The first letter only —
    /// lowercasing the whole phrase turns "Signing in as the current Windows
    /// user" into a sentence with a proper noun missing from it.
    /// </summary>
    private static string Uncapitalise(string purpose) => purpose.Length == 0
        ? purpose
        : string.Concat(char.ToLowerInvariant(purpose[0]).ToString(), purpose.AsSpan(1));
}
