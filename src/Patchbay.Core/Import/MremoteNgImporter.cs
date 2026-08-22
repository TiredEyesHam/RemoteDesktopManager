using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Patchbay.Core.Editing;
using Patchbay.Core.Model;
using Patchbay.Core.Validation;

namespace Patchbay.Core.Import;

/// <summary>
/// Reads an mRemoteNG <c>confCons.xml</c> (M1-15).
///
/// <para>
/// <b>Written against the format, not against anybody's source.</b> mRemoteNG
/// is GPL-2.0 and Patchbay is GPL-3.0-or-later, and those are incompatible in
/// that direction: lifting even a helper out of their tree would bind this
/// repository to terms it has not chosen, and the remedy would be relicensing
/// rather than deleting a file. Reading a file format is a different act
/// entirely, and it is the one this does.
/// </para>
///
/// <para>
/// <b>The inheritance maps better than RDCMan's did.</b> A <c>.rdg</c> carries
/// <c>inherit="FromParent"</c> per block of settings; mRemoteNG carries an
/// <c>Inherit*</c> attribute per setting, which is Patchbay's own rule written
/// in somebody else's vocabulary. So <c>InheritColors="true"</c> becomes a null
/// colour depth and nothing has to be resolved at import time.
/// </para>
///
/// <para>
/// <b>This is an inventory, not an invitation, and that is why the
/// <c>.rdp</c> rule is not applied here.</b> A <see cref="RdpImporter"/> file
/// is one connection that circulates as an attachment — "connect to this
/// machine" — so it may switch a redirection off but never on. A
/// <c>confCons.xml</c> is somebody's whole estate, imported because it was
/// asked for, and quietly dropping the drive redirection they configured on
/// forty connections would be losing their work rather than defending them.
/// What it does instead is count: a file where connections hand this computer
/// to the far end says so in a sentence, which is the part somebody wants to
/// see when the file came from a colleague.
/// </para>
///
/// <para>
/// Passwords are counted and never decrypted. mRemoteNG encrypts them under a
/// key derived from a password that defaults to a value published in its own
/// documentation, so unlike RDCMan's DPAPI blobs these could be read — which
/// makes leaving them alone a decision rather than a limitation. Reading
/// somebody's credential store because the key is guessable is a thing to do
/// deliberately, with the person watching, and not silently in the middle of
/// an import.
/// </para>
/// </summary>
public static partial class MremoteNgImporter
{
    /// <summary>
    /// Configuration versions this understands. 2.6 is what mRemoteNG 1.76
    /// onwards writes; anything below 1.0 predates the format having a version
    /// at all.
    /// </summary>
    public const double OldestSupportedVersion = 1.0;

    public const double NewestSupportedVersion = 2.9;

    /// <summary>Reads a file.</summary>
    /// <exception cref="ImportException">The file is unreadable or not a confCons.xml.</exception>
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

    /// <summary>Reads file contents that have already been decoded.</summary>
    public static ImportResult Import(string xml)
    {
        ArgumentNullException.ThrowIfNull(xml);

        using MemoryStream stream = new(Encoding.UTF8.GetBytes(xml));

        return Import(stream);
    }

    /// <exception cref="ImportException">The stream is unreadable or not a confCons.xml.</exception>
    public static ImportResult Import(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        GuardWholeFileEncryption(stream);

        XDocument document = SafeXml.Load(stream);

        XElement root = document.Root
            ?? throw new ImportException("This file is empty.");

        if (!string.Equals(root.Name.LocalName, "Connections", StringComparison.Ordinal))
        {
            throw new ImportException(
                $"This is not an mRemoteNG file. Its outermost element is "
                + $"'{root.Name.LocalName}', and a confCons.xml starts with 'Connections'.");
        }

        // Bound the nesting before anything walks the tree recursively.
        SafeXml.GuardDepth(root);

        Context context = new();

        ReadVersion(root, context);

        GroupNode imported = new()
        {
            Name = Text(root, "Name") ?? "Imported connections",
        };

        ReadChildren(root, imported, context);

        context.Finish();

        return new ImportResult(imported, context.Warnings, context.Groups, context.Servers);
    }

    /// <summary>
    /// mRemoteNG can encrypt the whole file rather than just the passwords in
    /// it, and what it writes then is not XML at all. Saying so beats "this
    /// file is not valid XML", which sends somebody looking for a corrupt file
    /// when what they have is a working one they need a password for.
    /// </summary>
    private static void GuardWholeFileEncryption(Stream stream)
    {
        if (!stream.CanSeek)
        {
            return;
        }

        long start = stream.Position;

        try
        {
            Span<byte> head = stackalloc byte[8];
            int read = stream.Read(head);

            foreach (byte b in head[..read])
            {
                // Byte order marks, the NUL half of a UTF-16 character, and
                // whitespace all come before the first real one.
                if (b is 0xEF or 0xBB or 0xBF or 0xFF or 0xFE or 0x00
                    or 0x20 or 0x09 or 0x0A or 0x0D)
                {
                    continue;
                }

                if (b != (byte)'<')
                {
                    throw new ImportException(
                        "This file does not start with XML, which is what mRemoteNG writes when "
                        + "the whole file is encrypted rather than just the passwords in it. "
                        + "Patchbay cannot read one of those. Turning off full file encryption "
                        + "in mRemoteNG and saving again produces a file it can.");
                }

                return;
            }
        }
        finally
        {
            stream.Position = start;
        }
    }

    private static void ReadVersion(XElement root, Context context)
    {
        string? raw = Text(root, "ConfVersion");

        if (raw is null)
        {
            // Early files omit it, and they are close enough to try.
            return;
        }

        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double version))
        {
            context.Warn(
                $"The file claims configuration version '{raw}', which is not a number. Reading "
                + "it anyway.");

            return;
        }

        if (version < OldestSupportedVersion)
        {
            throw new ImportException(
                $"This file uses mRemoteNG configuration version {raw}, which is older than "
                + "anything Patchbay can read.");
        }

        if (version > NewestSupportedVersion)
        {
            // Best effort rather than refusal, the same as the RDCMan reader:
            // the shape has been stable, and refusing outright is worse than
            // importing what is recognisable and saying so.
            context.Warn(
                $"This file was written by a newer mRemoteNG (configuration version {raw}). "
                + "Anything Patchbay did not recognise has been left out.");
        }
    }

    private static void ReadChildren(XElement parent, GroupNode target, Context context)
    {
        foreach (XElement child in parent.Elements())
        {
            if (!string.Equals(child.Name.LocalName, "Node", StringComparison.Ordinal))
            {
                continue;
            }

            if (string.Equals(Text(child, "Type"), "Container", StringComparison.OrdinalIgnoreCase))
            {
                ReadContainer(child, target, context);
            }
            else
            {
                ReadConnection(child, target, context);
            }
        }
    }

    private static void ReadContainer(XElement source, GroupNode parent, Context context)
    {
        GroupNode group = new()
        {
            Name = NodeOperations.UniqueName(parent, Text(source, "Name") ?? "Folder"),
            Notes = Owned(source, "Description") ? Text(source, "Descr") : null,
        };

        ReadSettings(source, group, context);

        parent.Add(group);
        context.Groups++;

        ReadChildren(source, group, context);
    }

    private static void ReadConnection(XElement source, GroupNode parent, Context context)
    {
        // Everything mRemoteNG can open, and Patchbay opens one of them. A
        // connection imported as an RDP session because its protocol was
        // ignored is worse than one that was left out and said so.
        string protocol = ResolveProtocol(source);

        if (!string.Equals(protocol, "RDP", StringComparison.OrdinalIgnoreCase))
        {
            context.OtherProtocol(protocol);
            return;
        }

        string? host = Text(source, "Hostname");

        if (host is null)
        {
            context.WithoutAnAddress++;
            return;
        }

        ServerNode server = new()
        {
            HostName = host,
            Name = NodeOperations.UniqueName(parent, Text(source, "Name") ?? host),
            Notes = Owned(source, "Description") ? Text(source, "Descr") : null,
        };

        ReadSettings(source, server, context);

        parent.Add(server);
        context.Servers++;
    }

    /// <summary>
    /// Copies the settings. Every one of them is guarded by its own
    /// <c>Inherit</c> attribute, which is the same sentence Patchbay says with
    /// a null.
    /// </summary>
    private static void ReadSettings(XElement source, ConnectionNode target, Context context)
    {
        ConnectionSettings settings = target.Settings;

        ReadConnectionSettings(source, settings, context);
        ReadCredentials(source, settings, context);
        ReadGateway(source, settings, context);
        ReadDisplay(source, settings, context);
        ReadLocalResources(source, settings, context);
        ReadExperience(source, settings);
        ReadSecurity(source, settings, context);
        ReadExternalTools(source, context);
    }

    private static void ReadConnectionSettings(
        XElement source,
        ConnectionSettings settings,
        Context context)
    {
        if (Owned(source, "Port") && Int(source, "Port") is { } port)
        {
            if (NodeValidator.IsValidPort(port))
            {
                settings.Port = port;
            }
            else
            {
                context.Warn(
                    $"A port of {port} is not one anything can connect to, so the inherited port "
                    + "is used instead.");
            }
        }

        if (Owned(source, "UseConsoleSession"))
        {
            settings.ConnectToConsole = Bool(source, "UseConsoleSession");
        }

        if (Owned(source, "RDPMinutesToIdleTimeout") && Int(source, "RDPMinutesToIdleTimeout") is { } idle and >= 0)
        {
            settings.IdleTimeoutMinutes = idle;
        }

        if (Text(source, "LoadBalanceInfo") is not null)
        {
            context.Note("connection broker routing");
        }
    }

    private static void ReadCredentials(
        XElement source,
        ConnectionSettings settings,
        Context context)
    {
        if (Owned(source, "Username"))
        {
            settings.UserName = Text(source, "Username");
        }

        if (Owned(source, "Domain"))
        {
            settings.Domain = Text(source, "Domain");
        }

        if (Text(source, "Password") is not null)
        {
            context.Passwords++;

            // A password was saved, so this connection was not meant to ask.
            // Asking is what is left, and it is the honest answer rather than
            // the convenient one — see the note at the top of the file.
            settings.CredentialMode = CredentialMode.Prompt;
        }
    }

    private static void ReadGateway(XElement source, ConnectionSettings settings, Context context)
    {
        if (Owned(source, "RDGatewayHostname"))
        {
            settings.GatewayHostName = Text(source, "RDGatewayHostname");
        }

        if (Owned(source, "RDGatewayUsername"))
        {
            settings.GatewayUserName = Text(source, "RDGatewayUsername");
        }

        if (Owned(source, "RDGatewayDomain"))
        {
            settings.GatewayDomain = Text(source, "RDGatewayDomain");
        }

        if (Owned(source, "RDGatewayUsageMethod") && Text(source, "RDGatewayUsageMethod") is { } usage)
        {
            settings.GatewayUsage = usage switch
            {
                "Never" => GatewayUsage.None,
                "Always" => GatewayUsage.Always,
                "Detect" => GatewayUsage.WhenDirectFails,
                _ => Unknown(),
            };
        }

        if (Owned(source, "RDGatewayUseConnectionCredentials")
            && Text(source, "RDGatewayUseConnectionCredentials") is { } sharing)
        {
            // Three values for two settings: the first two say whether the
            // gateway is offered the same account, and the third answers a
            // different question, about what it will take as proof.
            settings.GatewayUseSameCredentials = sharing switch
            {
                "Yes" => true,
                "No" => false,
                _ => null,
            };

            if (string.Equals(sharing, "SmartCard", StringComparison.Ordinal))
            {
                settings.GatewayCredentialSource = GatewayCredentialSource.SmartCard;
            }
        }

        if (Text(source, "RDGatewayPassword") is not null)
        {
            context.GatewayPasswords++;
        }

        GatewayUsage? Unknown()
        {
            context.Warn(
                $"A gateway setting of '{usage}' was not recognised, so the gateway was left to "
                + "inherit.");

            return null;
        }
    }

    private static void ReadDisplay(XElement source, ConnectionSettings settings, Context context)
    {
        if (Owned(source, "Colors") && Text(source, "Colors") is { } colours)
        {
            settings.ColourDepth = colours switch
            {
                "Colors15Bit" => ColourDepth.HighColour15,
                "Colors16Bit" => ColourDepth.HighColour16,
                "Colors24Bit" => ColourDepth.TrueColour24,
                "Colors32Bit" => ColourDepth.TrueColour32,
                _ => UnknownDepth(),
            };
        }

        if (Owned(source, "Resolution") && Text(source, "Resolution") is { } resolution)
        {
            // A single setting holding two different answers. Res1280x1024 is
            // a size; SmartSize is a way of handling the window; FitToWindow
            // and Fullscreen are neither, and Patchbay has nowhere to put them.
            Match match = ResolutionPattern().Match(resolution);

            if (match.Success)
            {
                settings.DesktopWidth = int.Parse(match.Groups["w"].ValueSpan, CultureInfo.InvariantCulture);
                settings.DesktopHeight = int.Parse(match.Groups["h"].ValueSpan, CultureInfo.InvariantCulture);
                settings.UseSmartSizing = false;
            }
            else if (string.Equals(resolution, "SmartSize", StringComparison.Ordinal))
            {
                settings.UseSmartSizing = true;
            }
            else
            {
                context.Note("opening full screen or sized to the window");
            }
        }

        ColourDepth? UnknownDepth()
        {
            context.Warn(
                $"A colour depth of '{colours}' is not one Patchbay offers, so the inherited "
                + "depth is used instead.");

            return null;
        }
    }

    private static void ReadLocalResources(
        XElement source,
        ConnectionSettings settings,
        Context context)
    {
        if (Owned(source, "RedirectClipboard"))
        {
            settings.RedirectClipboard = Bool(source, "RedirectClipboard");
        }

        if (Owned(source, "RedirectDiskDrives"))
        {
            settings.RedirectDrives = Bool(source, "RedirectDiskDrives");
        }

        if (Owned(source, "RedirectPrinters"))
        {
            settings.RedirectPrinters = Bool(source, "RedirectPrinters");
        }

        if (Owned(source, "RedirectSmartCards"))
        {
            settings.RedirectSmartCards = Bool(source, "RedirectSmartCards");
        }

        if (Owned(source, "RedirectPorts"))
        {
            settings.RedirectPorts = Bool(source, "RedirectPorts");
        }

        if (Owned(source, "RedirectAudioCapture"))
        {
            settings.RedirectMicrophone = Bool(source, "RedirectAudioCapture");
        }

        if (Owned(source, "RedirectSound") && Text(source, "RedirectSound") is { } sound)
        {
            settings.AudioMode = sound switch
            {
                "BringToThisComputer" => AudioMode.PlayLocally,
                "LeaveAtRemoteComputer" => AudioMode.PlayRemotely,
                "DoNotPlay" => AudioMode.DoNotPlay,
                _ => null,
            };
        }

        if (Owned(source, "SoundQuality") && Text(source, "SoundQuality") is { } quality)
        {
            settings.AudioQuality = quality switch
            {
                "Dynamic" => AudioQuality.Dynamic,
                "Medium" => AudioQuality.Medium,
                "High" => AudioQuality.High,
                _ => null,
            };
        }

        if (Bool(source, "RedirectKeys") is true)
        {
            context.Note("sending Windows key combinations to the far end");
        }

        // Counted rather than refused, because this file is an inventory
        // somebody asked to import rather than a message that arrived. What it
        // gets instead is a sentence saying how many, which is the thing worth
        // knowing when the file came from a colleague.
        if (settings.RedirectDrives is true
            || settings.RedirectSmartCards is true
            || settings.RedirectPorts is true
            || settings.RedirectMicrophone is true)
        {
            context.HandsSomethingOver++;
        }
    }

    /// <summary>
    /// How the desktop is allowed to look. These read the right way round,
    /// unlike a <c>.rdp</c>, where four of the same settings are written as
    /// "disable" and mean the opposite of what they say.
    /// </summary>
    private static void ReadExperience(XElement source, ConnectionSettings settings)
    {
        if (Owned(source, "DisplayWallpaper"))
        {
            settings.DesktopBackground = Bool(source, "DisplayWallpaper");
        }

        if (Owned(source, "DisplayThemes"))
        {
            settings.VisualStyles = Bool(source, "DisplayThemes");
        }

        if (Owned(source, "EnableFontSmoothing"))
        {
            settings.FontSmoothing = Bool(source, "EnableFontSmoothing");
        }

        if (Owned(source, "EnableDesktopComposition"))
        {
            settings.DesktopComposition = Bool(source, "EnableDesktopComposition");
        }

        if (Owned(source, "CacheBitmaps"))
        {
            settings.PersistentBitmapCache = Bool(source, "CacheBitmaps");
        }
    }

    private static void ReadSecurity(XElement source, ConnectionSettings settings, Context context)
    {
        if (Owned(source, "RDPAuthenticationLevel")
            && Text(source, "RDPAuthenticationLevel") is { } level)
        {
            settings.ServerAuthentication = level switch
            {
                "AuthRequired" => ServerAuthentication.Require,
                "WarnOnFailedAuth" => ServerAuthentication.Warn,
                "NoAuth" => ServerAuthentication.Connect,
                _ => null,
            };

            if (string.Equals(level, "NoAuth", StringComparison.Ordinal))
            {
                context.WithoutAnIdentityCheck++;
            }
        }

        if (Owned(source, "UseCredSsp") && Bool(source, "UseCredSsp") is false)
        {
            context.WithoutNetworkLevelAuthentication++;
        }
    }

    /// <summary>
    /// mRemoteNG can run a tool on <em>this</em> computer before and after a
    /// connection. Patchbay does not, and the tools themselves live in a
    /// different file, so there is nothing to import — but a connection that
    /// was set up to run something locally is worth knowing about rather than
    /// dropping in silence.
    /// </summary>
    private static void ReadExternalTools(XElement source, Context context)
    {
        string[] attributes = ["PreExtApp", "PostExtApp"];

        foreach (string attribute in attributes)
        {
            if (Owned(source, attribute) && Text(source, attribute) is { } tool)
            {
                context.ExternalTool(tool);
            }
        }
    }

    // ── Reading values ──────────────────────────────────────────────────

    /// <summary>
    /// What a connection actually opens, following <c>InheritProtocol</c> up
    /// the tree rather than assuming. A file where the folder says SSH and the
    /// connections all inherit is an ordinary file, and reading each of them
    /// as RDP would fill the tree with connections that cannot work.
    /// </summary>
    private static string ResolveProtocol(XElement node)
    {
        for (XElement? current = node; current is not null; current = current.Parent)
        {
            if (Owned(current, "Protocol"))
            {
                return Text(current, "Protocol") ?? "RDP";
            }
        }

        return "RDP";
    }

    /// <summary>
    /// Whether a node sets a value itself. <c>InheritColors="true"</c> means
    /// "take the parent's", which Patchbay says by leaving the property null —
    /// so there is nothing to do and nothing to resolve.
    /// </summary>
    private static bool Owned(XElement node, string setting) =>
        Bool(node, "Inherit" + setting) is not true;

    private static string? Text(XElement node, string name)
    {
        string? value = (string?)node.Attribute(name);

        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool? Bool(XElement node, string name) =>
        Text(node, name) is { } value && bool.TryParse(value, out bool parsed) ? parsed : null;

    private static int? Int(XElement node, string name) =>
        Text(node, name) is { } value
            && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : null;

    [GeneratedRegex(@"^Res(?<w>\d{3,5})x(?<h>\d{3,5})$", RegexOptions.CultureInvariant)]
    private static partial Regex ResolutionPattern();

    /// <summary>
    /// Collects counts and warnings while the walk happens, so that a file
    /// with four hundred password-bearing connections produces one sentence
    /// about it rather than four hundred.
    /// </summary>
    private sealed class Context
    {
        private readonly List<string> _warnings = [];
        private readonly SortedSet<string> _unsupported = new(StringComparer.Ordinal);
        private readonly SortedSet<string> _protocols = new(StringComparer.OrdinalIgnoreCase);
        private readonly SortedSet<string> _tools = new(StringComparer.Ordinal);

        private int _otherProtocols;

        public int Groups { get; set; }

        public int Servers { get; set; }

        public int WithoutAnAddress { get; set; }

        public int Passwords { get; set; }

        public int GatewayPasswords { get; set; }

        public int HandsSomethingOver { get; set; }

        public int WithoutAnIdentityCheck { get; set; }

        public int WithoutNetworkLevelAuthentication { get; set; }

        public IReadOnlyList<string> Warnings => _warnings;

        public void Warn(string message) => _warnings.Add(message);

        /// <summary>Records a feature Patchbay does not model, once.</summary>
        public void Note(string feature) => _unsupported.Add(feature);

        /// <summary>Records a connection Patchbay cannot open.</summary>
        public void OtherProtocol(string protocol)
        {
            _protocols.Add(protocol);
            _otherProtocols++;
        }

        /// <summary>Records a tool a connection runs on this computer.</summary>
        public void ExternalTool(string tool) => _tools.Add(tool);

        public void Finish()
        {
            if (Passwords > 0 || GatewayPasswords > 0)
            {
                _warnings.Add(
                    $"Saved passwords were not imported ({Passwords} connections, "
                    + $"{GatewayPasswords} gateways). mRemoteNG encrypts them under a key derived "
                    + "from a password, and reading somebody's credential store is a thing to do "
                    + "deliberately rather than in the middle of an import, so those connections "
                    + "will ask when they connect. User names and domains did come across.");
            }

            if (_otherProtocols > 0)
            {
                _warnings.Add(
                    $"{_otherProtocols} connections use {Join(_protocols)} rather than RDP and "
                    + "were left out. Patchbay opens remote desktops and nothing else, so "
                    + "importing them as RDP would produce connections that cannot work.");
            }

            if (HandsSomethingOver > 0)
            {
                _warnings.Add(
                    $"{HandsSomethingOver} connections and folders offer this computer's drives, smart card "
                    + "reader, ports or microphone to the machine they connect to. That is how "
                    + "they were set up and it came across as it stands — worth a look if this "
                    + "file came from somebody else.");
            }

            if (WithoutAnIdentityCheck > 0)
            {
                _warnings.Add(
                    $"{WithoutAnIdentityCheck} connections are set to connect without checking "
                    + "the identity of the server. That came across as it stands, and it is worth "
                    + "changing: a session to a server that cannot prove who it is looks exactly "
                    + "like one to a server that can.");
            }

            if (WithoutNetworkLevelAuthentication > 0)
            {
                _warnings.Add(
                    $"{WithoutNetworkLevelAuthentication} connections switch off network level "
                    + "authentication, which moves the logon to a screen drawn by the far end. "
                    + "Patchbay has no setting for it, so those connections will use it.");
            }

            if (_tools.Count > 0)
            {
                _warnings.Add(
                    $"Some connections run a tool on this computer before or after connecting "
                    + $"({Join(_tools)}). Patchbay does not run external tools, and the tools "
                    + "themselves are in a different mRemoteNG file, so nothing was imported "
                    + "from that.");
            }

            if (WithoutAnAddress > 0)
            {
                _warnings.Add($"{WithoutAnAddress} entries had no address and were left out.");
            }

            if (_unsupported.Count > 0)
            {
                _warnings.Add(
                    $"Patchbay does not handle {Join(_unsupported)} yet, so those settings were "
                    + "not carried over.");
            }
        }

        private static string Join(SortedSet<string> items) => items.Count switch
        {
            1 => items.First(),
            _ => string.Join(", ", items.Take(items.Count - 1)) + " and " + items.Last(),
        };
    }
}
