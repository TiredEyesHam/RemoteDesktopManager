using System.Text.Json.Serialization;

namespace Patchbay.Core.Model;

/// <summary>
/// Every setting a connection can carry. Each property is nullable, and null
/// carries meaning: <em>inherit from the nearest ancestor that sets it</em>.
/// A non-null value is an override that stops the search.
///
/// That single convention is what makes group inheritance work, so it holds
/// everywhere without exception — including for value types, which is why
/// even <see cref="Port"/> and the booleans are nullable.
///
/// Adding a property here needs no change to the resolver or the serialiser.
/// <c>SettingsResolverTests.Every_setting_participates_in_inheritance</c>
/// fails if a new property is not reachable, so the two stay in step.
/// </summary>
public sealed class ConnectionSettings
{
    // ── Connection ──────────────────────────────────────────────────────
    public int? Port { get; set; }

    public int? ConnectTimeoutSeconds { get; set; }

    public bool? ConnectToConsole { get; set; }

    /// <summary>
    /// Whether a session that breaks is brought back on its own (M4-08). Off
    /// for a machine where a second session displaces the first and somebody
    /// would rather decide for themselves; on everywhere else.
    /// </summary>
    public bool? AutoReconnect { get; set; }

    // ── Credentials ─────────────────────────────────────────────────────
    public string? UserName { get; set; }

    public string? Domain { get; set; }

    /// <summary>
    /// Points at a named credential profile rather than holding a secret.
    /// Passwords never live in this object and never reach the document file
    /// in the clear — see M3-02.
    /// </summary>
    public Guid? CredentialProfileId { get; set; }

    public CredentialMode? CredentialMode { get; set; }

    // ── Gateway ─────────────────────────────────────────────────────────
    public string? GatewayHostName { get; set; }

    public GatewayUsage? GatewayUsage { get; set; }

    /// <summary>
    /// The account the <em>gateway</em> accepts, which is routinely not the
    /// account the machine behind it accepts (M4-11). Null while
    /// <see cref="GatewayUseSameCredentials"/> is on, which is the usual case.
    /// </summary>
    public string? GatewayUserName { get; set; }

    /// <summary>The domain that goes with <see cref="GatewayUserName"/>.</summary>
    public string? GatewayDomain { get; set; }

    /// <summary>What the gateway will take as proof (M4-11).</summary>
    public GatewayCredentialSource? GatewayCredentialSource { get; set; }

    /// <summary>
    /// Whether the gateway is offered the same credentials as the machine
    /// behind it (M4-11). True is both the control's default and the ordinary
    /// arrangement — one domain account that the gateway and the server both
    /// know — and it is what stops the gateway asking for a second password
    /// nobody has been given.
    /// </summary>
    public bool? GatewayUseSameCredentials { get; set; }

    // ── Display ─────────────────────────────────────────────────────────
    public int? DesktopWidth { get; set; }

    public int? DesktopHeight { get; set; }

    public bool? UseSmartSizing { get; set; }

    public ColourDepth? ColourDepth { get; set; }

    // ── Local resources ─────────────────────────────────────────────────
    public bool? RedirectClipboard { get; set; }

    public bool? RedirectDrives { get; set; }

    public bool? RedirectPrinters { get; set; }

    public AudioMode? AudioMode { get; set; }

    /// <summary>Whether the far end can record from this computer's microphone (M4-13).</summary>
    public bool? RedirectMicrophone { get; set; }

    /// <summary>How much bandwidth remote audio may spend (M4-13).</summary>
    public AudioQuality? AudioQuality { get; set; }

    /// <summary>
    /// Whether a smart card reader on this computer is offered to the session
    /// (M4-13). Off by default and deliberately so: it is the one redirection
    /// that hands the far end something it can authenticate with.
    /// </summary>
    public bool? RedirectSmartCards { get; set; }

    /// <summary>Whether serial and parallel ports are offered to the session (M4-13).</summary>
    public bool? RedirectPorts { get; set; }

    /// <summary>
    /// Whether supported plug-and-play devices are offered to the session
    /// (M4-13) — cameras, media players, and the other USB devices the control
    /// knows how to forward. Not a general USB passthrough, which the control
    /// does not do.
    /// </summary>
    public bool? RedirectDevices { get; set; }

    /// <summary>Whether point-of-sale devices are offered to the session (M4-13).</summary>
    public bool? RedirectPointOfSaleDevices { get; set; }

    // ── Experience ──────────────────────────────────────────────────────

    /// <summary>
    /// What sort of link this is, as a hint to the server (M4-14).
    /// <see cref="Model.ConnectionQuality.Detect"/> asks the control to
    /// measure instead, which is a different property and not a value.
    /// </summary>
    public ConnectionQuality? ConnectionQuality { get; set; }

    /// <summary>Whether the remote desktop's wallpaper is drawn (M4-14).</summary>
    public bool? DesktopBackground { get; set; }

    /// <summary>Whether text is anti-aliased (M4-14). Cheap, and the difference is very visible.</summary>
    public bool? FontSmoothing { get; set; }

    /// <summary>Whether the remote desktop's window composition runs (M4-14). Expensive.</summary>
    public bool? DesktopComposition { get; set; }

    /// <summary>Whether a window being dragged is drawn, rather than an outline (M4-14).</summary>
    public bool? ShowWindowContentsWhileDragging { get; set; }

    /// <summary>Whether menus fade and slide (M4-14).</summary>
    public bool? MenuAnimations { get; set; }

    /// <summary>Whether the remote desktop's theme is drawn, rather than the classic look (M4-14).</summary>
    public bool? VisualStyles { get; set; }

    /// <summary>
    /// Whether the bitmap cache is kept on disk between sessions (M4-14). It
    /// makes reconnecting to a familiar machine noticeably quicker, at the
    /// price of a cache directory holding fragments of what was on screen.
    /// </summary>
    public bool? PersistentBitmapCache { get; set; }

    // ── Security ────────────────────────────────────────────────────────

    /// <summary>What to do about a server that cannot prove who it is (M4-09).</summary>
    public ServerAuthentication? ServerAuthentication { get; set; }

    // ── Advanced ────────────────────────────────────────────────────────

    /// <summary>
    /// How often the client sends a keep-alive, in seconds; zero switches it
    /// off (M4-15).
    ///
    /// Without one, a session whose server has vanished sits there looking
    /// connected until somebody types into it. With one, the drop is noticed
    /// within an interval, which gives M4-08 something to reconnect from.
    /// </summary>
    public int? KeepAliveIntervalSeconds { get; set; }

    /// <summary>
    /// How long the session may sit idle before the client ends it, in
    /// minutes; zero means never (M4-15). A local rule, not a server policy —
    /// the server has its own and this does not replace it.
    /// </summary>
    public int? IdleTimeoutMinutes { get; set; }

    /// <summary>
    /// The values used when nothing in a node's ancestry sets a property.
    /// These mirror what <c>mstsc.exe</c> does out of the box, so a connection
    /// with no configuration at all still behaves the way people expect.
    ///
    /// A few properties are deliberately left null here, because absence is
    /// their correct resting state — there is no sensible default user name or
    /// gateway, and inventing an empty string for one would lose the
    /// difference between "not configured" and "configured as blank". Those
    /// properties are listed in <see cref="WithoutDefaults"/>, and consumers
    /// must handle them being null even after resolution.
    /// </summary>
    [JsonIgnore]
    public static ConnectionSettings Defaults => new()
    {
        Port = 3389,
        ConnectTimeoutSeconds = 15,
        ConnectToConsole = false,
        AutoReconnect = true,
        UserName = null,
        Domain = null,
        CredentialProfileId = null,
        CredentialMode = Model.CredentialMode.Prompt,
        GatewayHostName = null,
        GatewayUsage = Model.GatewayUsage.None,
        GatewayUserName = null,
        GatewayDomain = null,
        GatewayCredentialSource = Model.GatewayCredentialSource.Password,
        GatewayUseSameCredentials = true,
        DesktopWidth = 1920,
        DesktopHeight = 1080,
        UseSmartSizing = true,
        ColourDepth = Model.ColourDepth.TrueColour32,
        RedirectClipboard = true,
        RedirectDrives = false,
        RedirectPrinters = false,
        AudioMode = Model.AudioMode.PlayLocally,
        RedirectMicrophone = false,
        AudioQuality = Model.AudioQuality.Dynamic,
        RedirectSmartCards = false,
        RedirectPorts = false,
        RedirectDevices = false,
        RedirectPointOfSaleDevices = false,
        ConnectionQuality = Model.ConnectionQuality.Detect,
        DesktopBackground = false,
        FontSmoothing = true,
        DesktopComposition = false,
        ShowWindowContentsWhileDragging = false,
        MenuAnimations = false,
        VisualStyles = true,
        PersistentBitmapCache = true,
        ServerAuthentication = Model.ServerAuthentication.Warn,
        KeepAliveIntervalSeconds = 60,
        IdleTimeoutMinutes = 0,
    };

    /// <summary>
    /// Settings that have no built-in default and stay null after resolution
    /// unless something in the ancestry sets them.
    ///
    /// This list is asserted against <see cref="Defaults"/> by
    /// <c>SettingsResolverTests.Settings_without_a_default_are_the_expected_ones</c>,
    /// so adding a property forces a deliberate choice: give it a default, or
    /// add it here. Silently forgetting is the one option not available.
    /// </summary>
    public static IReadOnlySet<string> WithoutDefaults { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            nameof(UserName),
            nameof(Domain),
            nameof(CredentialProfileId),
            nameof(GatewayHostName),
            nameof(GatewayUserName),
            nameof(GatewayDomain),
        };

    /// <summary>Shallow copy. Used by duplicate (M2-09) and by the resolver.</summary>
    public ConnectionSettings Clone() => (ConnectionSettings)MemberwiseClone();
}
