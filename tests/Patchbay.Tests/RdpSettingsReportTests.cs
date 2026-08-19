using Patchbay.Core.Sessions;

namespace Patchbay.Tests;

/// <summary>
/// What a half-applied plan says for itself (M4-04).
///
/// The interesting question is never "did everything work" — on an older
/// control the answer is routinely no and the session is fine. It is whether
/// anything that failed changed what the session <em>is</em>, and these are the
/// cases where the two answers differ.
/// </summary>
public class RdpSettingsReportTests
{
    private static RdpSettingWrite Cosmetic => new()
    {
        Target = RdpSettingTarget.Client,
        Name = "ColorDepth",
        Value = 32,
        Setting = "ColourDepth",
        Purpose = "The colour depth",
    };

    private static RdpSettingWrite Material => new()
    {
        Target = RdpSettingTarget.AdvancedSettings,
        Name = "RedirectClipboard",
        Value = false,
        Setting = "RedirectClipboard",
        Purpose = "Clipboard redirection",
        IsMaterial = true,
    };

    private static RdpSettingReport Entry(
        RdpSettingWrite write,
        RdpSettingOutcome outcome = RdpSettingOutcome.Applied)
        => new() { Write = write, Outcome = outcome };

    private static RdpSettingsReport Report(params RdpSettingReport[] entries)
        => new() { Entries = entries };

    // ── Clean, safe, and the gap between them ───────────────────────────

    [Fact]
    public void Everything_applied_is_clean_and_safe()
    {
        RdpSettingsReport report = Report(Entry(Cosmetic), Entry(Material));

        Assert.True(report.IsClean);
        Assert.True(report.IsSafe);
        Assert.Null(report.Notice);
    }

    [Fact]
    public void A_cosmetic_failure_is_unclean_and_still_safe()
    {
        // The ordinary case on an older control, and the reason the two
        // questions are asked separately.
        RdpSettingsReport report = Report(
            Entry(Cosmetic, RdpSettingOutcome.Unsupported),
            Entry(Material));

        Assert.False(report.IsClean);
        Assert.True(report.IsSafe);
        Assert.Null(report.Notice);
        Assert.Single(report.Failures);
        Assert.Empty(report.Concerns);
    }

    [Fact]
    public void A_redirection_that_would_not_switch_off_is_not_safe()
    {
        RdpSettingsReport report = Report(
            Entry(Cosmetic),
            Entry(Material, RdpSettingOutcome.Unsupported));

        Assert.False(report.IsSafe);
        Assert.Single(report.Concerns);
        Assert.Contains("clipboard redirection", report.Notice);
    }

    [Fact]
    public void A_rejected_write_counts_the_same_as_an_absent_one()
    {
        // The control being old and the control objecting are different
        // conversations to have with whoever is reading the log, but they
        // leave the session in the same place.
        RdpSettingsReport report = Report(Entry(Material, RdpSettingOutcome.Rejected));

        Assert.False(report.IsSafe);
        Assert.Single(report.Concerns);
    }

    [Fact]
    public void The_notice_says_how_many_and_which()
    {
        RdpSettingWrite gateway = Material with { Purpose = "The gateway", Name = "GatewayHostname" };

        RdpSettingsReport report = Report(
            Entry(Material, RdpSettingOutcome.Unsupported),
            Entry(gateway, RdpSettingOutcome.Rejected));

        Assert.Contains("2", report.Notice);
        Assert.Contains("clipboard redirection", report.Notice);
        Assert.Contains("the gateway", report.Notice);
    }

    [Fact]
    public void The_notice_stays_quiet_about_what_does_not_matter()
    {
        // A warning shown every time an older control declines a colour depth
        // is one people learn to dismiss unread, and the one that mattered
        // goes with it.
        RdpSettingsReport report = Report(
            Entry(Cosmetic, RdpSettingOutcome.Unsupported),
            Entry(Material, RdpSettingOutcome.Unsupported));

        Assert.DoesNotContain("colour depth", report.Notice);
        Assert.Contains("clipboard redirection", report.Notice);
    }

    [Fact]
    public void The_notice_keeps_the_proper_nouns_in_a_purpose()
    {
        // Lowercasing the whole phrase to drop it into a sentence takes the
        // capital off Windows with it.
        RdpSettingWrite sso = Material with { Purpose = "Signing in as the current Windows user" };

        RdpSettingsReport report = Report(Entry(sso, RdpSettingOutcome.Unsupported));

        Assert.Contains("signing in as the current Windows user", report.Notice);
    }

    [Fact]
    public void Concerns_keep_the_order_of_the_plan()
    {
        RdpSettingWrite first = Material with { Purpose = "The gateway" };
        RdpSettingWrite second = Material with { Purpose = "Clipboard redirection" };

        RdpSettingsReport report = Report(
            Entry(first, RdpSettingOutcome.Unsupported),
            Entry(Cosmetic, RdpSettingOutcome.Unsupported),
            Entry(second, RdpSettingOutcome.Unsupported));

        Assert.Equal(
            ["The gateway", "Clipboard redirection"],
            report.Concerns.Select(c => c.Write.Purpose));
    }

    [Fact]
    public void An_empty_report_has_nothing_to_complain_about()
    {
        Assert.True(RdpSettingsReport.Empty.IsClean);
        Assert.True(RdpSettingsReport.Empty.IsSafe);
        Assert.Null(RdpSettingsReport.Empty.Notice);
        Assert.Empty(RdpSettingsReport.Empty.Entries);
    }

    // ── One line of it ──────────────────────────────────────────────────

    [Fact]
    public void An_applied_write_is_not_a_failure_and_never_matters()
    {
        RdpSettingReport entry = Entry(Material);

        Assert.False(entry.IsFailure);
        Assert.False(entry.Matters);
    }

    [Fact]
    public void A_failure_on_something_cosmetic_is_a_failure_that_does_not_matter()
    {
        RdpSettingReport entry = Entry(Cosmetic, RdpSettingOutcome.Unsupported);

        Assert.True(entry.IsFailure);
        Assert.False(entry.Matters);
    }

    [Fact]
    public void A_write_offers_every_name_it_would_accept_best_first()
    {
        RdpSettingWrite write = Material with { Alternatives = ["OldName", "OlderName"] };

        Assert.Equal(["RedirectClipboard", "OldName", "OlderName"], write.Candidates);
    }

    [Fact]
    public void A_write_prints_as_what_it_would_do()
    {
        Assert.Equal("AdvancedSettings.RedirectClipboard = False", Material.ToString());
    }
}
