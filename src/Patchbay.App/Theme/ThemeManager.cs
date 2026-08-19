using System.Windows;
using Microsoft.Win32;

namespace Patchbay.App.Theme;

public enum AppTheme
{
    /// <summary>Follow whatever Windows is set to.</summary>
    System = 0,
    Light = 1,
    Dark = 2,
}

/// <summary>
/// Swaps the palette dictionary at runtime.
///
/// The palette is always the first entry in the application's merged
/// dictionaries, and it is replaced wholesale rather than edited. Every style
/// reaches its colours through <c>DynamicResource</c>, so replacing the
/// dictionary repaints the window without anything having to be told.
/// </summary>
public static class ThemeManager
{
    private const int PaletteIndex = 0;

    private const string PersonalizeKey =
        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public static AppTheme Preference { get; private set; } = AppTheme.System;

    /// <summary>What is actually on screen, with System resolved.</summary>
    public static AppTheme Resolved =>
        Preference is AppTheme.System
            ? (SystemPrefersDark() ? AppTheme.Dark : AppTheme.Light)
            : Preference;

    public static void Apply(AppTheme theme)
    {
        Preference = theme;

        Uri source = new(
            Resolved is AppTheme.Dark
                ? "Theme/Palette.Dark.xaml"
                : "Theme/Palette.Light.xaml",
            UriKind.Relative);

        Application.Current.Resources.MergedDictionaries[PaletteIndex] =
            new ResourceDictionary { Source = source };
    }

    /// <summary>Flips between light and dark, resolving System first.</summary>
    public static void Toggle() =>
        Apply(Resolved is AppTheme.Dark ? AppTheme.Light : AppTheme.Dark);

    /// <summary>
    /// Reads the Windows apps-use-light-theme setting. Absent on older builds,
    /// in which case light is the right assumption.
    /// </summary>
    public static bool SystemPrefersDark()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);

            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
