using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Windows.Forms;
using Patchbay.Core.Sessions;
using Patchbay.Rdp.Interop;

namespace Patchbay.Rdp.Hosting;

/// <summary>
/// The RDP control as a WinForms control, so it can be given a window (M4-03).
///
/// The ActiveX control cannot be drawn into a surface someone else owns. It
/// wants a real HWND, and the airspace rules and docked-rather-than-modal
/// prompts all follow from that. <see cref="AxHost"/> supplies one; this is
/// the smallest subclass that does the job.
///
/// The COM object does not exist until the handle does, which is why
/// <see cref="Client"/> is null before <see cref="EnsureCreated"/>.
/// </summary>
[DesignerCategory("")]
public sealed class RdpSessionControl : AxHost
{
    private readonly RdpEngineInfo _engine;

    private ConnectionPointCookie? _events;
    private RdpSettingsObjects? _settings;
    private bool _smartSizing = true;

    /// <summary>
    /// Creates a host for <paramref name="engine"/>, which should have come
    /// from <see cref="RdpEngineProbe.Detect"/> so that the class id is one
    /// that has already been proved creatable on this machine.
    /// </summary>
    public RdpSessionControl(RdpEngineInfo engine)
        : base(ClassIdOf(engine))
    {
        _engine = engine;
    }

    /// <summary>
    /// The control, once it exists. Null until the handle is created, which
    /// <see cref="EnsureCreated"/> forces and adding it to a visible parent
    /// does on its own.
    /// </summary>
    public RdpClientInstance? Client { get; private set; }

    /// <summary>
    /// Whether the control scales its picture to the window it is given
    /// instead of drawing it pixel for pixel (M5-09).
    ///
    /// Scaling fills the window whatever shape it is, so this is half of smart
    /// sizing. The other half is giving the control a window of the session's
    /// own shape, which is <see cref="RdpSessionPane"/>'s job; setting this
    /// alone and handing over the whole pane produces a stretched desktop.
    ///
    /// Kept here as well as on the control, because the preference is chosen
    /// before the handle exists.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool SmartSizing
    {
        get => _smartSizing;
        set
        {
            _smartSizing = value;
            ApplySmartSizing();
        }
    }

    /// <summary>
    /// The size of the remote desktop as the control understands it: what was
    /// asked for before a connection, what was agreed after one. The far end
    /// may give a different answer, and laying a session out against the
    /// requested size is off by exactly that difference.
    ///
    /// <see cref="PixelSize.Empty"/> before the control exists.
    /// </summary>
    public PixelSize DesktopSize => Client is null
        ? PixelSize.Empty
        : new PixelSize(Client.GetProperty<int>("DesktopWidth"), Client.GetProperty<int>("DesktopHeight"));

    /// <summary>
    /// How the far end proved itself, as the control reports it (M4-09), or
    /// <see cref="SessionSecurity.Unknown"/> when it has not said.
    ///
    /// Read when wanted rather than cached. <c>AuthenticationType</c>
    /// describes the connection that exists, and a control that has not
    /// connected returns the same zero as a connection with no authentication
    /// at all; <see cref="RdpAuthenticationType"/> deals with that.
    /// </summary>
    public SessionSecurity NegotiatedSecurity
    {
        get
        {
            if (Settings()?.Resolve(RdpSettingTarget.AdvancedSettings) is not { } settings)
            {
                return SessionSecurity.Unknown;
            }

            try
            {
                return RdpAuthenticationType.ToSecurity(
                    RdpDispatch.Get<int>(settings, "AuthenticationType"));
            }
            catch (RdpEngineException)
            {
                // A control older than the generation that introduced it, or
                // one that would not answer. Not knowing is an ordinary answer
                // to this question and the status bar already has words for it.
                return SessionSecurity.Unknown;
            }
        }
    }

    /// <summary>Raised when the COM object first becomes available.</summary>
    public event EventHandler? ClientAttached;

    /// <summary>
    /// Everything the control announces about its session (M4-06), in
    /// <c>Core</c>'s vocabulary so that handlers need not know an ActiveX
    /// control is involved.
    ///
    /// Raised on the control's own thread from inside its call frame. See
    /// <see cref="RdpEventSink"/> for why handlers should be short.
    /// </summary>
    public event EventHandler<SessionSignalEventArgs>? SignalReceived;

    /// <summary>
    /// Forces the handle, and with it the COM object, into existence.
    /// </summary>
    /// <exception cref="RdpEngineException">The control could not be created.</exception>
    public RdpClientInstance EnsureCreated()
    {
        if (Client is null)
        {
            CreateControl();
        }

        return Client ?? throw new RdpEngineException(
            "The RDP control was created but never handed over its COM object. "
            + "That usually means the ActiveX registration is damaged.");
    }

    /// <summary>
    /// Called by <see cref="AxHost"/> once the OCX is live. The instance is
    /// marked as not owned: WinForms created it and WinForms will release it,
    /// and releasing it twice takes the window with it.
    /// </summary>
    protected override void AttachInterfaces()
    {
        object? ocx = GetOcx();

        if (ocx is null)
        {
            return;
        }

        Client = new RdpClientInstance(ocx, _engine, ownsComObject: false);

        // Whatever was chosen before there was anything to choose it on.
        ApplySmartSizing();

        ClientAttached?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Configures the control from a connection's resolved settings (M4-04)
    /// and reports what this generation would not take.
    ///
    /// Call before connecting. Most of these properties are read once as the
    /// connection is made, so applying them to a live session succeeds and
    /// changes nothing.
    ///
    /// Smart sizing is deliberately not in the plan; it belongs to
    /// <see cref="SmartSizing"/> once a tab can toggle it (M5-09), and a
    /// reconnect that put the saved value back would undo what somebody just
    /// chose.
    /// </summary>
    /// <exception cref="RdpEngineException">The control could not be created.</exception>
    public RdpSettingsReport ApplySettings(SessionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        RdpClientInstance client = EnsureCreated();

        return RdpSettingsApplier.Apply(client, RdpSettingsMapper.Plan(request));
    }

    /// <summary>
    /// The describer to give <see cref="SessionSignalRouter"/> (M4-07), wired
    /// to this control so a disconnect is explained in Microsoft's own words
    /// and Windows' own language.
    ///
    /// Reads the control late rather than capturing it: a reason is only
    /// meaningful when the session ends, and by then this control may have gone
    /// away. A null client means Patchbay answers with the code.
    /// </summary>
    public SessionReasons CreateReasons() => new(reason => Client?.DescribeDisconnect(reason));

    /// <summary>
    /// The settings objects hanging off this control, or null before the
    /// control exists. One instance, so a plan of twenty writes does not walk
    /// the generation names twenty times.
    /// </summary>
    private RdpSettingsObjects? Settings()
    {
        if (Client is null)
        {
            return null;
        }

        return _settings ??= new RdpSettingsObjects(Client);
    }

    /// <summary>
    /// Pushes <see cref="SmartSizing"/> at the control, if there is one yet.
    /// A generation without the setting is not worth stopping for: the picture
    /// is simply not scaled, which is visible.
    /// </summary>
    private void ApplySmartSizing()
    {
        if (Settings()?.Resolve(RdpSettingTarget.AdvancedSettings) is not { } settings)
        {
            return;
        }

        try
        {
            RdpDispatch.Set(settings, "SmartSizing", _smartSizing);
        }
        catch (RdpEngineException)
        {
            // Kept in _smartSizing, so the pane still letterboxes and the next
            // connection tries again.
        }
    }

    /// <summary>
    /// Called by <see cref="AxHost"/> once there is an object to listen to.
    /// The connection point is what makes the control call back at all; before
    /// this runs, a session can connect and disconnect in silence.
    /// </summary>
    protected override void CreateSink()
    {
        if (Client is null || _events is not null)
        {
            return;
        }

        _events = new ConnectionPointCookie(
            Client.ComObject,
            new RdpEventSink(RaiseSignal),
            typeof(IMsTscAxEvents));
    }

    /// <summary>
    /// Stops listening. Called by <see cref="AxHost"/> when the window goes
    /// away, and again on dispose; disconnecting twice is not an error, but
    /// leaving it connected would keep the control holding a reference to a
    /// sink that points back at a disposed control.
    /// </summary>
    protected override void DetachSink()
    {
        _events?.Disconnect();
        _events = null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DetachSink();
            Client?.Dispose();
            Client = null;

            // Dropped, not released: they are the control's own settings
            // objects, and the control is WinForms' to release.
            _settings = null;
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// Hands one announcement to whoever is listening.
    ///
    /// A handler that throws would unwind into native code, where the
    /// exception becomes an HRESULT the control discards and the session
    /// carries on in a state nobody chose. Caught and rethrown from the
    /// message loop instead, where it surfaces normally with its stack intact.
    /// </summary>
    private void RaiseSignal(SessionSignalEventArgs announcement)
    {
        EventHandler<SessionSignalEventArgs>? handler = SignalReceived;

        if (handler is null)
        {
            return;
        }

        try
        {
            handler(this, announcement);
        }
        catch (Exception ex)
        {
            ExceptionDispatchInfo captured = ExceptionDispatchInfo.Capture(ex);

            if (IsHandleCreated)
            {
                BeginInvoke(captured.Throw);
            }
            else
            {
                captured.Throw();
            }
        }
    }

    /// <summary>
    /// <see cref="AxHost"/> takes the class id as a string, and it has to be
    /// handed to the base constructor before any field is set, so the null
    /// check cannot live in the constructor body.
    /// </summary>
    private static string ClassIdOf(RdpEngineInfo engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        return engine.ClassId.ToString();
    }
}
