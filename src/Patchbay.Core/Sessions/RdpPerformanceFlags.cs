using Patchbay.Core.Model;

namespace Patchbay.Core.Sessions;

/// <summary>
/// The single number behind the experience checkboxes (M4-14).
///
/// <para>
/// <b>Six of the flags turn something off and two turn something on, and that
/// is the whole reason this is a class rather than a line in the mapper.</b>
/// The obvious reading — set the bit to enable the feature — gets six of the
/// eight exactly backwards, and every one of those mistakes produces a session
/// that connects perfectly well and looks wrong in a way nobody attributes to
/// a setting. Wallpaper, window contents while dragging, menu animations,
/// themes and the two cursor flags are all <em>disable</em> bits; font
/// smoothing and desktop composition are <em>enable</em> bits. So the mapping
/// from a checkbox to a bit is an inversion for some and not for others, and
/// writing it out once with the inversion visible is the only way it stays
/// right.
/// </para>
///
/// <para>
/// The number is a hint. The server has a policy of its own and may refuse any
/// of it, and connection quality detection (M4-14) can override the lot — so
/// nothing here is material in <see cref="RdpSettingWrite.IsMaterial"/>'s
/// sense. A desktop that came up with its wallpaper showing when somebody
/// asked for it hidden announces itself the instant the session draws, which
/// is the definition of an immaterial failure.
/// </para>
/// </summary>
public static class RdpPerformanceFlags
{
    /// <summary>Hide the remote desktop's wallpaper. <c>TS_PERF_DISABLE_WALLPAPER</c>.</summary>
    public const int DisableWallpaper = 0x00000001;

    /// <summary>Drag an outline rather than the window. <c>TS_PERF_DISABLE_FULLWINDOWDRAG</c>.</summary>
    public const int DisableFullWindowDrag = 0x00000002;

    /// <summary>Open menus without the fade. <c>TS_PERF_DISABLE_MENUANIMATIONS</c>.</summary>
    public const int DisableMenuAnimations = 0x00000004;

    /// <summary>Draw the classic look rather than the theme. <c>TS_PERF_DISABLE_THEMING</c>.</summary>
    public const int DisableTheming = 0x00000008;

    /// <summary>Drop the pointer's shadow. <c>TS_PERF_DISABLE_CURSOR_SHADOW</c>.</summary>
    public const int DisableCursorShadow = 0x00000020;

    /// <summary>Drop pointer blinking and trails. <c>TS_PERF_DISABLE_CURSORSETTINGS</c>.</summary>
    public const int DisableCursorSettings = 0x00000040;

    /// <summary>
    /// Anti-alias text. <c>TS_PERF_ENABLE_FONT_SMOOTHING</c> — an
    /// <em>enable</em> bit, unlike the six above it.
    /// </summary>
    public const int EnableFontSmoothing = 0x00000080;

    /// <summary>
    /// Run the remote desktop's window composition.
    /// <c>TS_PERF_ENABLE_DESKTOP_COMPOSITION</c> — the other enable bit.
    /// </summary>
    public const int EnableDesktopComposition = 0x00000100;

    /// <summary>
    /// Everything this understands. Anything outside it is a bit somebody set
    /// by hand, and <see cref="For"/> never produces one.
    /// </summary>
    public const int Known =
        DisableWallpaper
        | DisableFullWindowDrag
        | DisableMenuAnimations
        | DisableTheming
        | DisableCursorShadow
        | DisableCursorSettings
        | EnableFontSmoothing
        | EnableDesktopComposition;

    /// <summary>
    /// The number for a set of resolved settings.
    /// </summary>
    /// <remarks>
    /// A setting still null after resolution contributes nothing, which for a
    /// disable bit means the feature stays on and for an enable bit means it
    /// stays off. That is the control's own resting state in both cases, so an
    /// unresolved setting produces the session somebody would have got without
    /// Patchbay rather than a guess.
    /// </remarks>
    public static int For(ConnectionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        int flags = 0;

        // The inversions. Read each of these as "the person unticked it, so
        // tell the server to leave it out".
        flags |= Off(settings.DesktopBackground, DisableWallpaper);
        flags |= Off(settings.ShowWindowContentsWhileDragging, DisableFullWindowDrag);
        flags |= Off(settings.MenuAnimations, DisableMenuAnimations);
        flags |= Off(settings.VisualStyles, DisableTheming);

        // The two that are not inverted.
        flags |= On(settings.FontSmoothing, EnableFontSmoothing);
        flags |= On(settings.DesktopComposition, EnableDesktopComposition);

        return flags;
    }

    /// <summary>
    /// The flags in <paramref name="value"/>, named, in bit order. For the
    /// settings report and for a log line — a session that came up looking
    /// wrong is much easier to explain from six words than from <c>0x8F</c>.
    /// </summary>
    public static IReadOnlyList<string> Describe(int value)
    {
        List<string> names = [];

        Name(names, value, DisableWallpaper, "no wallpaper");
        Name(names, value, DisableFullWindowDrag, "outline drag");
        Name(names, value, DisableMenuAnimations, "no menu animations");
        Name(names, value, DisableTheming, "no theme");
        Name(names, value, DisableCursorShadow, "no cursor shadow");
        Name(names, value, DisableCursorSettings, "plain cursor");
        Name(names, value, EnableFontSmoothing, "font smoothing");
        Name(names, value, EnableDesktopComposition, "desktop composition");

        return names;
    }

    private static int Off(bool? wanted, int flag) => wanted is false ? flag : 0;

    private static int On(bool? wanted, int flag) => wanted is true ? flag : 0;

    private static void Name(List<string> names, int value, int flag, string label)
    {
        if ((value & flag) == flag)
        {
            names.Add(label);
        }
    }
}
