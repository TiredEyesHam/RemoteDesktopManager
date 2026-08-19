using Patchbay.Core.Model;
using Patchbay.Core.Sessions;

namespace Patchbay.Tests;

/// <summary>
/// The one number behind the experience checkboxes (M4-14).
///
/// Six of the eight flags turn something <em>off</em> and two turn something
/// <em>on</em>, so the mapping from a tick to a bit is an inversion for some
/// and not for others. Every mistake available here produces a session that
/// connects perfectly well and looks wrong in a way nobody attributes to a
/// setting, which is why the inversion is asserted one flag at a time rather
/// than trusted to read correctly.
/// </summary>
public class RdpPerformanceFlagsTests
{
    private static int For(Action<ConnectionSettings> configure)
    {
        ConnectionSettings settings = new();
        configure(settings);

        return RdpPerformanceFlags.For(settings);
    }

    // ── Guards ──────────────────────────────────────────────────────────

    [Fact]
    public void Flags_need_settings_to_come_from()
        => Assert.Throws<ArgumentNullException>(() => RdpPerformanceFlags.For(null!));

    [Fact]
    public void A_setting_nobody_resolved_contributes_nothing()
    {
        // Which for a disable bit leaves the feature on and for an enable bit
        // leaves it off — the control's own resting state in both cases. An
        // unresolved setting produces the session somebody would have got
        // without Patchbay, rather than a guess.
        Assert.Equal(0, RdpPerformanceFlags.For(new ConnectionSettings()));
    }

    // ── The six that are inverted ───────────────────────────────────────

    [Fact]
    public void Turning_the_wallpaper_off_sets_the_bit()
        => Assert.Equal(
            RdpPerformanceFlags.DisableWallpaper,
            For(s => s.DesktopBackground = false));

    [Fact]
    public void Turning_the_wallpaper_on_sets_nothing()
    {
        // The mistake this catches is the obvious reading — bit set means
        // feature on — which is exactly backwards for six of the eight.
        Assert.Equal(0, For(s => s.DesktopBackground = true));
    }

    [Fact]
    public void Dragging_a_window_without_its_contents_sets_the_bit()
        => Assert.Equal(
            RdpPerformanceFlags.DisableFullWindowDrag,
            For(s => s.ShowWindowContentsWhileDragging = false));

    [Fact]
    public void Menu_animations_off_sets_the_bit()
        => Assert.Equal(
            RdpPerformanceFlags.DisableMenuAnimations,
            For(s => s.MenuAnimations = false));

    [Fact]
    public void Visual_styles_off_sets_the_theming_bit()
    {
        // Two names for one idea: the setting is called visual styles because
        // that is what Windows calls it, and the flag is called theming because
        // that is what the protocol calls it.
        Assert.Equal(RdpPerformanceFlags.DisableTheming, For(s => s.VisualStyles = false));
    }

    // ── The two that are not ────────────────────────────────────────────

    [Fact]
    public void Font_smoothing_on_sets_the_bit()
        => Assert.Equal(
            RdpPerformanceFlags.EnableFontSmoothing,
            For(s => s.FontSmoothing = true));

    [Fact]
    public void Font_smoothing_off_sets_nothing()
        => Assert.Equal(0, For(s => s.FontSmoothing = false));

    [Fact]
    public void Desktop_composition_on_sets_the_bit()
        => Assert.Equal(
            RdpPerformanceFlags.EnableDesktopComposition,
            For(s => s.DesktopComposition = true));

    [Fact]
    public void Desktop_composition_off_sets_nothing()
        => Assert.Equal(0, For(s => s.DesktopComposition = false));

    // ── Together ────────────────────────────────────────────────────────

    [Fact]
    public void The_shipped_defaults_ask_for_a_plain_desktop_with_readable_text()
    {
        // The combination worth having over a link that is not a LAN: nothing
        // decorative, and the one cheap thing that makes a real difference to
        // reading.
        int flags = RdpPerformanceFlags.For(ConnectionSettings.Defaults);

        Assert.Equal(
            RdpPerformanceFlags.DisableWallpaper
            | RdpPerformanceFlags.DisableFullWindowDrag
            | RdpPerformanceFlags.DisableMenuAnimations
            | RdpPerformanceFlags.EnableFontSmoothing,
            flags);
    }

    [Fact]
    public void Everything_at_once_stays_inside_the_flags_that_are_known()
    {
        int flags = For(s =>
        {
            s.DesktopBackground = false;
            s.ShowWindowContentsWhileDragging = false;
            s.MenuAnimations = false;
            s.VisualStyles = false;
            s.FontSmoothing = true;
            s.DesktopComposition = true;
        });

        Assert.Equal(0, flags & ~RdpPerformanceFlags.Known);
    }

    [Fact]
    public void No_two_flags_share_a_bit()
    {
        int[] flags =
        [
            RdpPerformanceFlags.DisableWallpaper,
            RdpPerformanceFlags.DisableFullWindowDrag,
            RdpPerformanceFlags.DisableMenuAnimations,
            RdpPerformanceFlags.DisableTheming,
            RdpPerformanceFlags.DisableCursorShadow,
            RdpPerformanceFlags.DisableCursorSettings,
            RdpPerformanceFlags.EnableFontSmoothing,
            RdpPerformanceFlags.EnableDesktopComposition,
        ];

        Assert.Equal(flags.Sum(), flags.Aggregate(0, (all, flag) => all | flag));
    }

    [Fact]
    public void The_gap_at_0x10_is_deliberate()
    {
        // There is no flag between theming and the cursor shadow. Filling it in
        // to make the sequence tidy would set a bit whose meaning nobody here
        // has established.
        Assert.Equal(0, RdpPerformanceFlags.Known & 0x10);
    }

    // ── Saying what it means ────────────────────────────────────────────

    [Fact]
    public void Nothing_set_describes_as_nothing()
        => Assert.Empty(RdpPerformanceFlags.Describe(0));

    [Fact]
    public void The_description_reads_in_bit_order()
    {
        Assert.Equal(
            ["no wallpaper", "no theme", "font smoothing"],
            RdpPerformanceFlags.Describe(
                RdpPerformanceFlags.DisableWallpaper
                | RdpPerformanceFlags.DisableTheming
                | RdpPerformanceFlags.EnableFontSmoothing));
    }

    [Fact]
    public void A_bit_nobody_here_set_is_not_described()
    {
        // A number arriving from a document written by a later Patchbay, or
        // from somebody's hand. Naming it would be inventing a meaning.
        Assert.Empty(RdpPerformanceFlags.Describe(0x8000));
    }
}
