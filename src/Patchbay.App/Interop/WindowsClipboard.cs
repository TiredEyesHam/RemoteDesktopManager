using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using Patchbay.Core.Security;

namespace Patchbay.App.Interop;

/// <summary>
/// The Windows clipboard, with the two flags that matter for a password
/// (M3-09).
///
/// <para>
/// Since Windows 10 1809 the clipboard is not one slot. What is copied goes
/// into clipboard history, which survives being cleared and is readable from
/// Win+V for as long as the session lasts, and it is uploaded to the cloud
/// clipboard and pushed to the person's other machines if they have that
/// turned on. A thirty-second countdown against the current slot is no defence
/// against either.
/// </para>
///
/// <para>
/// Both are opted out of by putting extra formats on the data object.
/// Undocumented-looking and entirely documented: <c>CanIncludeInClipboardHistory</c>
/// and <c>CanUploadToCloudClipboard</c> hold a <c>DWORD</c> of zero, and
/// <c>ExcludeClipboardContentFromMonitorProcessing</c> asks clipboard monitors
/// not to process the contents at all. They are set on the data object rather
/// than called as an API, which is why the text and the flags have to go on in
/// one <see cref="Clipboard.SetDataObject(object, bool)"/> and cannot be added
/// afterwards.
/// </para>
///
/// <para>
/// Every call here can fail, because the clipboard is a shared resource that
/// another process can hold open, and none of these failures is exceptional.
/// They come back as false and <see cref="SecretClipboard"/> decides what to
/// do about it — which for a failed clear means trying again rather than
/// leaving a password sitting there.
/// </para>
/// </summary>
public sealed class WindowsClipboard : ISystemClipboard
{
    /// <summary>Keeps it out of the Win+V history, which outlives the clear.</summary>
    private const string ExcludeFromHistory = "CanIncludeInClipboardHistory";

    /// <summary>Keeps it off the person's other machines.</summary>
    private const string ExcludeFromCloud = "CanUploadToCloudClipboard";

    /// <summary>Asks clipboard managers not to process the contents.</summary>
    private const string ExcludeFromMonitors = "ExcludeClipboardContentFromMonitorProcessing";

    /// <inheritdoc />
    public bool IsAvailable => true;

    /// <inheritdoc />
    public long Token => GetClipboardSequenceNumber();

    /// <inheritdoc />
    public bool SetSecret(Secret secret)
    {
        ArgumentNullException.ThrowIfNull(secret);

        DataObject data = new();

        // The one place a copied password becomes a string, and it is handed
        // straight to the clipboard (M3-03). The clipboard's own copy is the
        // one the countdown is about.
        data.SetText(secret.RevealAsString());

        data.SetData(ExcludeFromHistory, Dword(0));
        data.SetData(ExcludeFromCloud, Dword(0));
        data.SetData(ExcludeFromMonitors, Dword(0));

        return Put(data);
    }

    /// <inheritdoc />
    public bool SetText(string text)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);

        DataObject data = new();
        data.SetText(text);

        return Put(data);
    }

    /// <inheritdoc />
    public bool Clear()
    {
        try
        {
            Clipboard.Clear();
            return true;
        }
        catch (ExternalException)
        {
            return false;
        }
    }

    private static bool Put(DataObject data)
    {
        try
        {
            Clipboard.SetDataObject(data, copy: true);
            return true;
        }
        catch (ExternalException)
        {
            return false;
        }
    }

    /// <summary>
    /// A four-byte zero, which is what these formats are read as. A stream
    /// rather than an <c>int</c>, because the clipboard stores formats it does
    /// not know about as bytes and an <c>int</c> would be serialised as
    /// something else entirely.
    /// </summary>
    private static MemoryStream Dword(int value) => new(BitConverter.GetBytes(value));

    /// <summary>
    /// Increments every time anything is put on the clipboard, by any process.
    /// Cheap, needs no clipboard lock, and answers the only question
    /// <see cref="SecretClipboard"/> has: is what I put there still there?
    /// </summary>
    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();
}
