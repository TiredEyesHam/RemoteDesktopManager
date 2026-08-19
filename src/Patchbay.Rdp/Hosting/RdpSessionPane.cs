using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using Patchbay.Core.Sessions;

namespace Patchbay.Rdp.Hosting;

/// <summary>
/// The space a session is drawn in, and the thing that decides how much of it
/// the session gets (M5-09).
///
/// <see cref="RdpSessionControl"/> is a window that paints a remote desktop at
/// whatever size it is given. This is the container that gives it one, and it
/// exists because there are two answers and the person gets to choose between
/// them — <see cref="SessionScaling"/> works out both, and this applies the
/// one that was picked:
///
/// <list type="bullet">
///   <item><b>Smart sizing on</b> — the control is sized to the largest
///   rectangle of the session's own shape that fits, and centred. The control
///   scales the desktop into it. Because the rectangle is the right shape, the
///   scaling cannot stretch anything; because it always fits, nothing scrolls.</item>
///   <item><b>Off</b> — the control is given the session's full size and this
///   pane scrolls.</item>
/// </list>
///
/// <para>
/// <b>Why the scrolling happens here and not in WPF.</b> A hosted child window
/// is not composed with WPF content: it neither scrolls with a
/// <c>ScrollViewer</c> nor gets clipped by one, so a session inside one would
/// sit still while its surroundings moved and paint over whatever scrolled
/// past it. <c>AirspaceRules</c> reports exactly that, by name. Scrolling on
/// this side of the boundary has none of that problem — the scrollbars are
/// child windows of the same parent, and clipping a child to its parent is the
/// one thing Win32 has always done.
/// </para>
///
/// <para>
/// The pane owns its control. They have the same lifetime, one of each per
/// session, and disposing the pane takes the session's window with it — which
/// is why <c>SessionSurface</c> detaches rather than disposes when a tab is
/// switched away from.
/// </para>
/// </summary>
[DesignerCategory("")]
public sealed class RdpSessionPane : Panel
{
    private readonly RdpSessionControl _session;

    private PixelSize _sessionSize;
    private bool _applying;

    /// <summary>
    /// Wraps a session control. The control is added to this pane and belongs
    /// to it from here on.
    /// </summary>
    public RdpSessionPane(RdpSessionControl session)
    {
        ArgumentNullException.ThrowIfNull(session);

        _session = session;

        // Off until something needs it. ApplyLayout owns this from here.
        AutoScroll = false;

        _session.SignalReceived += OnSignalReceived;

        Controls.Add(_session);
        ApplyLayout();
    }

    /// <summary>The control being framed.</summary>
    public RdpSessionControl Session => _session;

    /// <summary>
    /// Whether the picture is scaled to fit rather than scrolled. The v1
    /// default, and the default here — see <see cref="SessionScaling"/> for
    /// why, and for what it costs.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool SmartSizing
    {
        get => _session.SmartSizing;
        set
        {
            if (_session.SmartSizing == value)
            {
                return;
            }

            _session.SmartSizing = value;

            // Back to the top left. A session that was scrolled to its bottom
            // right and is now scaled to fit has nowhere to be scrolled to,
            // and WinForms will otherwise keep the offset for the next time
            // smart sizing is turned off.
            AutoScrollPosition = Point.Empty;

            ApplyLayout();
        }
    }

    /// <summary>
    /// The resolution of the remote desktop. Set from the connection's
    /// settings before connecting, and replaced by
    /// <see cref="RefreshSessionSize"/> with what the far end actually agreed
    /// to once it has.
    ///
    /// <see cref="PixelSize.Empty"/> means not known yet, and nothing is drawn.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public PixelSize SessionSize
    {
        get => _sessionSize;
        set
        {
            if (_sessionSize == value)
            {
                return;
            }

            _sessionSize = value;
            ApplyLayout();
        }
    }

    /// <summary>Where the session sits and how much it is being scaled, as last worked out.</summary>
    public SessionPlacement Placement { get; private set; } = SessionPlacement.Nowhere;

    /// <summary>
    /// Raised when <see cref="Placement"/> actually changes — not on every
    /// layout pass, of which there are many that arrive at the same answer.
    /// The status bar's percentage (M5-17) is the subscriber.
    /// </summary>
    public event EventHandler? PlacementChanged;

    /// <summary>
    /// Asks the control what resolution the session actually ended up with.
    /// Called when it connects, because that is the moment the answer stops
    /// being a request and starts being a fact.
    /// </summary>
    public void RefreshSessionSize()
    {
        PixelSize negotiated = _session.DesktopSize;

        if (!negotiated.IsEmpty)
        {
            SessionSize = negotiated;
        }
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        ApplyLayout();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _session.SignalReceived -= OnSignalReceived;
        }

        base.Dispose(disposing);
    }

    private void OnSignalReceived(object? sender, SessionSignalEventArgs e)
    {
        if (e.Signal == SessionSignal.Connected)
        {
            RefreshSessionSize();
        }
    }

    private void ApplyLayout()
    {
        // Setting a child's bounds starts another layout pass, and this one
        // would arrive at the same answer and start a third.
        if (_applying)
        {
            return;
        }

        _applying = true;

        try
        {
            // ClientSize, not Size: it is what is left after any scrollbar,
            // which is the space there actually is.
            SessionPlacement placement = SessionScaling.Place(
                _sessionSize,
                new PixelSize(ClientSize.Width, ClientSize.Height),
                _session.SmartSizing);

            // A bar left over from the last size is still taking space out of
            // the client area, and WinForms will not take it away by itself
            // until a layout pass this one is about to suppress. So it goes
            // now, and the fitting is done again against the space that
            // reappears — otherwise switching back to smart sizing leaves a
            // scrollbar sitting under a picture that no longer needs one.
            if (AutoScroll != placement.NeedsScrolling)
            {
                AutoScroll = placement.NeedsScrolling;

                placement = SessionScaling.Place(
                    _sessionSize,
                    new PixelSize(ClientSize.Width, ClientSize.Height),
                    _session.SmartSizing);
            }

            bool moved = Placement != placement;
            Placement = placement;

            if (moved)
            {
                PlacementChanged?.Invoke(this, EventArgs.Empty);
            }

            if (Placement.Bounds.IsEmpty)
            {
                _session.Visible = false;
                return;
            }

            // Offset by the display rectangle rather than by nothing, because
            // in a scrolled pane the origin is not where the client area
            // starts — placing children at raw client coordinates is what
            // makes a scrolled control refuse to move.
            Point origin = DisplayRectangle.Location;

            _session.Bounds = new Rectangle(
                origin.X + Placement.Bounds.X,
                origin.Y + Placement.Bounds.Y,
                Placement.Bounds.Width,
                Placement.Bounds.Height);

            _session.Visible = true;
        }
        finally
        {
            _applying = false;
        }
    }
}
