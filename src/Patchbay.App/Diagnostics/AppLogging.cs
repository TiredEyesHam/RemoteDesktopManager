using System.Globalization;
using System.IO;
using System.Text;
using Patchbay.Core.Diagnostics;
using Serilog;
using Serilog.Core;

namespace Patchbay.App.Diagnostics;

/// <summary>
/// Where the log goes (M0-07).
///
/// <para>
/// <see cref="PatchbayLog"/> decides what a Patchbay logger does with a value;
/// this decides where the line ends up. The split is what lets the redaction
/// policy live in Core, where the tests can see it, without Core knowing what
/// <c>%LOCALAPPDATA%</c> is.
/// </para>
/// </summary>
internal static class AppLogging
{
    /// <summary>
    /// Local application data, not roaming, and deliberately the opposite of
    /// <see cref="App.DefaultDocumentPath"/>. The document is a list of
    /// machines somebody wants on whichever desktop they sign in to. A log is
    /// about one run on one machine, and following a profile share around
    /// would make it slower to write and harder to read.
    /// </summary>
    public static string Directory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Patchbay",
        "logs");

    /// <summary>
    /// The environment variable that sets the starting level, until there is a
    /// settings page to do it (M7-01).
    /// </summary>
    public const string LevelVariable = "PATCHBAY_LOG_LEVEL";

    /// <summary>
    /// A week. Retention is a security decision here rather than a disk one:
    /// once <c>M4-16</c> lands these files hold host names, account names and
    /// when somebody connected to what, which is the same map of an estate the
    /// threat model says not to hand around. Keeping a year of it would be
    /// keeping a year of that.
    /// </summary>
    private const int RetainedFiles = 7;

    private const long FileSizeLimit = 16L * 1024 * 1024;

    private const string OutputTemplate =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}";

    /// <summary>
    /// Starts logging and makes it the ambient <see cref="Log.Logger"/>, so
    /// that anything reaching for <c>Log.ForContext</c> later gets the redacted
    /// one rather than the silent default.
    ///
    /// <para>
    /// Nothing here is allowed to stop the application starting. A profile that
    /// will not let a file be created is a reason to run without a log, not a
    /// reason to refuse to run.
    /// </para>
    /// </summary>
    public static Logger Start()
    {
        PatchbayLog.ApplyEnvironmentLevel(Environment.GetEnvironmentVariable(LevelVariable));

        Logger logger;

        try
        {
            logger = PatchbayLog.Create(configuration => configuration.WriteTo.File(
                Path.Combine(Directory, "patchbay-.log"),
                outputTemplate: OutputTemplate,

                // Invariant, not the desktop's locale. A log is read next to
                // other logs, often from other machines, and a decimal comma
                // in one of them is a diff nobody wanted.
                formatProvider: CultureInfo.InvariantCulture,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: RetainedFiles,
                fileSizeLimitBytes: FileSizeLimit,
                rollOnFileSizeLimit: true,

                // Two copies of Patchbay on one desktop is an ordinary thing to
                // do, and without this the second one finds the file locked and
                // logs nothing at all.
                shared: true,

                // No byte order mark. The mask is not ASCII, so the encoding
                // has to be said out loud rather than left to whatever the
                // sink defaults to this year.
                encoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger = PatchbayLog.Create(_ => { });
        }

        Log.Logger = logger;

        return logger;
    }
}
