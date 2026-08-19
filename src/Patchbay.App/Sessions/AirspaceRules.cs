using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Patchbay.App.Sessions;

/// <summary>What a hosted session control will do that its container did not ask for.</summary>
public enum AirspaceProblem
{
    /// <summary>The control will not appear at all.</summary>
    NotRendered,

    /// <summary>The control will appear, but ignore an effect applied above it.</summary>
    IgnoresVisualEffect,

    /// <summary>The control will paint outside the bounds it was given.</summary>
    EscapesClipping,
}

/// <summary>One thing wrong with where a session has been put.</summary>
public sealed record AirspaceViolation
{
    public required AirspaceProblem Problem { get; init; }

    /// <summary>The ancestor responsible, named as usefully as it can be.</summary>
    public required string Element { get; init; }

    /// <summary>What will actually happen, in terms someone can act on.</summary>
    public required string Explanation { get; init; }

    public override string ToString() => $"{Problem}: {Element} — {Explanation}";
}

/// <summary>
/// The layout rules a live session imposes on everything above it (M4-03).
///
/// The RDP control paints into its own child window, and a child window is not
/// part of WPF's composition. It sits in front, always, and WPF's rendering
/// model has no say in it. That produces three failures, all of which look
/// like bugs somewhere else:
///
/// <list type="number">
///   <item>An opacity, effect or transform on any ancestor applies to WPF
///   content and is simply ignored by the session. The window fades; the
///   remote desktop does not.</item>
///   <item>Clipping is not applied either, so a session inside a scrolling or
///   clipping container paints straight over its neighbours and keeps
///   painting there while they scroll underneath.</item>
///   <item>A window with <c>AllowsTransparency</c> set is composed as a
///   layered window, and child HWNDs are not composed into one at all. The
///   session vanishes completely — no error, no blank rectangle, nothing.</item>
/// </list>
///
/// The third is the one to watch, because the obvious way to build the custom
/// title bar in M0-12 is to turn <c>AllowsTransparency</c> on, and the cost
/// only shows up in M4 when someone tries to connect. Use <c>WindowChrome</c>
/// instead.
///
/// What is <i>not</i> a violation: a <see cref="Popup"/>, a tooltip or a
/// context menu over the session. Those get their own top-level windows and
/// order above the control quite happily. It is in-window WPF content drawn
/// over the session that loses, which is why
/// <see cref="SessionSurface"/> swaps rather than stacks.
/// </summary>
public static class AirspaceRules
{
    /// <summary>
    /// Walks up from a hosted session and reports what its ancestors will do
    /// to it. An empty list means the placement is sound.
    /// </summary>
    /// <param name="host">The element holding the session, once it is loaded.</param>
    public static IReadOnlyList<AirspaceViolation> Inspect(DependencyObject host)
    {
        ArgumentNullException.ThrowIfNull(host);

        List<AirspaceViolation> violations = [];

        for (DependencyObject? current = Parent(host); current is not null; current = Parent(current))
        {
            // A popup boundary ends the walk: the content beyond it lives in a
            // different top-level window, and that window's composition has no
            // bearing on a child HWND inside this one.
            if (current is Popup)
            {
                break;
            }

            if (current is Window window)
            {
                InspectWindow(window, violations);
                break;
            }

            InspectAncestor(current, violations);
        }

        return violations;
    }

    /// <summary>A report for the log, or for a notice in the shell.</summary>
    public static string Describe(IReadOnlyList<AirspaceViolation> violations)
    {
        ArgumentNullException.ThrowIfNull(violations);

        if (violations.Count == 0)
        {
            return "Session placement is airspace-safe.";
        }

        StringBuilder report = new();
        report.AppendLine("This session is placed somewhere that will not display it correctly:");

        foreach (AirspaceViolation violation in violations)
        {
            report.AppendLine(CultureInfo.InvariantCulture, $"  {violation}");
        }

        return report.ToString().TrimEnd();
    }

    private static void InspectWindow(Window window, List<AirspaceViolation> violations)
    {
        if (window.AllowsTransparency)
        {
            violations.Add(new AirspaceViolation
            {
                Problem = AirspaceProblem.NotRendered,
                Element = Name(window),
                Explanation = "the window sets AllowsTransparency, so it is composed as a layered "
                    + "window and the session will not be drawn at all. Use WindowChrome for a "
                    + "custom title bar instead.",
            });
        }

        InspectAncestor(window, violations);
    }

    private static void InspectAncestor(DependencyObject ancestor, List<AirspaceViolation> violations)
    {
        if (ancestor is not UIElement element)
        {
            return;
        }

        if (element.Opacity < 1.0)
        {
            violations.Add(Ignored(ancestor, $"Opacity is {element.Opacity}, which the session will not honour"));
        }

        if (element.OpacityMask is not null)
        {
            violations.Add(Ignored(ancestor, "an OpacityMask is set, which the session will not honour"));
        }

        if (element.Effect is not null)
        {
            violations.Add(Ignored(ancestor, "an Effect is set, which the session will not honour"));
        }

        if (element.RenderTransform is not null && !element.RenderTransform.Value.IsIdentity)
        {
            violations.Add(Ignored(ancestor, "a RenderTransform is set, which the session will not honour"));
        }

        if (element is FrameworkElement { LayoutTransform: { } layout } && !layout.Value.IsIdentity)
        {
            violations.Add(Ignored(ancestor, "a LayoutTransform is set, which the session will not honour"));
        }

        // Clipping is the one that damages neighbours rather than the session
        // itself, so it is worth naming separately.
        if (ancestor is ScrollViewer)
        {
            violations.Add(new AirspaceViolation
            {
                Problem = AirspaceProblem.EscapesClipping,
                Element = Name(ancestor),
                Explanation = "the session is inside a ScrollViewer. It will neither scroll nor clip, "
                    + "and will paint over whatever scrolls past it.",
            });
        }
        else if (element.Clip is not null || (element is FrameworkElement { ClipToBounds: true }))
        {
            violations.Add(new AirspaceViolation
            {
                Problem = AirspaceProblem.EscapesClipping,
                Element = Name(ancestor),
                Explanation = "clipping is set here, and the session will ignore it and paint outside.",
            });
        }
    }

    private static AirspaceViolation Ignored(DependencyObject ancestor, string explanation) => new()
    {
        Problem = AirspaceProblem.IgnoresVisualEffect,
        Element = Name(ancestor),
        Explanation = explanation,
    };

    private static DependencyObject? Parent(DependencyObject node)
    {
        // Visual first, because that is the tree composition follows. The
        // logical fallback catches the gaps, notably content sitting inside a
        // popup or a template that has not been realised yet.
        if (node is Visual or System.Windows.Media.Media3D.Visual3D)
        {
            DependencyObject? visualParent = VisualTreeHelper.GetParent(node);

            if (visualParent is not null)
            {
                return visualParent;
            }
        }

        return LogicalTreeHelper.GetParent(node);
    }

    private static string Name(DependencyObject element)
    {
        string type = element.GetType().Name;

        return element is FrameworkElement { Name.Length: > 0 } named
            ? $"{type} '{named.Name}'"
            : type;
    }
}
