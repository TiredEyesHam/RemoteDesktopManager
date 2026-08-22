using System.Globalization;
using System.Text;
using Patchbay.Core.Editing;
using Patchbay.Core.Model;
using Patchbay.Core.Validation;

namespace Patchbay.Core.Import;

/// <summary>
/// Reads Remote Desktop <c>.rdp</c> files — the ones <c>mstsc.exe</c> saves,
/// one connection each (M1-14).
///
/// <para>
/// <b>A <c>.rdg</c> is a document; a <c>.rdp</c> is a message.</b> The first
/// is something a person built and kept. The second arrives — emailed by a
/// supplier, downloaded from a portal, dropped in a share — and the format can
/// say a great deal more than "here is a machine to connect to". It can hand
/// the far end every drive on this computer, the smart card in its reader and
/// the microphone; it can name a program to run instead of a desktop; and it
/// can ask the client not to check who it is connecting to. In October 2024 a
/// campaign did exactly the first of those at scale, with signed <c>.rdp</c>
/// files attached to plausible email.
/// </para>
///
/// <para>
/// So the rule is one sentence: <b>an imported file may switch a redirection
/// off, but it may not switch on one that Patchbay leaves off.</b> What counts
/// as off is read from <see cref="ConnectionSettings.Defaults"/> rather than
/// from a list written out here, so the rule keeps meaning the same thing if a
/// default ever moves. Nothing is dropped in silence: everything refused is
/// named in the warnings, and turning any of it back on is a checkbox in the
/// inspector — a decision made by the person, which is where it belongs.
/// </para>
///
/// <para>
/// Passwords are counted and never decrypted, the same as the RDCMan importer.
/// <c>password 51</c> is a DPAPI blob belonging to whoever saved the file, and
/// <see cref="RdpFile"/> does not keep it.
/// </para>
/// </summary>
public static class RdpImporter
{
    /// <summary>What a group of imported connections is called.</summary>
    public const string GroupName = "Imported connections";

    /// <summary>Longest display name taken from a file name.</summary>
    private const int MaxNameLength = 64;

    /// <summary>How much of a start program is quoted back in a warning.</summary>
    private const int MaxQuoted = 120;

    /// <summary>Reads one file.</summary>
    /// <exception cref="ImportException">The file is unreadable or names no machine.</exception>
    public static ImportResult ImportFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return ImportFiles([path]);
    }

    /// <summary>
    /// Reads several files into one group.
    ///
    /// <para>
    /// A file that cannot be read becomes a warning rather than the end of the
    /// import. Somebody who selected twenty and has one bad one wants the
    /// nineteen; losing the lot over one is the behaviour that gets worked
    /// around by importing them one at a time.
    /// </para>
    /// </summary>
    /// <exception cref="ImportException">Nothing in the selection could be read.</exception>
    public static ImportResult ImportFiles(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        if (paths.Count == 0)
        {
            throw new ImportException("No files were chosen, so there was nothing to import.");
        }

        GroupNode group = new() { Name = GroupName };
        Context context = new();
        ImportException? first = null;

        foreach (string path in paths)
        {
            try
            {
                using FileStream stream = File.OpenRead(path);

                Read(RdpFile.Read(stream), NameFor(path), group, context);
            }
            catch (ImportException ex)
            {
                first ??= ex;
                context.Warn($"{Path.GetFileName(path)} was not imported. {ex.Message}");
            }
            catch (IOException ex)
            {
                first ??= new ImportException($"'{path}' could not be opened: {ex.Message}", ex);
                context.Warn($"{Path.GetFileName(path)} could not be opened. {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                first ??= new ImportException($"Patchbay is not allowed to read '{path}'.", ex);
                context.Warn($"Patchbay is not allowed to read {Path.GetFileName(path)}.");
            }
        }

        // One bad file among many is a warning; every file bad is a failed
        // import, and saying so is more use than an empty group.
        if (context.Servers == 0 && first is not null)
        {
            throw first;
        }

        context.Finish();

        return new ImportResult(group, context.Warnings, 0, context.Servers)
        {
            // The group is always scaffolding here: a .rdp describes a machine
            // and no structure at all, so nothing about this one came out of a
            // file. What that means for a selection of one is that the
            // connection goes straight into the tree rather than into a folder
            // holding one thing.
            RootIsWrapper = true,
        };
    }

    /// <summary>Reads file contents that have already been decoded.</summary>
    /// <param name="text">The file.</param>
    /// <param name="name">What to call the connection; the address is used if this is null.</param>
    public static ImportResult Import(string text, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        return Import(RdpFile.Read(text), name);
    }

    /// <summary>Reads a stream, detecting its encoding.</summary>
    public static ImportResult Import(Stream stream, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(stream);

        return Import(RdpFile.Read(stream), name);
    }

    private static ImportResult Import(RdpFile file, string? name)
    {
        GroupNode group = new() { Name = GroupName };
        Context context = new();

        Read(file, name, group, context);
        context.Finish();

        return new ImportResult(group, context.Warnings, 0, context.Servers)
        {
            RootIsWrapper = true,
        };
    }

    private static void Read(RdpFile file, string? name, GroupNode parent, Context context)
    {
        string address = file.Text("full address")
            ?? file.Text("alternate full address")
            ?? throw new ImportException(
                "This file names no machine to connect to, so it is either not a Remote Desktop "
                + "file or is missing its 'full address' line.");

        (string host, int? embedded) = SplitAddress(address);

        if (!NodeValidator.IsValidHost(host))
        {
            // Quoted back on purpose: an address that fails this usually means
            // the file is not a .rdp at all, and seeing what was read is what
            // tells somebody that.
            throw new ImportException(
                $"'{Quote(host)}' is not a host name or address Patchbay can use.");
        }

        // Cleaned here rather than wherever the name came from, because this
        // is the one place a name enters the tree.
        string wanted = name is null ? host : Clean(name, MaxNameLength);

        ServerNode server = new()
        {
            HostName = host,
            Name = NodeOperations.UniqueName(parent, wanted.Length == 0 ? host : wanted),
        };

        ReadConnection(file, server.Settings, embedded, context);
        ReadCredentials(file, server.Settings, context);
        ReadGateway(file, server.Settings, context);
        ReadDisplay(file, server.Settings, context);
        ReadLocalResources(file, server.Settings, context);
        ReadExperience(file, server.Settings);
        ReadSecurity(file, server.Settings, context);
        ReadProgram(file, context);

        if (file.RepeatedNames.Count > 0)
        {
            context.Repeated(file.RepeatedNames);
        }

        parent.Add(server);
        context.Servers++;
    }

    // ── Connection ──────────────────────────────────────────────────────

    private static void ReadConnection(
        RdpFile file,
        ConnectionSettings settings,
        int? embeddedPort,
        Context context)
    {
        // An explicit line wins over a port carried in the address, because a
        // file holding both was written by something that meant the line.
        int? port = file.Number("server port") ?? embeddedPort;

        if (port is { } chosen)
        {
            if (NodeValidator.IsValidPort(chosen))
            {
                settings.Port = chosen;
            }
            else
            {
                context.Warn(
                    $"A port of {chosen} is not one anything can connect to, so the inherited "
                    + "port is used instead.");
            }
        }

        // Two names for the same setting, a release apart. The newer spelling
        // is read first, so a file carrying both is read the way the client
        // that wrote it meant.
        settings.ConnectToConsole =
            file.Flag("administrative session") ?? file.Flag("connect to console");

        settings.AutoReconnect = file.Flag("autoreconnection enabled");

        // Minutes here, milliseconds on the control. The unit is the whole of
        // this setting: a file asking for one minute must not become a
        // keep-alive every millisecond.
        if (file.Number("keepalive interval") is { } minutes and >= 0)
        {
            settings.KeepAliveIntervalSeconds = minutes * 60;
        }

        if (file.Text("alternate full address") is { } alternate
            && !string.Equals(alternate, file.Text("full address"), StringComparison.OrdinalIgnoreCase))
        {
            context.Note("the second address a broker redirected to");
        }

        // Present and empty, for both of these, is how a client writes "no".
        // Reading the name rather than the value would put a note about broker
        // routing on every file mstsc has ever saved.
        if (file.Text("loadbalanceinfo") is not null)
        {
            context.Note("connection broker routing");
        }

        if (file.Text("kdcproxyname") is not null)
        {
            context.Note("Kerberos proxies");
        }
    }

    // ── Credentials ─────────────────────────────────────────────────────

    private static void ReadCredentials(RdpFile file, ConnectionSettings settings, Context context)
    {
        string? user = file.Text("username");
        settings.Domain = file.Text("domain");

        // DOMAIN\user in one field is how these files usually carry it.
        // Splitting is what puts the domain in the box marked domain, rather
        // than leaving it inside a user name that nothing matches.
        if (user is not null && settings.Domain is null)
        {
            int slash = user.IndexOf('\\');

            if (slash > 0 && slash < user.Length - 1)
            {
                settings.Domain = user[..slash];
                user = user[(slash + 1)..];
            }
        }

        settings.UserName = user;

        if (file.Has("password 51"))
        {
            context.Passwords++;

            // A password was saved, so this connection was not meant to ask.
            // Asking is the only honest thing left: the blob is encrypted to
            // whoever saved the file, and that is rarely whoever imports it.
            settings.CredentialMode = CredentialMode.Prompt;
        }
    }

    // ── Gateway ─────────────────────────────────────────────────────────

    private static void ReadGateway(RdpFile file, ConnectionSettings settings, Context context)
    {
        settings.GatewayHostName = file.Text("gatewayhostname");
        settings.GatewayUserName = file.Text("gatewayusername");
        settings.GatewayUseSameCredentials = file.Flag("promptcredentialonce");

        if (file.Number("gatewayusagemethod") is { } usage)
        {
            settings.GatewayUsage = usage switch
            {
                0 or 4 => GatewayUsage.None,
                1 => GatewayUsage.Always,
                2 => GatewayUsage.WhenDirectFails,

                // 3 means "whatever a policy on this machine says", which is a
                // different answer from any of the three Patchbay has and not
                // one worth guessing at.
                _ => Unrecognised(),
            };
        }

        settings.GatewayCredentialSource = file.Number("gatewaycredentialssource") switch
        {
            0 => GatewayCredentialSource.Password,
            1 => GatewayCredentialSource.SmartCard,
            4 => GatewayCredentialSource.Any,
            _ => null,
        };

        GatewayUsage? Unrecognised()
        {
            context.Warn(
                $"This file uses gateway mode {usage}, which Patchbay does not have, so the "
                + "gateway setting was left to inherit.");

            return null;
        }
    }

    // ── Display ─────────────────────────────────────────────────────────

    private static void ReadDisplay(RdpFile file, ConnectionSettings settings, Context context)
    {
        settings.DesktopWidth = Positive(file.Number("desktopwidth"));
        settings.DesktopHeight = Positive(file.Number("desktopheight"));
        settings.UseSmartSizing = file.Flag("smart sizing");

        if (file.Number("session bpp") is { } bits)
        {
            if (Enum.IsDefined(typeof(ColourDepth), bits))
            {
                settings.ColourDepth = (ColourDepth)bits;
            }
            else
            {
                context.Warn(
                    $"A colour depth of {bits} bits is not one Patchbay offers, so the inherited "
                    + "depth is used instead.");
            }
        }

        // Full screen is not in here on purpose, though the file says it: a
        // session opens in a tab, which is a difference somebody sees the
        // moment it draws rather than a setting quietly lost. Every file mstsc
        // saves asks for full screen, so noting it would put a warning on
        // every import that told nobody anything.
        if (file.Flag("use multimon") is true)
        {
            context.Note("spreading a session across several monitors");
        }
    }

    // ── Local resources ─────────────────────────────────────────────────

    /// <summary>
    /// The redirections, each measured against the default it would be
    /// overriding. This is where the rule at the top of the file lives: off is
    /// always imported, and on is imported only where on is already where
    /// Patchbay starts.
    /// </summary>
    private static void ReadLocalResources(RdpFile file, ConnectionSettings settings, Context context)
    {
        ConnectionSettings defaults = ConnectionSettings.Defaults;

        settings.RedirectClipboard = Redirection(
            file.Flag("redirectclipboard"), defaults.RedirectClipboard, "the clipboard", context);

        // A list of drive letters rather than a switch, and "*" means every
        // drive there is — including the one holding the profile that opened
        // the file.
        settings.RedirectDrives = Redirection(
            Listed(file, "drivestoredirect"), defaults.RedirectDrives, "your drives", context);

        settings.RedirectPrinters = Redirection(
            file.Flag("redirectprinters"), defaults.RedirectPrinters, "your printers", context);

        settings.RedirectSmartCards = Redirection(
            file.Flag("redirectsmartcards"),
            defaults.RedirectSmartCards,
            "your smart card reader",
            context);

        settings.RedirectPorts = Redirection(
            file.Flag("redirectcomports"),
            defaults.RedirectPorts,
            "your serial and parallel ports",
            context);

        settings.RedirectDevices = Redirection(
            Listed(file, "devicestoredirect"),
            defaults.RedirectDevices,
            "plug-and-play devices",
            context);

        settings.RedirectPointOfSaleDevices = Redirection(
            file.Flag("redirectposdevices"),
            defaults.RedirectPointOfSaleDevices,
            "point-of-sale devices",
            context);

        settings.RedirectMicrophone = Redirection(
            file.Flag("audiocapturemode"),
            defaults.RedirectMicrophone,
            "your microphone",
            context);

        settings.AudioMode = file.Number("audiomode") switch
        {
            0 => AudioMode.PlayLocally,
            1 => AudioMode.PlayRemotely,
            2 => AudioMode.DoNotPlay,
            _ => null,
        };

        settings.AudioQuality = file.Number("audioqualitymode") switch
        {
            0 => AudioQuality.Dynamic,
            1 => AudioQuality.Medium,
            2 => AudioQuality.High,
            _ => null,
        };
    }

    /// <summary>
    /// A redirection the file asked for, or null to leave it inherited.
    /// </summary>
    /// <param name="asked">What the file said, or null if it said nothing.</param>
    /// <param name="defaultsOn">
    /// Where Patchbay starts, out of <see cref="ConnectionSettings.Defaults"/>.
    /// </param>
    /// <param name="what">What is being handed over, for the warning.</param>
    private static bool? Redirection(bool? asked, bool? defaultsOn, string what, Context context)
    {
        if (asked is not true || defaultsOn is true)
        {
            return asked;
        }

        context.Refused(what);
        return null;
    }

    /// <summary>
    /// The list-shaped redirections, which are on when the list has anything
    /// in it. An empty list is the client's way of saying none, and is a
    /// genuine "off" rather than an absence.
    /// </summary>
    private static bool? Listed(RdpFile file, string name) =>
        file.Has(name) ? file.Text(name) is not null : null;

    // ── Experience ──────────────────────────────────────────────────────

    /// <summary>
    /// How the desktop is allowed to look. Four of these are written as
    /// "disable" in the file and read as "enable" here, so each of those is an
    /// inversion; the two that are not are the two spelt "allow".
    /// </summary>
    private static void ReadExperience(RdpFile file, ConnectionSettings settings)
    {
        settings.DesktopBackground = Not(file.Flag("disable wallpaper"));
        settings.ShowWindowContentsWhileDragging = Not(file.Flag("disable full window drag"));
        settings.MenuAnimations = Not(file.Flag("disable menu anims"));
        settings.VisualStyles = Not(file.Flag("disable themes"));
        settings.FontSmoothing = file.Flag("allow font smoothing");
        settings.DesktopComposition = file.Flag("allow desktop composition");
        settings.PersistentBitmapCache = file.Flag("bitmapcachepersistenable");

        // Detection is not a link speed, so a file asking for both is read as
        // asking for detection — it is the answer that stays right after the
        // laptop moves.
        if (file.Flag("networkautodetect") is true || file.Number("connection type") is 7)
        {
            settings.ConnectionQuality = ConnectionQuality.Detect;
            return;
        }

        // Written out rather than cast. The numbers line up with the enum
        // today, and a coincidence is not a contract.
        settings.ConnectionQuality = file.Number("connection type") switch
        {
            1 => ConnectionQuality.Modem,
            2 => ConnectionQuality.LowSpeedBroadband,
            3 => ConnectionQuality.Satellite,
            4 => ConnectionQuality.HighSpeedBroadband,
            5 => ConnectionQuality.Wan,
            6 => ConnectionQuality.Lan,
            _ => null,
        };
    }

    // ── Security ────────────────────────────────────────────────────────

    private static void ReadSecurity(RdpFile file, ConnectionSettings settings, Context context)
    {
        // 1 is the strict one and 2 is the lenient one, so the numbers do not
        // rise with strictness and a cast off the enum would swap the two
        // answers that matter.
        settings.ServerAuthentication = file.Number("authentication level") switch
        {
            1 => ServerAuthentication.Require,
            2 => ServerAuthentication.Warn,
            0 => Silenced(),
            _ => null,
        };

        if (file.Flag("enablecredsspsupport") is false)
        {
            context.Downgraded(
                "asked for network level authentication to be switched off, which moves the "
                + "logon to a screen drawn by the far end");
        }

        ServerAuthentication? Silenced()
        {
            context.Downgraded(
                "asked Patchbay not to check the identity of the machine it connects to, so a "
                + "server that could not prove who it was would have been connected to in "
                + "silence");

            return null;
        }
    }

    // ── A program instead of a desktop ──────────────────────────────────

    /// <summary>
    /// <c>alternate shell</c> and the RemoteApp settings both name something
    /// to run on the far end instead of a desktop. Patchbay models neither, so
    /// nothing is imported whatever this finds — but a file that came from
    /// somewhere else and names a program is the part worth reading before
    /// connecting, so it is quoted rather than counted.
    /// </summary>
    private static void ReadProgram(RdpFile file, Context context)
    {
        // Both, where a file names both. They are different settings and a
        // file carrying two answers is worth showing as two, not as whichever
        // one happened to be read first.
        string?[] named = [file.Text("remoteapplicationprogram"), file.Text("alternate shell")];

        foreach (string program in named.OfType<string>())
        {
            context.Program(Quote(program));
        }

        if (named.All(p => p is null) && file.Flag("remoteapplicationmode") is true)
        {
            context.Program(null);
        }
    }

    // ── Reading values ──────────────────────────────────────────────────

    /// <summary>
    /// Splits <c>host:3390</c>, leaving a bare IPv6 address alone. Exactly one
    /// colon is a port; several mean an address written without the brackets a
    /// port would have needed.
    /// </summary>
    private static (string Host, int? Port) SplitAddress(string address)
    {
        if (address.StartsWith('['))
        {
            int close = address.IndexOf(']');

            if (close < 0)
            {
                return (address, null);
            }

            string rest = address[(close + 1)..];

            return rest.StartsWith(':') && Number(rest[1..]) is { } bracketed
                ? (address[1..close], bracketed)
                : (address[1..close], null);
        }

        int colon = address.LastIndexOf(':');

        if (colon <= 0 || address.IndexOf(':') != colon)
        {
            return (address, null);
        }

        return Number(address[(colon + 1)..]) is { } port
            ? (address[..colon], port)
            : (address, null);

        static int? Number(string text) =>
            int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                ? parsed
                : null;
    }

    private static int? Positive(int? value) => value is > 0 ? value : null;

    private static bool? Not(bool? value) => value is null ? null : !value.Value;

    /// <summary>
    /// The display name for a file, which is the file's own name: it is what
    /// the person who saved it called the machine, and it is more use in a
    /// tree than the address written out twice.
    /// </summary>
    private static string? NameFor(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);

        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    /// <summary>Text out of the file, fit to put in a sentence shown to a person.</summary>
    private static string Quote(string text) => Clean(text, MaxQuoted);

    /// <summary>
    /// Text from the file made fit to look at. A file name is not something
    /// Patchbay chose, and a name carrying a right-to-left override reads in
    /// the tree as a different machine from the one it connects to — which is
    /// a cheap trick and an effective one.
    /// </summary>
    private static string Clean(string text, int limit)
    {
        StringBuilder clean = new();

        foreach (char c in text)
        {
            if (clean.Length == limit)
            {
                clean.Append('…');
                break;
            }

            // Control characters and the invisible formatting ones, which is
            // where the direction overrides live. Whitespace becomes a single
            // space so that words do not run together.
            if (char.IsControl(c) || char.GetUnicodeCategory(c) is UnicodeCategory.Format)
            {
                continue;
            }

            clean.Append(char.IsWhiteSpace(c) ? ' ' : c);
        }

        return clean.ToString().Trim();
    }

    /// <summary>
    /// Collects what happened while the files are read, so that twenty files
    /// with the same problem produce one sentence rather than twenty.
    /// </summary>
    private sealed class Context
    {
        private readonly List<string> _warnings = [];
        // A list rather than a set, because the order these are read in is
        // the order they are worth hearing about, and sorting them would put
        // plug-and-play devices ahead of the drives.
        private readonly List<string> _refused = [];
        private readonly SortedSet<string> _downgrades = new(StringComparer.Ordinal);
        private readonly SortedSet<string> _programs = new(StringComparer.Ordinal);
        private readonly SortedSet<string> _unsupported = new(StringComparer.Ordinal);
        private readonly SortedSet<string> _repeated = new(StringComparer.OrdinalIgnoreCase);

        private int _unnamedPrograms;

        public int Servers { get; set; }

        public int Passwords { get; set; }

        public IReadOnlyList<string> Warnings => _warnings;

        public void Warn(string message) => _warnings.Add(message);

        /// <summary>A redirection the file asked for and did not get.</summary>
        public void Refused(string what)
        {
            if (!_refused.Contains(what))
            {
                _refused.Add(what);
            }
        }

        /// <summary>Something the file asked for that would have weakened the connection.</summary>
        public void Downgraded(string what) => _downgrades.Add(what);

        /// <summary>Records a feature Patchbay does not model, once.</summary>
        public void Note(string feature) => _unsupported.Add(feature);

        /// <summary>Records settings the file gave more than one value.</summary>
        public void Repeated(IEnumerable<string> names) => _repeated.UnionWith(names);

        /// <summary>Records a program the file wanted to run instead of a desktop.</summary>
        public void Program(string? named)
        {
            if (named is null)
            {
                _unnamedPrograms++;
            }
            else
            {
                _programs.Add(named);
            }
        }

        public void Finish()
        {
            if (_refused.Count > 0)
            {
                _warnings.Add(
                    $"This file asked to hand over {Join(_refused)}. Patchbay has not turned that "
                    + "on. A connection file can switch a redirection off but not on, because the "
                    + "machine at the other end is the one that gains from it — turn any of them "
                    + "on yourself once you know the connection is yours.");
            }

            if (_downgrades.Count > 0)
            {
                _warnings.Add(
                    $"This file {string.Join(". It also ", _downgrades)}. None of that was "
                    + "imported.");
            }

            if (_programs.Count > 0 || _unnamedPrograms > 0)
            {
                string named = _programs.Count > 0
                    ? $": {Join(_programs)}"
                    : ", though it does not say which";

                _warnings.Add(
                    $"This file runs a program instead of a desktop{named}. Patchbay opens "
                    + "desktops, so nothing was imported from it — but a connection file that "
                    + "arrived from somewhere else and names a program to run is worth a look "
                    + "before you connect.");
            }

            if (Passwords > 0)
            {
                _warnings.Add(
                    $"Saved passwords were not imported ({Passwords}). Windows encrypts one "
                    + "against the account that saved the file, so it cannot be read here, and "
                    + "those connections will ask when they connect. User names and domains did "
                    + "come across.");
            }

            if (_repeated.Count > 0)
            {
                _warnings.Add(
                    $"These settings appear more than once with different values: "
                    + $"{Join(_repeated)}. The last of each was used, which is what the Remote "
                    + "Desktop client does — but nothing that writes these files repeats a "
                    + "setting.");
            }

            if (_unsupported.Count > 0)
            {
                _warnings.Add(
                    $"Patchbay does not handle {Join(_unsupported)} yet, so those settings were "
                    + "not carried over.");
            }
        }

        private static string Join(IReadOnlyCollection<string> items) => items.Count switch
        {
            1 => items.First(),
            _ => string.Join(", ", items.Take(items.Count - 1)) + " and " + items.Last(),
        };
    }
}
