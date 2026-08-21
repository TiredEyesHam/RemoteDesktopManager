using System.IO;
using System.Reflection;
using System.Windows;
using Microsoft.Win32;
using Patchbay.App.Diagnostics;
using Patchbay.App.Interop;
using Patchbay.App.Security;
using Patchbay.App.Theme;
using Patchbay.App.ViewModels;
using Patchbay.Core.Sessions;
using Patchbay.Core.Storage;
using Patchbay.Rdp.Hosting;
using Serilog;

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

        // First, so that anything going wrong after this has somewhere to be
        // written down. The logger is redacted by construction (M3-08) —
        // PatchbayLog has no way to make one that is not.
        AppLogging.Start();

        Log.Information(
            "Patchbay {Version} starting on {OSVersion}",
            Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown",
            Environment.OSVersion.VersionString);

        ThemeManager.Apply(AppTheme.System);

        // A path on the command line opens that document instead. Enough for
        // "open with", and for running against a scratch file without
        // disturbing the real one.
        string path = e.Args.Length > 0 && !string.IsNullOrWhiteSpace(e.Args[0])
            ? e.Args[0]
            : DefaultDocumentPath;

        Log.Information("Opening {DocumentPath}", path);

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

        Log.Information(
            "Session host is {Host}",
            sessionHost is RdpRemoteSessionHost real ? real.Engine.Description : "a simulation");

        // Real data protection here, unlike the view model's default: this is
        // the one place that knows it is running on the signed-in account
        // rather than in a test (M3-01).
        ShellViewModel shell = new(
            store,
            ChooseRdgFile,
            sessionHost,
            DpapiSecretProtector.ForCurrentUser(),
            new WindowsClipboard());

        MainWindow window = new(shell);
        MainWindow = window;
        window.Show();

        // Shown first, then filled. Reading the document is fast, but a window
        // that appears only after the disk has answered feels broken on a slow
        // profile share, and this one may well be on one.
        await shell.InitialiseAsync();

        // Worth a line, because it changes what every saved password does
        // next and it is the first thing to ask about when one will not work
        // (M3-07). No secret goes near this — the fact of a master password is
        // not one, and the redaction policy would not let one through anyway.
        Log.Information(
            "Document opened {Locked}",
            shell.IsDocumentLocked ? "locked by a master password" : "unlocked");
    }

    /// <summary>
    /// The file sink buffers, so a run that is not closed down loses its last
    /// few lines — which are the ones somebody is usually looking for.
    /// </summary>
    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("Patchbay stopping");
        Log.CloseAndFlush();

        base.OnExit(e);
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
