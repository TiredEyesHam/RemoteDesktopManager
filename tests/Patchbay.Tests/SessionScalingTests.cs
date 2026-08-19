using Patchbay.Core.Sessions;

namespace Patchbay.Tests;

/// <summary>
/// Fitting a session into a tab (M5-09). Every case here is a picture someone
/// would notice was wrong: a stretched desktop, a scrollbar that should not be
/// there, a session marooned in the corner of an empty pane.
/// </summary>
public class SessionScalingTests
{
    private static readonly PixelSize Wide = new(1920, 1080);
    private static readonly PixelSize Small = new(1024, 768);

    // ── Smart sizing on ─────────────────────────────────────────────────

    [Fact]
    public void A_session_that_already_fits_exactly_is_left_alone()
    {
        SessionPlacement placement = SessionScaling.Place(Wide, Wide, smartSizing: true);

        Assert.Equal(new PixelRect(0, 0, 1920, 1080), placement.Bounds);
        Assert.False(placement.IsScaled);
        Assert.Equal(100, placement.ScalePercent);
    }

    [Fact]
    public void A_session_too_big_for_the_tab_is_shrunk_to_fit()
    {
        SessionPlacement placement = SessionScaling.Place(Wide, new PixelSize(960, 540), smartSizing: true);

        Assert.Equal(new PixelRect(0, 0, 960, 540), placement.Bounds);
        Assert.Equal(50, placement.ScalePercent);
        Assert.False(placement.NeedsScrolling);
    }

    [Fact]
    public void A_small_session_is_enlarged_to_fill_a_big_tab()
    {
        // Drawn at its own size it would be a small rectangle marooned in a
        // large empty one, which reads as a bug rather than as a choice.
        SessionPlacement placement = SessionScaling.Place(Small, new PixelSize(2048, 1536), smartSizing: true);

        Assert.Equal(new PixelRect(0, 0, 2048, 1536), placement.Bounds);
        Assert.Equal(200, placement.ScalePercent);
    }

    [Fact]
    public void The_shape_of_the_desktop_survives_a_tab_of_a_different_shape()
    {
        // 1920×1080 into a tall pane. Filling it would give back a stretched
        // desktop, and nothing about a stretched desktop announces itself.
        SessionPlacement placement = SessionScaling.Place(Wide, new PixelSize(800, 900), smartSizing: true);

        Assert.Equal(800, placement.Bounds.Width);
        Assert.Equal(450, placement.Bounds.Height);
        AssertSameShape(Wide, placement.Bounds.Size);
    }

    [Fact]
    public void The_bars_of_a_letterbox_are_the_same_on_both_sides()
    {
        SessionPlacement placement = SessionScaling.Place(Wide, new PixelSize(800, 900), smartSizing: true);

        Assert.Equal(0, placement.Bounds.X);
        Assert.Equal((900 - 450) / 2, placement.Bounds.Y);
    }

    [Fact]
    public void A_wide_tab_puts_the_bars_at_the_sides_instead()
    {
        SessionPlacement placement = SessionScaling.Place(Small, new PixelSize(1600, 768), smartSizing: true);

        Assert.Equal(768, placement.Bounds.Height);
        Assert.Equal(1024, placement.Bounds.Width);
        Assert.Equal((1600 - 1024) / 2, placement.Bounds.X);
        Assert.Equal(0, placement.Bounds.Y);
    }

    [Fact]
    public void Smart_sizing_never_asks_for_a_scrollbar()
    {
        foreach (PixelSize viewport in new PixelSize[]
        {
            new(320, 200), new(1919, 1079), new(1920, 1080), new(4000, 3000),
        })
        {
            SessionPlacement placement = SessionScaling.Place(Wide, viewport, smartSizing: true);

            Assert.False(placement.NeedsScrolling);
            Assert.True(placement.Bounds.Width <= viewport.Width);
            Assert.True(placement.Bounds.Height <= viewport.Height);
        }
    }

    [Fact]
    public void Rounding_never_puts_the_picture_over_the_edge()
    {
        // A pixel over the edge is a scrollbar that appears, steals the width
        // that made it necessary, and disappears again, forever. Awkward ratios
        // are where that happens, so walk a run of them.
        for (int width = 331; width < 431; width++)
        {
            PixelSize viewport = new(width, 277);
            SessionPlacement placement = SessionScaling.Place(Wide, viewport, smartSizing: true);

            Assert.True(placement.Bounds.Width <= viewport.Width, $"width at {viewport}");
            Assert.True(placement.Bounds.Height <= viewport.Height, $"height at {viewport}");
            Assert.True(placement.Bounds.Width >= 1);
            Assert.True(placement.Bounds.Height >= 1);
        }
    }

    [Fact]
    public void A_tab_too_small_to_hold_a_pixel_still_gets_a_pixel()
    {
        // The control takes a window with no area badly, and a pane can be one
        // pixel wide for exactly as long as someone holds the splitter there.
        SessionPlacement placement = SessionScaling.Place(Wide, new PixelSize(1, 1), smartSizing: true);

        Assert.Equal(1, placement.Bounds.Width);
        Assert.Equal(1, placement.Bounds.Height);
    }

    // ── Smart sizing off ────────────────────────────────────────────────

    [Fact]
    public void With_smart_sizing_off_the_picture_keeps_its_own_size()
    {
        SessionPlacement placement = SessionScaling.Place(Wide, new PixelSize(960, 540), smartSizing: false);

        Assert.Equal(new PixelSize(1920, 1080), placement.Bounds.Size);
        Assert.False(placement.IsScaled);
        Assert.True(placement.NeedsScrolling);
    }

    [Fact]
    public void A_session_that_is_scrolled_starts_at_the_top_left()
    {
        // Where the Start button is, and the title of every window.
        SessionPlacement placement = SessionScaling.Place(Wide, new PixelSize(960, 540), smartSizing: false);

        Assert.Equal(0, placement.Bounds.X);
        Assert.Equal(0, placement.Bounds.Y);
    }

    [Fact]
    public void A_session_smaller_than_the_tab_sits_in_the_middle_of_it()
    {
        SessionPlacement placement = SessionScaling.Place(Small, new PixelSize(1600, 1000), smartSizing: false);

        Assert.Equal((1600 - 1024) / 2, placement.Bounds.X);
        Assert.Equal((1000 - 768) / 2, placement.Bounds.Y);
        Assert.False(placement.NeedsScrolling);
    }

    [Fact]
    public void Each_axis_decides_for_itself_whether_to_centre()
    {
        // Wider than the tab but not as tall: scrolled sideways, centred down.
        SessionPlacement placement = SessionScaling.Place(Wide, new PixelSize(1200, 1400), smartSizing: false);

        Assert.Equal(0, placement.Bounds.X);
        Assert.Equal((1400 - 1080) / 2, placement.Bounds.Y);
        Assert.True(placement.NeedsScrolling);
    }

    [Fact]
    public void A_session_exactly_the_size_of_the_tab_does_not_scroll()
    {
        SessionPlacement placement = SessionScaling.Place(Wide, Wide, smartSizing: false);

        Assert.False(placement.NeedsScrolling);
        Assert.Equal(new PixelRect(0, 0, 1920, 1080), placement.Bounds);
    }

    [Fact]
    public void One_pixel_over_is_still_over()
    {
        SessionPlacement placement = SessionScaling.Place(
            new PixelSize(1921, 1080),
            Wide,
            smartSizing: false);

        Assert.True(placement.NeedsScrolling);
    }

    // ── Nothing to place ────────────────────────────────────────────────

    [Fact]
    public void A_session_with_no_resolution_yet_is_not_placed_anywhere()
    {
        foreach (bool smartSizing in new[] { true, false })
        {
            Assert.Equal(
                SessionPlacement.Nowhere,
                SessionScaling.Place(PixelSize.Empty, Wide, smartSizing));
        }
    }

    [Fact]
    public void A_tab_that_has_not_been_measured_yet_gets_the_session_at_its_own_size()
    {
        // The first layout pass asks before there is anything to fit into.
        // Scaling to zero would hand the control a window with no area.
        SessionPlacement placement = SessionScaling.Place(Wide, PixelSize.Empty, smartSizing: true);

        Assert.Equal(new PixelSize(1920, 1080), placement.Bounds.Size);
        Assert.False(placement.IsScaled);
    }

    [Fact]
    public void A_negative_size_is_treated_as_no_size()
    {
        Assert.True(new PixelSize(-1920, 1080).IsEmpty);
        Assert.True(new PixelSize(1920, -1080).IsEmpty);
        Assert.Equal(
            SessionPlacement.Nowhere,
            SessionScaling.Place(new PixelSize(-1, -1), Wide, smartSizing: true));
    }

    // ── The scale on its own ────────────────────────────────────────────

    [Fact]
    public void The_scale_is_decided_by_the_axis_that_runs_out_first()
    {
        Assert.Equal(0.5, SessionScaling.ScaleFor(Wide, new PixelSize(960, 900)), 4);
        Assert.Equal(0.5, SessionScaling.ScaleFor(Wide, new PixelSize(1800, 540)), 4);
    }

    [Fact]
    public void Nothing_to_measure_scales_by_one_rather_than_by_zero()
    {
        Assert.Equal(1.0, SessionScaling.ScaleFor(PixelSize.Empty, Wide));
        Assert.Equal(1.0, SessionScaling.ScaleFor(Wide, PixelSize.Empty));
    }

    [Fact]
    public void A_scale_a_thousandth_off_one_does_not_count_as_scaled()
    {
        // 1919 of 1920 is not a resize anybody can see, and reporting it as one
        // would put "99%" in the status bar of a session that fits.
        SessionPlacement placement = SessionScaling.Place(Wide, new PixelSize(1920, 1080), smartSizing: true);

        Assert.False(placement.IsScaled);
    }

    private static void AssertSameShape(PixelSize session, PixelSize drawn)
    {
        double before = (double)session.Width / session.Height;
        double after = (double)drawn.Width / drawn.Height;

        // Within a pixel of rounding on the smaller axis.
        Assert.True(
            Math.Abs(before - after) < 0.01,
            $"{session} was drawn as {drawn}, which is a different shape.");
    }
}
