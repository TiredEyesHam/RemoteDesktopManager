using System.Globalization;
using Patchbay.Core.Inheritance;
using Patchbay.Core.Model;

namespace Patchbay.App.ViewModels;

/// <summary>
/// Turns setting values into words.
///
/// Enum member names are written for the compiler and read badly on screen —
/// "WhenDirectFails" and "PlayLocally" are not sentences. Splitting them on
/// capitals gets close but produces "Do Not Play" and "True Colour32", so the
/// wording is written out here instead. It is a short list and it only grows
/// when a setting is added.
/// </summary>
public static class SettingDisplay
{
    /// <summary>Shown wherever a value has not been set at any level.</summary>
    public const string NotSet = "Not set";

    private static readonly Dictionary<object, string> Labels = new()
    {
        [CredentialMode.Prompt] = "Ask each time",
        [CredentialMode.Profile] = "Use a saved credential",
        [CredentialMode.CurrentUser] = "Use the signed-in Windows account",

        [GatewayUsage.None] = "Connect directly",
        [GatewayUsage.Always] = "Always use the gateway",
        [GatewayUsage.WhenDirectFails] = "Only if connecting directly fails",

        [ColourDepth.HighColour15] = "15-bit",
        [ColourDepth.HighColour16] = "16-bit",
        [ColourDepth.TrueColour24] = "24-bit",
        [ColourDepth.TrueColour32] = "32-bit, full colour",

        [AudioMode.PlayLocally] = "Play on this computer",
        [AudioMode.PlayRemotely] = "Play on the remote computer",
        [AudioMode.DoNotPlay] = "Do not play",

        [AudioQuality.Dynamic] = "Let the server decide",
        [AudioQuality.Medium] = "Medium",
        [AudioQuality.High] = "High",

        [ConnectionQuality.Detect] = "Detect it automatically",
        [ConnectionQuality.Modem] = "Modem",
        [ConnectionQuality.LowSpeedBroadband] = "Low-speed broadband",
        [ConnectionQuality.Satellite] = "Satellite",
        [ConnectionQuality.HighSpeedBroadband] = "High-speed broadband",
        [ConnectionQuality.Wan] = "Wide-area network",
        [ConnectionQuality.Lan] = "Local network",

        // The wording matters more here than anywhere else on this list. "Warn"
        // and "Require" are the two settings people confuse, and the confusion
        // is only expensive in one direction.
        [ServerAuthentication.Connect] = "Connect without checking",
        [ServerAuthentication.Require] = "Do not connect unless the server is proved",
        [ServerAuthentication.Warn] = "Warn me if the server cannot be proved",

        [GatewayCredentialSource.Password] = "A password",
        [GatewayCredentialSource.SmartCard] = "A smart card",
        [GatewayCredentialSource.Any] = "Whatever the gateway accepts",
    };

    /// <summary>Every value a choice setting can take, in declaration order.</summary>
    public static IReadOnlyList<object> ChoicesFor(SettingDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        return [.. Enum.GetValues(descriptor.ValueType).Cast<object>()];
    }

    /// <summary>The wording for a single value.</summary>
    public static string Describe(object? value, SettingDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        if (value is null)
        {
            return NotSet;
        }

        return descriptor.Kind switch
        {
            SettingKind.Boolean => value is true ? "On" : "Off",
            SettingKind.Choice => Describe(value),
            SettingKind.Hidden => "Configured",
            _ => System.Convert.ToString(value, CultureInfo.CurrentCulture) ?? NotSet,
        };
    }

    /// <summary>The wording for an enum value, falling back to its name.</summary>
    public static string Describe(object value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return Labels.TryGetValue(value, out string? label)
            ? label
            : value.ToString() ?? string.Empty;
    }
}
