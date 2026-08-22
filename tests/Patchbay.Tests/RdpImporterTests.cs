using System.Reflection;
using System.Text;
using Patchbay.Core.Import;
using Patchbay.Core.Model;

namespace Patchbay.Tests;

public sealed class RdpImporterTests : IDisposable
{
    /// <summary>
    /// A file in the shape <c>mstsc.exe</c> saves one: every setting written
    /// whether or not it was changed, the address carrying a port, and a
    /// password blob belonging to whoever saved it.
    /// </summary>
    private const string Sample = """
        screen mode id:i:2
        use multimon:i:0
        desktopwidth:i:1600
        desktopheight:i:900
        session bpp:i:32
        winposstr:s:0,3,0,0,800,600
        compression:i:1
        keyboardhook:i:2
        audiocapturemode:i:0
        videoplaybackmode:i:1
        connection type:i:6
        networkautodetect:i:0
        bandwidthautodetect:i:1
        displayconnectionbar:i:1
        disable wallpaper:i:1
        allow font smoothing:i:1
        allow desktop composition:i:0
        disable full window drag:i:1
        disable menu anims:i:1
        disable themes:i:0
        disable cursor setting:i:0
        bitmapcachepersistenable:i:1
        full address:s:web-prd-01.corp.local:3390
        audiomode:i:0
        audioqualitymode:i:0
        redirectprinters:i:1
        redirectcomports:i:0
        redirectsmartcards:i:0
        redirectclipboard:i:1
        redirectposdevices:i:0
        drivestoredirect:s:
        autoreconnection enabled:i:1
        authentication level:i:2
        prompt for credentials:i:0
        negotiate security layer:i:1
        remoteapplicationmode:i:0
        alternate shell:s:
        shell working directory:s:
        gatewayhostname:s:
        gatewayusagemethod:i:4
        gatewaycredentialssource:i:4
        gatewayprofileusagemethod:i:0
        promptcredentialonce:i:1
        kdcproxyname:s:
        loadbalanceinfo:s:
        username:s:CORP\rdpadmin
        domain:s:
        password 51:b:01000000d08c9ddf0115d1118c7a00c04fc297eb
        """;

    private readonly string _folder;

    public RdpImporterTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), $"patchbay-rdp-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    // ── A file the Remote Desktop client wrote ──────────────────────────

    [Fact]
    public void The_address_and_the_port_arrive_from_the_one_line()
    {
        ServerNode server = Only(RdpImporter.Import(Sample));

        Assert.Equal("web-prd-01.corp.local", server.HostName);
        Assert.Equal(3390, server.Settings.Port);
    }

    [Fact]
    public void An_explicit_port_line_wins_over_one_carried_in_the_address()
    {
        ServerNode server = Machine("server port:i:3391", "full address:s:web-01:3390");

        Assert.Equal(3391, server.Settings.Port);
    }

    [Fact]
    public void A_port_nothing_could_connect_to_is_left_to_inherit()
    {
        ImportResult result = RdpImporter.Import("full address:s:web-01:70000\n");

        Assert.Null(Only(result).Settings.Port);
        Assert.Contains(result.Warnings, w => w.Contains("70000", StringComparison.Ordinal));
    }

    /// <summary>
    /// Two colons and no brackets is an address, not a machine and a port. The
    /// wrong answer here produces a connection to a host called <c>fe80</c>.
    /// </summary>
    [Fact]
    public void A_bare_IPv6_address_is_not_read_as_a_host_and_a_port()
    {
        ServerNode server = Only(RdpImporter.Import("full address:s:fe80::1\n"));

        Assert.Equal("fe80::1", server.HostName);
        Assert.Null(server.Settings.Port);
    }

    [Fact]
    public void A_bracketed_IPv6_address_gives_up_its_port()
    {
        ServerNode server = Only(RdpImporter.Import("full address:s:[fe80::1]:3390\n"));

        Assert.Equal("fe80::1", server.HostName);
        Assert.Equal(3390, server.Settings.Port);
    }

    [Fact]
    public void The_display_settings_come_across()
    {
        ConnectionSettings settings = Only(RdpImporter.Import(Sample)).Settings;

        Assert.Equal(1600, settings.DesktopWidth);
        Assert.Equal(900, settings.DesktopHeight);
        Assert.Equal(ColourDepth.TrueColour32, settings.ColourDepth);
    }

    [Fact]
    public void A_colour_depth_Patchbay_does_not_offer_is_left_to_inherit()
    {
        ImportResult result = RdpImporter.Import("full address:s:web-01\nsession bpp:i:8\n");

        Assert.Null(Only(result).Settings.ColourDepth);
        Assert.Contains(result.Warnings, w => w.Contains("8 bits", StringComparison.Ordinal));
    }

    /// <summary>
    /// The file says what to switch off and the setting says what to switch
    /// on, so four of these seven are read backwards from how they are
    /// written. Getting one wrong produces a session that connects perfectly
    /// and looks wrong.
    /// </summary>
    [Fact]
    public void The_experience_settings_written_as_disable_are_read_as_enable()
    {
        ConnectionSettings settings = Only(RdpImporter.Import(Sample)).Settings;

        Assert.False(settings.DesktopBackground);
        Assert.False(settings.ShowWindowContentsWhileDragging);
        Assert.False(settings.MenuAnimations);
        Assert.True(settings.VisualStyles);
        Assert.True(settings.FontSmoothing);
        Assert.False(settings.DesktopComposition);
        Assert.True(settings.PersistentBitmapCache);
    }

    [Fact]
    public void A_named_link_speed_is_read_when_nothing_asks_for_detection()
    {
        Assert.Equal(ConnectionQuality.Lan, Only(RdpImporter.Import(Sample)).Settings.ConnectionQuality);
    }

    [Fact]
    public void Detection_beats_a_named_link_speed()
    {
        ConnectionSettings settings =
            Machine("connection type:i:2", "networkautodetect:i:1").Settings;

        Assert.Equal(ConnectionQuality.Detect, settings.ConnectionQuality);
    }

    [Fact]
    public void The_gateway_comes_across()
    {
        ConnectionSettings settings = Machine(
            "gatewayhostname:s:rdg.corp.local",
            "gatewayusagemethod:i:2",
            "gatewaycredentialssource:i:1",
            "promptcredentialonce:i:1").Settings;

        Assert.Equal("rdg.corp.local", settings.GatewayHostName);
        Assert.Equal(GatewayUsage.WhenDirectFails, settings.GatewayUsage);
        Assert.Equal(GatewayCredentialSource.SmartCard, settings.GatewayCredentialSource);
        Assert.True(settings.GatewayUseSameCredentials);
    }

    /// <summary>
    /// Mode 3 means "whatever a policy on this machine says", which is not one
    /// of the three answers Patchbay has. Guessing at it would route a session
    /// through a gateway nobody named, or past one somebody did.
    /// </summary>
    [Fact]
    public void A_gateway_mode_Patchbay_does_not_have_is_left_to_inherit()
    {
        ImportResult result = RdpImporter.Import(
            "full address:s:web-01\ngatewayhostname:s:rdg.corp.local\ngatewayusagemethod:i:3\n");

        Assert.Null(Only(result).Settings.GatewayUsage);
        Assert.Contains(result.Warnings, w => w.Contains("gateway mode 3", StringComparison.Ordinal));
    }

    [Fact]
    public void A_gateway_that_is_switched_off_is_imported_as_off()
    {
        Assert.Equal(GatewayUsage.None, Only(RdpImporter.Import(Sample)).Settings.GatewayUsage);
    }

    /// <summary>
    /// Minutes in the file, seconds in the document, milliseconds on the
    /// control. Two conversions, and each one is a factor of sixty or a
    /// thousand away from a keep-alive that floods or never fires.
    /// </summary>
    [Fact]
    public void A_keep_alive_in_minutes_becomes_seconds()
    {
        Assert.Equal(120, Machine("keepalive interval:i:2").Settings.KeepAliveIntervalSeconds);
    }

    [Fact]
    public void Everything_the_file_does_not_mention_is_left_to_inherit()
    {
        ConnectionSettings settings = Only(RdpImporter.Import("full address:s:web-01\n")).Settings;

        foreach (PropertyInfo property in typeof(ConnectionSettings)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.CanWrite)
            {
                Assert.Null(property.GetValue(settings));
            }
        }
    }

    // ── Credentials ─────────────────────────────────────────────────────

    [Fact]
    public void A_domain_carried_inside_the_user_name_is_split_out()
    {
        ConnectionSettings settings = Only(RdpImporter.Import(Sample)).Settings;

        Assert.Equal("CORP", settings.Domain);
        Assert.Equal("rdpadmin", settings.UserName);
    }

    [Fact]
    public void A_domain_of_its_own_is_left_alone()
    {
        ConnectionSettings settings = Machine("username:s:rdpadmin", "domain:s:CORP").Settings;

        Assert.Equal("CORP", settings.Domain);
        Assert.Equal("rdpadmin", settings.UserName);
    }

    [Fact]
    public void A_saved_password_is_counted_and_the_connection_asks_instead()
    {
        ImportResult result = RdpImporter.Import(Sample);

        Assert.Equal(CredentialMode.Prompt, Only(result).Settings.CredentialMode);
        Assert.Contains(
            result.Warnings,
            w => w.Contains("Saved passwords were not imported", StringComparison.Ordinal));
    }

    // ── The shape of the file ───────────────────────────────────────────

    /// <summary>
    /// mstsc writes UTF-16. Read as UTF-8 the file is a NUL between every
    /// letter, and every line fails to parse — an import that reports nothing
    /// wrong and produces nothing at all.
    /// </summary>
    [Fact]
    public void A_file_written_in_UTF16_reads_the_same_as_one_written_in_UTF8()
    {
        ServerNode utf16 = Only(RdpImporter.Import(Encoded(Sample, Encoding.Unicode)));
        ServerNode utf8 = Only(RdpImporter.Import(Encoded(Sample, new UTF8Encoding(true))));

        Assert.Equal(utf16.HostName, utf8.HostName);
        Assert.Equal(utf16.Settings.Port, utf8.Settings.Port);
    }

    [Fact]
    public void Lines_that_are_not_settings_are_skipped()
    {
        RdpFile file = RdpFile.Read("full address:s:web-01\nnonsense\n\n:i:4\nport:x:3389\n");

        Assert.Equal("web-01", file.Text("full address"));
        Assert.Equal(3, file.UnreadableLines);
    }

    [Fact]
    public void A_value_holding_colons_survives_intact()
    {
        Assert.Equal("0,3,0,0,800,600", RdpFile.Read("winposstr:s:0,3,0,0,800,600\n").Text("winposstr"));
        Assert.Equal("web-01:3390", RdpFile.Read("full address:s:web-01:3390\n").Text("full address"));
    }

    [Fact]
    public void A_name_is_matched_whatever_its_case()
    {
        Assert.Equal("web-01", RdpFile.Read("Full Address:s:web-01\n").Text("full address"));
    }

    /// <summary>
    /// Nothing that writes these files repeats a setting, so a file that does
    /// is either hand-edited or built to read one way and behave another.
    /// </summary>
    [Fact]
    public void A_repeated_setting_takes_the_last_value_and_says_so()
    {
        ImportResult result = RdpImporter.Import(
            "full address:s:web-01\nauthentication level:i:2\nauthentication level:i:1\n");

        Assert.Equal(ServerAuthentication.Require, Only(result).Settings.ServerAuthentication);
        Assert.Contains(
            result.Warnings,
            w => w.Contains("authentication level", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_setting_repeated_with_the_same_value_is_not_worth_mentioning()
    {
        ImportResult result = RdpImporter.Import(
            "full address:s:web-01\nsession bpp:i:32\nsession bpp:i:32\n");

        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void A_file_that_names_no_machine_is_refused()
    {
        ImportException ex = Assert.Throws<ImportException>(
            () => RdpImporter.Import("session bpp:i:32\n"));

        Assert.Contains("names no machine", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_that_is_not_a_connection_file_at_all_is_refused()
    {
        Assert.Throws<ImportException>(
            () => RdpImporter.Import("<?xml version=\"1.0\"?><RDCMan />"));
    }

    // ── Several files at once ───────────────────────────────────────────

    /// <summary>
    /// One connection does not need a folder around it. The group exists
    /// because <see cref="ImportResult"/> holds one, not because the file
    /// described anything.
    /// </summary>
    [Fact]
    public void One_file_goes_into_the_tree_without_a_group_around_it()
    {
        ImportResult result = RdpImporter.ImportFile(Save("WEB-PRD-01.rdp", Sample));

        ServerNode server = Assert.IsType<ServerNode>(result.Node);
        Assert.Equal("WEB-PRD-01", server.Name);
    }

    [Fact]
    public void The_connection_is_named_after_the_file_rather_than_the_address()
    {
        ImportResult result = RdpImporter.ImportFile(Save("Payroll (live).rdp", Sample));

        Assert.Equal("Payroll (live)", Only(result).Name);
        Assert.Equal("web-prd-01.corp.local", Only(result).HostName);
    }

    [Fact]
    public void Several_files_arrive_in_one_group()
    {
        ImportResult result = RdpImporter.ImportFiles(
        [
            Save("WEB-01.rdp", "full address:s:web-01\n"),
            Save("WEB-02.rdp", "full address:s:web-02\n"),
        ]);

        GroupNode group = Assert.IsType<GroupNode>(result.Node);
        Assert.Equal(2, result.ServerCount);
        Assert.Equal(["WEB-01", "WEB-02"], group.ChildServers.Select(s => s.Name));
    }

    [Fact]
    public void Two_files_of_the_same_name_do_not_collide()
    {
        ImportResult result = RdpImporter.ImportFiles(
        [
            Save("WEB-01.rdp", "full address:s:web-01\n"),
            Save("nested/WEB-01.rdp", "full address:s:web-02\n"),
        ]);

        GroupNode group = Assert.IsType<GroupNode>(result.Node);
        Assert.Equal(["WEB-01", "WEB-01 (2)"], group.ChildServers.Select(s => s.Name));
    }

    /// <summary>
    /// Twenty files chosen at once and one of them bad: the nineteen are what
    /// was being asked for, and losing them is what gets worked around by
    /// importing one at a time.
    /// </summary>
    [Fact]
    public void A_file_that_cannot_be_read_does_not_lose_the_others()
    {
        ImportResult result = RdpImporter.ImportFiles(
        [
            Save("WEB-01.rdp", "full address:s:web-01\n"),
            Save("broken.rdp", "session bpp:i:32\n"),
        ]);

        Assert.Equal(1, result.ServerCount);
        Assert.Contains(result.Warnings, w => w.Contains("broken.rdp", StringComparison.Ordinal));
    }

    [Fact]
    public void Nothing_readable_at_all_is_a_failed_import()
    {
        Assert.Throws<ImportException>(() => RdpImporter.ImportFiles(
        [
            Save("broken.rdp", "session bpp:i:32\n"),
        ]));
    }

    // ── Choosing which reader gets the file ─────────────────────────────

    [Fact]
    public void The_extension_decides_which_reader_gets_the_file()
    {
        ImportResult result = ConnectionImport.From(Save("WEB-01.rdp", Sample));

        Assert.Equal("web-prd-01.corp.local", Assert.IsType<ServerNode>(result.Node).HostName);
    }

    [Fact]
    public void A_file_of_some_other_kind_is_refused_by_name()
    {
        ImportException ex = Assert.Throws<ImportException>(
            () => ConnectionImport.From(Save("servers.txt", Sample)));

        Assert.Contains("servers.txt", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_two_formats_can_be_imported_together()
    {
        ImportResult result = ConnectionImport.From(
        [
            Save("Corp.rdg", Rdg),
            Save("WEB-01.rdp", "full address:s:web-01\n"),
        ]);

        GroupNode group = Assert.IsType<GroupNode>(result.Node);

        Assert.Equal(2, result.ServerCount);
        Assert.Equal(["Corp servers"], group.ChildGroups.Select(g => g.Name));
        Assert.Equal(["WEB-01"], group.ChildServers.Select(s => s.Name));
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private const string Rdg = """
        <?xml version="1.0" encoding="utf-8"?>
        <RDCMan programVersion="2.83" schemaVersion="3">
          <file>
            <properties><name>Corp servers</name></properties>
            <server>
              <properties><name>db-01.corp.local</name></properties>
            </server>
          </file>
        </RDCMan>
        """;

    private static ServerNode Only(ImportResult result) =>
        Assert.IsType<ServerNode>(Assert.Single(result.Root.Children));

    /// <summary>A file holding an address and whatever else the test is about.</summary>
    private static ServerNode Machine(params string[] lines) =>
        Only(RdpImporter.Import(
            string.Join('\n', lines.Any(l => l.StartsWith("full address", StringComparison.Ordinal))
                ? lines
                : lines.Prepend("full address:s:web-01"))));

    private static MemoryStream Encoded(string text, Encoding encoding) =>
        new MemoryStream([.. encoding.GetPreamble(), .. encoding.GetBytes(text)]);

    private string Save(string name, string text)
    {
        string path = Path.Combine(_folder, name);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);

        return path;
    }
}
