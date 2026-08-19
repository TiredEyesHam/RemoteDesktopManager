using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Patchbay.App.Interop;

/// <summary>
/// Tells the desktop window manager to draw the title bar dark.
///
/// Without this a dark application sits under a white caption bar, which is
/// the sort of detail that makes a window look assembled rather than designed.
/// The attribute is unsupported before Windows 10 1809 and had a different
/// number before 20H1, so both are tried and failure is ignored — an
/// unthemed title bar is a cosmetic loss, not a reason to fail startup.
/// </summary>
public static class WindowTheming
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19;

    public static void SetDarkTitleBar(Window window, bool dark)
    {
        ArgumentNullException.ThrowIfNull(window);

        IntPtr handle = new WindowInteropHelper(window).Handle;

        if (handle == IntPtr.Zero)
        {
            // Called before the window has a handle; nothing to theme yet.
            return;
        }

        int value = dark ? 1 : 0;

        if (TrySet(handle, DwmwaUseImmersiveDarkMode, value) != 0)
        {
            TrySet(handle, DwmwaUseImmersiveDarkModeBefore20H1, value);
        }
    }

    private static int TrySet(IntPtr handle, int attribute, int value)
    {
        try
        {
            return DwmSetWindowAttribute(handle, attribute, ref value, sizeof(int));
        }
        catch (DllNotFoundException)
        {
            return -1;
        }
        catch (EntryPointNotFoundException)
        {
            return -1;
        }
    }

    [DllImport("dwmapi.dll", SetLastError = true)]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int attribute,
        ref int value,
        int size);
}
