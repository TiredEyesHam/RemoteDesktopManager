using System.Reflection;
using Patchbay.Core.Model;

namespace Patchbay.Core.Inheritance;

/// <summary>
/// The order, wording and grouping of settings as they appear on screen.
///
/// This is presentation metadata living in Core on purpose: it is the one
/// place a new setting has to be described, and
/// <c>SettingCatalogueTests.Every_setting_is_described_exactly_once</c> fails
/// if one is added to <see cref="ConnectionSettings"/> and not listed here.
/// Left in the view layer that guard could not exist, and a new setting would
/// simply never appear, which is a bug nobody notices for months.
/// </summary>
public static class SettingCatalogue
{
    /// <summary>
    /// The eight groups a setting can belong to (M1-03), in the order they are
    /// shown.
    ///
    /// <para>
    /// The order is not alphabetical and not the order the properties happen
    /// to be declared in — it runs from the settings somebody changes for
    /// every connection to the ones most people never open. Connection and
    /// Credentials are what a new entry needs before it will work at all;
    /// Gateway and Display are the two that get changed per site; Local
    /// resources, Experience and Security are the ones with real consequences
    /// that are set once for a group and inherited; Advanced is last because
    /// nothing in it changes whether a session works.
    /// </para>
    /// </summary>
    public const string ConnectionSection = "Connection";
    public const string CredentialsSection = "Credentials";
    public const string GatewaySection = "Gateway";
    public const string DisplaySection = "Display";
    public const string ResourcesSection = "Local resources";
    public const string ExperienceSection = "Experience";
    public const string SecuritySection = "Security";
    public const string AdvancedSection = "Advanced";

    /// <summary>Every setting, in the order it should be shown.</summary>
    public static IReadOnlyList<SettingDescriptor> All { get; } =
    [
        new(nameof(ConnectionSettings.Port), "Port", ConnectionSection, SettingKind.Number, typeof(int)),
        new(nameof(ConnectionSettings.ConnectTimeoutSeconds), "Connection timeout", ConnectionSection, SettingKind.Number, typeof(int), "Seconds to wait before giving up."),
        new(nameof(ConnectionSettings.ConnectToConsole), "Connect to console session", ConnectionSection, SettingKind.Boolean, typeof(bool)),
        new(nameof(ConnectionSettings.AutoReconnect), "Reconnect automatically", ConnectionSection, SettingKind.Boolean, typeof(bool), "Bring the session back on its own if it breaks."),

        new(nameof(ConnectionSettings.CredentialMode), "Credentials", CredentialsSection, SettingKind.Choice, typeof(CredentialMode)),
        new(nameof(ConnectionSettings.UserName), "User name", CredentialsSection, SettingKind.Text, typeof(string)),
        new(nameof(ConnectionSettings.Domain), "Domain", CredentialsSection, SettingKind.Text, typeof(string)),
        new(nameof(ConnectionSettings.CredentialProfileId), "Saved credential", CredentialsSection, SettingKind.Hidden, typeof(Guid)),

        new(nameof(ConnectionSettings.GatewayUsage), "Use a gateway", GatewaySection, SettingKind.Choice, typeof(GatewayUsage)),
        new(nameof(ConnectionSettings.GatewayHostName), "Gateway server", GatewaySection, SettingKind.Text, typeof(string)),
        new(nameof(ConnectionSettings.GatewayUseSameCredentials), "Use the same credentials", GatewaySection, SettingKind.Boolean, typeof(bool), "Offer the gateway the account configured above."),
        new(nameof(ConnectionSettings.GatewayCredentialSource), "Gateway sign-in", GatewaySection, SettingKind.Choice, typeof(GatewayCredentialSource)),
        new(nameof(ConnectionSettings.GatewayUserName), "Gateway user name", GatewaySection, SettingKind.Text, typeof(string)),
        new(nameof(ConnectionSettings.GatewayDomain), "Gateway domain", GatewaySection, SettingKind.Text, typeof(string)),

        new(nameof(ConnectionSettings.DesktopWidth), "Width", DisplaySection, SettingKind.Number, typeof(int)),
        new(nameof(ConnectionSettings.DesktopHeight), "Height", DisplaySection, SettingKind.Number, typeof(int)),
        new(nameof(ConnectionSettings.UseSmartSizing), "Scale to fit the window", DisplaySection, SettingKind.Boolean, typeof(bool)),
        new(nameof(ConnectionSettings.ColourDepth), "Colour depth", DisplaySection, SettingKind.Choice, typeof(ColourDepth)),

        new(nameof(ConnectionSettings.RedirectClipboard), "Clipboard", ResourcesSection, SettingKind.Boolean, typeof(bool)),
        new(nameof(ConnectionSettings.RedirectDrives), "Local drives", ResourcesSection, SettingKind.Boolean, typeof(bool)),
        new(nameof(ConnectionSettings.RedirectPrinters), "Printers", ResourcesSection, SettingKind.Boolean, typeof(bool)),
        new(nameof(ConnectionSettings.AudioMode), "Audio", ResourcesSection, SettingKind.Choice, typeof(AudioMode)),
        new(nameof(ConnectionSettings.AudioQuality), "Audio quality", ResourcesSection, SettingKind.Choice, typeof(AudioQuality)),
        new(nameof(ConnectionSettings.RedirectMicrophone), "Microphone", ResourcesSection, SettingKind.Boolean, typeof(bool), "Let the far end record from this computer."),
        new(nameof(ConnectionSettings.RedirectSmartCards), "Smart cards", ResourcesSection, SettingKind.Boolean, typeof(bool)),
        new(nameof(ConnectionSettings.RedirectPorts), "Serial and parallel ports", ResourcesSection, SettingKind.Boolean, typeof(bool)),
        new(nameof(ConnectionSettings.RedirectDevices), "Plug-and-play devices", ResourcesSection, SettingKind.Boolean, typeof(bool)),
        new(nameof(ConnectionSettings.RedirectPointOfSaleDevices), "Point-of-sale devices", ResourcesSection, SettingKind.Boolean, typeof(bool)),

        new(nameof(ConnectionSettings.ConnectionQuality), "Connection quality", ExperienceSection, SettingKind.Choice, typeof(ConnectionQuality), "How much the server may spend on how the desktop looks."),
        new(nameof(ConnectionSettings.DesktopBackground), "Desktop background", ExperienceSection, SettingKind.Boolean, typeof(bool)),
        new(nameof(ConnectionSettings.VisualStyles), "Visual styles", ExperienceSection, SettingKind.Boolean, typeof(bool)),
        new(nameof(ConnectionSettings.FontSmoothing), "Font smoothing", ExperienceSection, SettingKind.Boolean, typeof(bool)),
        new(nameof(ConnectionSettings.DesktopComposition), "Desktop composition", ExperienceSection, SettingKind.Boolean, typeof(bool)),
        new(nameof(ConnectionSettings.ShowWindowContentsWhileDragging), "Show window contents while dragging", ExperienceSection, SettingKind.Boolean, typeof(bool)),
        new(nameof(ConnectionSettings.MenuAnimations), "Menu animations", ExperienceSection, SettingKind.Boolean, typeof(bool)),
        new(nameof(ConnectionSettings.PersistentBitmapCache), "Keep the bitmap cache", ExperienceSection, SettingKind.Boolean, typeof(bool), "Quicker to reconnect, at the price of a cache on disk."),

        new(nameof(ConnectionSettings.ServerAuthentication), "Server authentication", SecuritySection, SettingKind.Choice, typeof(ServerAuthentication), "What to do about a server that cannot prove who it is."),

        new(nameof(ConnectionSettings.KeepAliveIntervalSeconds), "Keep-alive interval", AdvancedSection, SettingKind.Number, typeof(int), "Seconds between checks that the session is still there. Zero switches it off."),
        new(nameof(ConnectionSettings.IdleTimeoutMinutes), "Idle timeout", AdvancedSection, SettingKind.Number, typeof(int), "Minutes of inactivity before the session is closed. Zero means never."),
    ];

    /// <summary>Section headings, in display order, without repeats.</summary>
    public static IReadOnlyList<string> Sections { get; } =
        [.. All.Select(d => d.Section).Distinct(StringComparer.Ordinal)];

    /// <summary>The settings someone can actually edit, in display order.</summary>
    public static IReadOnlyList<SettingDescriptor> Editable { get; } =
        [.. All.Where(d => d.Kind is not SettingKind.Hidden)];

    /// <exception cref="ArgumentException">No such setting.</exception>
    public static SettingDescriptor For(string propertyName)
    {
        SettingDescriptor? descriptor = All.FirstOrDefault(
            d => string.Equals(d.PropertyName, propertyName, StringComparison.Ordinal));

        return descriptor ?? throw new ArgumentException(
            $"{propertyName} is not a described setting.", nameof(propertyName));
    }

    /// <summary>Reads a setting off a settings object by name.</summary>
    public static object? Read(ConnectionSettings settings, string propertyName)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return Property(propertyName).GetValue(settings);
    }

    /// <summary>
    /// Writes a setting by name. Null clears the override so the value is
    /// inherited again.
    /// </summary>
    public static void Write(ConnectionSettings settings, string propertyName, object? value)
    {
        ArgumentNullException.ThrowIfNull(settings);

        Property(propertyName).SetValue(settings, value);
    }

    private static PropertyInfo Property(string propertyName)
    {
        PropertyInfo? property = typeof(ConnectionSettings).GetProperty(
            propertyName,
            BindingFlags.Public | BindingFlags.Instance);

        return property ?? throw new ArgumentException(
            $"{propertyName} is not a setting on {nameof(ConnectionSettings)}.", nameof(propertyName));
    }
}
