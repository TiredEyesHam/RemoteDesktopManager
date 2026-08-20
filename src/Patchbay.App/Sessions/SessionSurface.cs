using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms.Integration;
using Patchbay.Rdp.Hosting;

namespace Patchbay.App.Sessions;

/// <summary>
/// The place a session is shown, and the rule that keeps it showable (M4-03).
///
/// One idea: swap, never stack. A hosted session sits in its own child window
/// and always draws in front of WPF content in the same window, so anything
/// meant to appear over a session cannot be layered on top of it — it has to
/// take its place. This control owns the swap so no caller has to remember it.
///
/// That is why prompts are docked panels rather than modal dialogs (M3-06). A
/// modal over a live session is a dialog nobody can see.
///
/// Placement is checked rather than assumed. On load the surface asks
/// <see cref="AirspaceRules"/> what its ancestors will do to it and publishes
/// the answer, because every one of those failures is silent and none of them
/// looks like a layout mistake when it happens.
///
/// Sizing is deliberately absent. The control gets the space it is given;
/// deciding what resolution to ask the far end for is smart sizing (M5-09) and
/// dynamic resolution (M5-10).
/// </summary>
public sealed class SessionSurface : UserControl, IDisposable
{
    private readonly Grid _root = new();
    private readonly WindowsFormsHost _host = new();
    private readonly ContentPresenter _overlayPresenter = new();

    public SessionSurface()
    {
        _overlayPresenter.SetBinding(
            ContentPresenter.ContentProperty,
            new System.Windows.Data.Binding(nameof(Overlay)) { Source = this });

        _root.Children.Add(_host);
        _root.Children.Add(_overlayPresenter);
        Content = _root;

        // Both children fill the same cell. Only ever one of them is visible,
        // which is the whole point; see UpdateVisibility.
        UpdateVisibility();

        Loaded += OnLoaded;
    }

    /// <summary>
    /// Raised once the surface is in a window and its placement has been
    /// checked. Carries an empty list when all is well, so a handler can log
    /// either way without asking twice.
    /// </summary>
    public event EventHandler<IReadOnlyList<AirspaceViolation>>? PlacementChecked;

    /// <summary>
    /// What the ancestors will do to the session, from the last check. Empty
    /// until the surface has been loaded.
    /// </summary>
    public IReadOnlyList<AirspaceViolation> AirspaceViolations { get; private set; } = [];

    /// <summary>WPF content shown in place of the session. Never over it.</summary>
    public static readonly DependencyProperty OverlayProperty = DependencyProperty.Register(
        nameof(Overlay),
        typeof(object),
        typeof(SessionSurface),
        new PropertyMetadata(null));

    /// <inheritdoc cref="OverlayProperty" />
    public object? Overlay
    {
        get => GetValue(OverlayProperty);
        set => SetValue(OverlayProperty, value);
    }

    /// <summary>
    /// How to draw <see cref="Overlay"/>. Exists so a surface inside a list
    /// can take a view model as its overlay and a template from its
    /// surroundings (M5-01) — without it, every tab would have to build its
    /// own copy of the same panel and hope it inherited the right data
    /// context.
    /// </summary>
    public static readonly DependencyProperty OverlayTemplateProperty = DependencyProperty.Register(
        nameof(OverlayTemplate),
        typeof(DataTemplate),
        typeof(SessionSurface),
        new PropertyMetadata(null, OnOverlayTemplateChanged));

    /// <inheritdoc cref="OverlayTemplateProperty" />
    public DataTemplate? OverlayTemplate
    {
        get => (DataTemplate?)GetValue(OverlayTemplateProperty);
        set => SetValue(OverlayTemplateProperty, value);
    }

    /// <summary>
    /// Whether the overlay is showing instead of the session. Setting this is
    /// the only way to hide a session without detaching it, and the session is
    /// collapsed rather than merely obscured, because obscuring it does not
    /// work.
    /// </summary>
    public static readonly DependencyProperty IsOverlayVisibleProperty = DependencyProperty.Register(
        nameof(IsOverlayVisible),
        typeof(bool),
        typeof(SessionSurface),
        new PropertyMetadata(true, OnVisibilityAffectingPropertyChanged));

    /// <inheritdoc cref="IsOverlayVisibleProperty" />
    public bool IsOverlayVisible
    {
        get => (bool)GetValue(IsOverlayVisibleProperty);
        set => SetValue(IsOverlayVisibleProperty, value);
    }

    /// <summary>Whether a session is currently attached.</summary>
    public bool HasSession => _host.Child is not null;

    /// <summary>
    /// The session to show. Bound per tab, so that the surface finds its own
    /// window rather than something outside having to hand one over at the
    /// right moment — which is a moment nobody can name, because WPF realises
    /// a tab some time after the view model creates it.
    ///
    /// A session with no window is ordinary rather than an error: the fake
    /// host (M4-01) has none, and the overlay is all those sessions show.
    /// </summary>
    public static readonly DependencyProperty SessionProperty = DependencyProperty.Register(
        nameof(Session),
        typeof(object),
        typeof(SessionSurface),
        new PropertyMetadata(null, OnSessionChanged));

    /// <inheritdoc cref="SessionProperty" />
    public object? Session
    {
        get => GetValue(SessionProperty);
        set => SetValue(SessionProperty, value);
    }

    /// <summary>
    /// Puts a session control in the surface. The surface does not own it: a
    /// tab that is switched away from should keep its session alive, so
    /// detaching is not disposing.
    /// </summary>
    public void AttachSession(System.Windows.Forms.Control control)
    {
        ArgumentNullException.ThrowIfNull(control);

        _host.Child = control;
        UpdateVisibility();
    }

    /// <summary>
    /// Takes the session out without disposing it, and shows the overlay,
    /// because there is nothing else left to show.
    /// </summary>
    public System.Windows.Forms.Control? DetachSession()
    {
        System.Windows.Forms.Control? detached = _host.Child;
        _host.Child = null;
        IsOverlayVisible = true;
        UpdateVisibility();

        return detached;
    }

    /// <summary>
    /// Re-runs the placement check. Worth calling after the surface is moved,
    /// for instance when a tab is torn out into its own window (M5-04).
    /// </summary>
    public IReadOnlyList<AirspaceViolation> CheckPlacement()
    {
        AirspaceViolations = AirspaceRules.Inspect(_host);
        PlacementChecked?.Invoke(this, AirspaceViolations);

        if (AirspaceViolations.Count > 0)
        {
            // To the debugger, and to whoever subscribed. Not to an assert
            // dialog: this type exists to argue that a modal over a session is
            // a dialog nobody can see, and raising one from here would be a
            // poor way to make the point. The shell shows it in the notice bar.
            Debug.WriteLine(AirspaceRules.Describe(AirspaceViolations));
        }

        return AirspaceViolations;
    }

    /// <summary>
    /// Releases the hosting bridge. The session itself is detached first and
    /// left alive, because <see cref="WindowsFormsHost"/> disposes whatever
    /// child it still holds and this surface never owned it — closing a tab
    /// should end a session by way of the code that started it, not as a side
    /// effect of a panel going away.
    /// </summary>
    public void Dispose()
    {
        _host.Child = null;
        _host.Dispose();
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => CheckPlacement();

    private static void OnOverlayTemplateChanged(
        DependencyObject d,
        DependencyPropertyChangedEventArgs e)
        => ((SessionSurface)d)._overlayPresenter.ContentTemplate = e.NewValue as DataTemplate;

    /// <summary>
    /// Takes the window out of a session that has one. The type test is the
    /// seam: <c>Core</c> may not name a WinForms control, so the session
    /// declares it in <c>Patchbay.Rdp</c> and the shell — which references
    /// both — is the only place the two meet.
    /// </summary>
    private static void OnSessionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        SessionSurface surface = (SessionSurface)d;

        // Detached rather than disposed. The session owns its window and is
        // entitled to be shown again somewhere else.
        surface.DetachSession();

        if (e.NewValue is IHostedSessionView { View: { } view })
        {
            surface.AttachSession(view);
        }
    }

    private static void OnVisibilityAffectingPropertyChanged(
        DependencyObject d,
        DependencyPropertyChangedEventArgs e)
        => ((SessionSurface)d).UpdateVisibility();

    private void UpdateVisibility()
    {
        bool showSession = HasSession && !IsOverlayVisible;

        // Collapsed, not Hidden. A hidden WindowsFormsHost still occupies its
        // space, and the point of the swap is that the overlay gets the space
        // the session was using.
        _host.Visibility = showSession ? Visibility.Visible : Visibility.Collapsed;
        _overlayPresenter.Visibility = showSession ? Visibility.Collapsed : Visibility.Visible;
    }
}
