using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Patchbay.Core.Editing;
using Patchbay.Core.Model;

namespace Patchbay.Core.Import;

/// <summary>
/// Reads a Remote Desktop Connection Manager <c>.rdg</c> file.
///
/// The mapping is luckier than it deserves to be. RDCMan groups its settings
/// into blocks — connection, gateway, remote desktop, local resources — and
/// each block carries <c>inherit="FromParent"</c> or <c>inherit="None"</c>.
/// Patchbay's rule is the same idea taken one level finer: null means inherit,
/// per property rather than per block. So a block marked FromParent maps to
/// leaving every property in it null, and nothing has to be resolved at import
/// time. What someone had set in RDCMan is what stays set here, and the
/// inheritance keeps working the way they set it up.
///
/// What does not come across is credentials. RDCMan stores passwords encrypted
/// with DPAPI against the user or the machine that saved them; they are
/// counted and reported, never decrypted and never written into a Patchbay
/// document. Credentials land in M3, with their own store.
/// </summary>
public static class RdgImporter
{
    /// <summary>Schema versions this understands. v3 is what RDCMan 2.7 onwards writes.</summary>
    public const int OldestSupportedSchema = 1;

    public const int NewestSupportedSchema = 3;

    public static ImportResult ImportFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        try
        {
            using FileStream stream = File.OpenRead(path);
            return Import(stream);
        }
        catch (IOException ex)
        {
            throw new ImportException($"'{path}' could not be opened: {ex.Message}", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new ImportException($"Patchbay is not allowed to read '{path}'.", ex);
        }
    }

    public static ImportResult Import(string xml)
    {
        ArgumentNullException.ThrowIfNull(xml);

        using MemoryStream stream = new(Encoding.UTF8.GetBytes(xml));
        return Import(stream);
    }

    /// <exception cref="ImportException">The file is unreadable or not a .rdg.</exception>
    public static ImportResult Import(Stream stream)
    {
        XDocument document = SafeXml.Load(stream);

        XElement root = document.Root
            ?? throw new ImportException("This file is empty.");

        if (!string.Equals(root.Name.LocalName, "RDCMan", StringComparison.Ordinal))
        {
            throw new ImportException(
                $"This is not an RDCMan file. Its outermost element is '{root.Name.LocalName}', "
                + "and a .rdg file starts with 'RDCMan'.");
        }

        // Bound the nesting before anything walks the tree recursively.
        SafeXml.GuardDepth(root);

        Context context = new();

        ReadSchemaVersion(root, context);

        XElement file = root.Element("file")
            ?? throw new ImportException(
                "This RDCMan file has no 'file' section, so there is nothing in it to import.");

        GroupNode imported = new()
        {
            Name = Text(file.Element("properties"), "name") ?? "Imported connections",
        };

        ReadCredentialProfiles(file, context);
        ReadSettings(file, imported, context);
        ReadChildren(file, imported, context);

        context.Finish();

        return new ImportResult(imported, context.Warnings, context.Groups, context.Servers);
    }

    private static void ReadSchemaVersion(XElement root, Context context)
    {
        string? raw = (string?)root.Attribute("schemaVersion");

        if (raw is null)
        {
            // Very early files omit it. They are close enough to v1 to try.
            return;
        }

        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int version))
        {
            context.Warn($"The file claims schema version '{raw}', which is not a number. Reading it anyway.");
            return;
        }

        if (version < OldestSupportedSchema)
        {
            throw new ImportException(
                $"This file uses RDCMan schema version {version}, which is older than anything "
                + "Patchbay can read.");
        }

        if (version > NewestSupportedSchema)
        {
            // Best effort rather than refusal: the shape has been stable, and
            // refusing outright would be worse than importing what is
            // recognisable and saying so.
            context.Warn(
                $"This file was written by a newer RDCMan (schema version {version}). Anything "
                + "Patchbay did not recognise has been left out.");
        }
    }

    private static void ReadChildren(XElement parent, GroupNode target, Context context)
    {
        foreach (XElement child in parent.Elements())
        {
            switch (child.Name.LocalName)
            {
                case "group":
                    ReadGroup(child, target, context);
                    break;

                case "server":
                    ReadServer(child, target, context);
                    break;

                case "smartGroup":
                    // A saved search, not a container of real connections.
                    context.Note("saved searches (smart groups)");
                    break;

                default:
                    break;
            }
        }
    }

    private static void ReadGroup(XElement source, GroupNode parent, Context context)
    {
        XElement? properties = source.Element("properties");

        GroupNode group = new()
        {
            Name = NodeOperations.UniqueName(parent, Text(properties, "name") ?? "Group"),
            Notes = Text(properties, "comment"),
        };

        ReadSettings(source, group, context);

        parent.Add(group);
        context.Groups++;

        ReadChildren(source, group, context);
    }

    private static void ReadServer(XElement source, GroupNode parent, Context context)
    {
        XElement? properties = source.Element("properties");

        // In RDCMan, 'name' is the address and 'displayName' is what you see.
        // Getting these the wrong way round produces a tree that looks right
        // and connects to nothing.
        string? address = Text(properties, "name");

        if (address is null)
        {
            context.SkippedServers++;
            return;
        }

        ServerNode server = new()
        {
            HostName = address,
            Name = NodeOperations.UniqueName(parent, Text(properties, "displayName") ?? address),
            Notes = Text(properties, "comment"),
        };

        ReadSettings(source, server, context);

        parent.Add(server);
        context.Servers++;
    }

    /// <summary>
    /// Copies the settings blocks. A block marked FromParent is skipped
    /// entirely, which leaves its properties null — the same thing said in
    /// Patchbay's vocabulary.
    /// </summary>
    private static void ReadSettings(XElement source, ConnectionNode target, Context context)
    {
        ConnectionSettings settings = target.Settings;

        if (Owns(source.Element("connectionSettings"), out XElement? connection))
        {
            settings.Port = Int(connection, "port");
            settings.ConnectToConsole = Bool(connection, "connectToConsole");

            if (HasText(connection, "startProgram"))
            {
                context.Note("start programs");
            }
        }

        if (Owns(source.Element("gatewaySettings"), out XElement? gateway))
        {
            bool enabled = Bool(gateway, "enabled") ?? false;
            bool bypassLocally = Bool(gateway, "localBypass") ?? false;

            settings.GatewayHostName = Text(gateway, "hostName");
            settings.GatewayUsage = enabled
                ? (bypassLocally ? GatewayUsage.WhenDirectFails : GatewayUsage.Always)
                : GatewayUsage.None;

            if (HasText(gateway, "userName") || HasText(gateway, "password"))
            {
                context.GatewayCredentials++;
            }
        }

        if (Owns(source.Element("remoteDesktop"), out XElement? desktop))
        {
            settings.DesktopWidth = Int(desktop, "desktopWidth");
            settings.DesktopHeight = Int(desktop, "desktopHeight");
            settings.UseSmartSizing = Bool(desktop, "sameSizeAsClientArea");
            settings.ColourDepth = ReadColourDepth(desktop, context);
        }

        if (Owns(source.Element("localResources"), out XElement? resources))
        {
            settings.RedirectClipboard = Bool(resources, "redirectClipboard");
            settings.RedirectDrives = Bool(resources, "redirectDrives");
            settings.RedirectPrinters = Bool(resources, "redirectPrinters");
            settings.AudioMode = ReadAudioMode(resources, context);

            if (Bool(resources, "redirectPorts") is true || Bool(resources, "redirectSmartCards") is true)
            {
                context.Note("port and smart-card redirection");
            }
        }

        if (Owns(source.Element("logonCredentials"), out XElement? logon))
        {
            settings.UserName = Text(logon, "userName");
            settings.Domain = Text(logon, "domain");

            if (HasText(logon, "password"))
            {
                context.Passwords++;

                // A password existed, so this connection was not meant to
                // prompt. Prompting is the only honest thing to do until the
                // credential store lands.
                settings.CredentialMode = CredentialMode.Prompt;
            }
        }
    }

    private static ColourDepth? ReadColourDepth(XElement? element, Context context)
    {
        int? bits = Int(element, "colorDepth");

        if (bits is null)
        {
            return null;
        }

        if (Enum.IsDefined(typeof(ColourDepth), bits.Value))
        {
            return (ColourDepth)bits.Value;
        }

        context.Warn(
            $"A colour depth of {bits.Value} bits is not one Patchbay offers, so those "
            + "connections will use the inherited depth instead.");

        return null;
    }

    private static AudioMode? ReadAudioMode(XElement? element, Context context)
    {
        int? mode = Int(element, "audioRedirection");

        return mode switch
        {
            0 => AudioMode.PlayLocally,
            1 => AudioMode.PlayRemotely,
            2 => AudioMode.DoNotPlay,
            null => null,
            _ => Unknown(),
        };

        AudioMode? Unknown()
        {
            context.Warn($"An audio setting of '{mode}' was not recognised and has been left to inherit.");
            return null;
        }
    }

    private static void ReadCredentialProfiles(XElement file, Context context)
    {
        int profiles = file
            .Elements("credentialsProfiles")
            .Elements("credentialsProfile")
            .Count();

        context.CredentialProfiles += profiles;
    }

    /// <summary>
    /// Whether a settings block carries its own values. Absent, or marked
    /// FromParent, means it inherits — which Patchbay expresses by leaving the
    /// properties null, so there is nothing to do.
    /// </summary>
    private static bool Owns(XElement? block, out XElement? owned)
    {
        owned = null;

        if (block is null)
        {
            return false;
        }

        string? inherit = (string?)block.Attribute("inherit");

        if (string.Equals(inherit, "FromParent", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        owned = block;
        return true;
    }

    private static string? Text(XElement? parent, string name)
    {
        string? value = (string?)parent?.Element(name);

        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool HasText(XElement? parent, string name) => Text(parent, name) is not null;

    private static bool? Bool(XElement? parent, string name) =>
        Text(parent, name) is { } value && bool.TryParse(value, out bool parsed) ? parsed : null;

    private static int? Int(XElement? parent, string name) =>
        Text(parent, name) is { } value
            && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : null;

    /// <summary>
    /// Collects counts and warnings while the walk happens, so that a file
    /// with four hundred password-bearing servers produces one sentence about
    /// it rather than four hundred.
    /// </summary>
    private sealed class Context
    {
        private readonly List<string> _warnings = [];
        private readonly SortedSet<string> _unsupported = new(StringComparer.Ordinal);

        public int Groups { get; set; }

        public int Servers { get; set; }

        public int SkippedServers { get; set; }

        public int Passwords { get; set; }

        public int GatewayCredentials { get; set; }

        public int CredentialProfiles { get; set; }

        public IReadOnlyList<string> Warnings => _warnings;

        public void Warn(string message) => _warnings.Add(message);

        /// <summary>Records a feature Patchbay does not model yet, once.</summary>
        public void Note(string feature) => _unsupported.Add(feature);

        public void Finish()
        {
            if (Passwords > 0 || GatewayCredentials > 0 || CredentialProfiles > 0)
            {
                _warnings.Add(
                    $"Saved passwords were not imported ({Passwords} connections, "
                    + $"{GatewayCredentials} gateways, {CredentialProfiles} credential profiles). "
                    + "RDCMan encrypts them against the account that saved them, and Patchbay does "
                    + "not have a credential store yet, so those connections will ask when they "
                    + "connect. User names and domains did come across.");
            }

            if (SkippedServers > 0)
            {
                _warnings.Add(
                    $"{SkippedServers} entries had no address and were left out.");
            }

            if (_unsupported.Count > 0)
            {
                _warnings.Add(
                    $"Patchbay does not handle {string.Join(", ", _unsupported)} yet, so those "
                    + "settings were not carried over.");
            }
        }
    }
}
