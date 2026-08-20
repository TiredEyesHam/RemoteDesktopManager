namespace Patchbay.Core.Sessions;

/// <summary>A width and a height in device pixels. Core has no drawing library to borrow one from.</summary>
public readonly record struct PixelSize(int Width, int Height)
{
    /// <summary>Nothing. What an unmeasured pane and an unconnected session both report.</summary>
    public static PixelSize Empty => default;

    /// <summary>True when there is no area to speak of. Zero and negative are the same answer.</summary>
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public override string ToString() => $"{Width}x{Height}";
}

/// <summary>A rectangle in the pane's own coordinates, origin top left.</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public static PixelRect Empty => default;

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public PixelSize Size => new(Width, Height);

    public override string ToString() => $"{Width}x{Height} at {X},{Y}";
}

/// <summary>Where a session's picture goes inside the space a tab has for it.</summary>
public readonly record struct SessionPlacement
{
    /// <summary>The rectangle the session control should occupy.</summary>
    public required PixelRect Bounds { get; init; }

    /// <summary>
    /// How much the picture is being resized. 1 is pixel for pixel; 0.6 means
    /// the remote desktop is drawn at sixty per cent, which is legible in the
    /// way a photograph of text is legible.
    /// </summary>
    public required double Scale { get; init; }

    /// <summary>Whether the picture is being resized at all.</summary>
    public bool IsScaled => Math.Abs(Scale - 1.0) > 0.0005;

    /// <summary>
    /// Whether the picture is bigger than the space, so the pane has to
    /// scroll. Never true under smart sizing — that is the point of it.
    /// </summary>
    public required bool NeedsScrolling { get; init; }

    /// <summary>The scale as a whole number, for the status bar (M5-17).</summary>
    public int ScalePercent => (int)Math.Round(Scale * 100.0, MidpointRounding.AwayFromZero);

    /// <summary>Nothing to place: no session, or nothing of it known yet.</summary>
    public static SessionPlacement Nowhere => new()
    {
        Bounds = PixelRect.Empty,
        Scale = 1.0,
        NeedsScrolling = false,
    };
}

/// <summary>
/// Fitting a session of one size into a tab of another (M5-09).
///
/// A session has the resolution it negotiated when it connected, and short of
/// dynamic resolution (M5-10) it keeps that resolution however the window is
/// resized afterwards. So there are only two things to do with a picture that
/// is the wrong size for the space, and this is the choice smart sizing offers:
///
/// <list type="bullet">
///   <item><b>On</b> — scale the picture to fit. Everything is visible and
///   nothing scrolls, at the cost of sharpness. This is the v1 default,
///   because a desktop you can see all of at ninety per cent is more use than
///   a crisp top-left corner of one.</item>
///   <item><b>Off</b> — draw it pixel for pixel and scroll. Sharp, and the
///   right answer for reading a log or lining up a screenshot.</item>
/// </list>
///
/// The non-obvious part is the letterbox. The control's <c>SmartSizing</c>
/// scales the desktop to fill whatever window it is given, whatever shape that
/// window is, so handing it the whole pane returns a 16:9 desktop stretched to
/// fit a tall one. Instead the picture gets the largest rectangle of the
/// session's own shape that fits, centred, with the pane's background above
/// and below. The scaling then cannot distort.
///
/// Smart sizing enlarges as well as shrinks. A 1024×768 session drawn at its
/// own size in a maximised tab is a small rectangle marooned in a large empty
/// one; mstsc and RDCMan both fill the space.
///
/// It is not a resolution change. Text at sixty per cent is blurred text, not
/// smaller text, and no amount of scaling gives the far end more room. That is
/// M5-10.
///
/// Lives in <c>Core</c> because it is arithmetic with right answers, and
/// because working it out inside a resize handler is where being one pixel out
/// shows up as a flickering scrollbar and nowhere else.
/// </summary>
public static class SessionScaling
{
    /// <summary>
    /// Works out where the session's picture goes.
    /// </summary>
    /// <param name="session">The session's resolution, as negotiated.</param>
    /// <param name="viewport">The space the tab has for it.</param>
    /// <param name="smartSizing">Whether to scale to fit rather than scroll.</param>
    public static SessionPlacement Place(PixelSize session, PixelSize viewport, bool smartSizing)
    {
        if (session.IsEmpty)
        {
            return SessionPlacement.Nowhere;
        }

        if (viewport.IsEmpty)
        {
            // Asked before the first layout pass. There is no fitting to be
            // done against a viewport of nothing, and answering "scale it to
            // zero" would hand the control a window with no area, which some
            // generations of it take badly. Its own size, and ask again later.
            return Unscaled(session, viewport);
        }

        return smartSizing ? Fitted(session, viewport) : Unscaled(session, viewport);
    }

    /// <summary>
    /// How much a session would have to be resized to fit. 1 when it already
    /// does exactly, less than 1 when it is too big, more when the tab has
    /// room to spare.
    /// </summary>
    public static double ScaleFor(PixelSize session, PixelSize viewport)
    {
        if (session.IsEmpty || viewport.IsEmpty)
        {
            return 1.0;
        }

        // The smaller of the two ratios: the axis that runs out of room first
        // is the one that decides, and deciding by it is what keeps the shape.
        return Math.Min(
            (double)viewport.Width / session.Width,
            (double)viewport.Height / session.Height);
    }

    private static SessionPlacement Fitted(PixelSize session, PixelSize viewport)
    {
        double scale = ScaleFor(session, viewport);

        // Rounding can put a rectangle a pixel over the edge, and a pixel over
        // the edge is a scrollbar that appears, steals the width that made it
        // necessary, and disappears again, forever.
        int width = Clamp(Round(session.Width * scale), viewport.Width);
        int height = Clamp(Round(session.Height * scale), viewport.Height);

        return new SessionPlacement
        {
            Bounds = new PixelRect(
                Centre(viewport.Width, width),
                Centre(viewport.Height, height),
                width,
                height),
            Scale = scale,
            NeedsScrolling = false,
        };
    }

    private static SessionPlacement Unscaled(PixelSize session, PixelSize viewport)
    {
        // Centred on an axis with room to spare, hard against the origin on one
        // without: the top left is where a scrolled session has to start,
        // because it is where the Start button and every window's title are.
        int x = Centre(viewport.Width, session.Width);
        int y = Centre(viewport.Height, session.Height);

        return new SessionPlacement
        {
            Bounds = new PixelRect(x, y, session.Width, session.Height),
            Scale = 1.0,
            NeedsScrolling = session.Width > viewport.Width || session.Height > viewport.Height,
        };
    }

    private static int Round(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);

    /// <summary>At least one pixel, and never larger than what it has to fit inside.</summary>
    private static int Clamp(int value, int limit) => Math.Clamp(value, 1, Math.Max(1, limit));

    /// <summary>The offset that centres <paramref name="length"/>, or zero when it does not fit.</summary>
    private static int Centre(int available, int length) => Math.Max(0, (available - length) / 2);
}
