using System.IO;
using System.Windows;
using Microsoft.Win32;
using Patchbay.App.Theme;
using Patchbay.App.ViewModels;
using Patchbay.Core.Sessions;
using Patchbay.Core.Storage;
using Patchbay.Rdp.Hosting;

namespace Patchbay.App;

public partial class App : Application
{
    /// <summary>
    /// Where the connection document lives by default. Roaming application
    /// data, so it follows a domain profile between machines — which is
    /// exactly what someone with a list of servers expects it to do.
    /// </summary>
    public static string DefaultDocumentPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Patchbay",
        "connections.json");

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ThemeManager.Apply(AppTheme.System);

        // A path on the command line opens that document instead. Enough for
        // "open with", and for running against a scratch file without
        // disturbing the real one.
        string path = e.Args.Length > 0 && !string.IsNullOrWhiteSpace(e.Args[0])
            ? e.Args[0]
            : DefaultDocumentPath;

        FileConnectionStore store = new(path);

        // The real control when this machine has one, and the fake when it does
        // not. The probe proves the class id by creating one, so a host that
        // comes back is a host that works — and a machine with no usable
        // control still gets a tree, an editor and an import, with every
        // session saying in amber that nothing is really connected.
        IRemoteSessionHost sessionHost =
            (IRemoteSessionHost?)RdpRemoteSessionHost.TryCreate() ?? new FakeRemoteSessionHost
            {
                ConnectDelay = TimeSpan.FromSeconds(1.2),
                DisconnectDelay = TimeSpan.FromMilliseconds(250),
                SimulatedLatency = TimeSpan.FromMilliseconds(28),
            };

        ShellViewModel shell = new(store, ChooseRdgFile, sessionHost);

        MainWindow window = new(shell);
        MainWindow = window;
        window.Show();

        // Shown first, then filled. Reading the document is fast, but a window
        // that appears only after the disk has answered feels broken on a slow
        // profile share, and this one may well be on one.
        await shell.InitialiseAsync();
    }

    /// <summary>
    /// Asks for an RDCMan file. Lives here rather than in the view model
    /// because it is a WPF dialog, and returns null when the person changes
    /// their mind.
    /// </summary>
    private static string? ChooseRdgFile()
    {
        OpenFileDialog dialog = new()
        {
            Title = "Import from Remote Desktop Connection Manager",
            Filter = "RDCMan files (*.rdg)|*.rdg|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
        };

        return dialog.ShowDialog() is true ? dialog.FileName : null;
    }
}
