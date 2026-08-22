using System.Text;
using Patchbay.Core.Import;
using Patchbay.Core.Model;

namespace Patchbay.Tests;

public sealed class MremoteNgImporterTests : IDisposable
{
    /// <summary>
    /// A file in the shape mRemoteNG writes one: a container that owns some
    /// settings and inherits the rest, connections that inherit almost
    /// everything from it, a protocol Patchbay does not open, and passwords
    /// that must not come across.
    /// </summary>
    private const string Sample = """
        <?xml version="1.0" encoding="utf-8"?>
        <mrng:Connections xmlns:mrng="http://mremoteng.org"
                          Name="Corp servers"
                          Export="false"
                          EncryptionEngine="AES"
                          BlockCipherMode="GCM"
                          KdfIterations="1000"
                          FullFileEncryption="false"
                          Protected="R0hIT0xkT2ZUaGVQcm90ZWN0ZWRTdHJpbmc="
                          ConfVersion="2.6">
          <Node Name="Production"
                Type="Container"
                Descr="Change freeze at the weekend."
                Username="svc_rdp"
                Domain="CORP"
                Password="cHJvZHVjdGlvbi1wYXNzd29yZC1ibG9i"
                Protocol="RDP"
                Port="3389"
                Colors="Colors32Bit"
                Resolution="Res1600x900"
                RDGatewayUsageMethod="Detect"
                RDGatewayHostname="rdg.corp.local"
                RDGatewayUseConnectionCredentials="Yes"
                RDPAuthenticationLevel="AuthRequired"
                InheritUsername="false"
                InheritDomain="false"
                InheritPassword="false"
                InheritProtocol="false"
                InheritPort="false"
                InheritColors="false"
                InheritResolution="false"
                InheritDescription="false"
                InheritRDGatewayUsageMethod="false"
                InheritRDGatewayHostname="false"
                InheritRDGatewayUseConnectionCredentials="false"
                InheritRDPAuthenticationLevel="false">
            <Node Name="WEB-PRD-01"
                  Type="Connection"
                  Descr=""
                  Hostname="web-prd-01.corp.local"
                  Username=""
                  Domain=""
                  Password="d2ViLXBhc3N3b3JkLWJsb2I="
                  Protocol="RDP"
                  Port="3390"
                  RedirectDiskDrives="true"
                  RedirectSound="LeaveAtRemoteComputer"
                  SoundQuality="High"
                  DisplayWallpaper="true"
                  DisplayThemes="false"
                  EnableFontSmoothing="true"
                  RDPMinutesToIdleTimeout="30"
                  InheritUsername="true"
                  InheritDomain="true"
                  InheritPassword="false"
                  InheritProtocol="false"
                  InheritPort="false"
                  InheritColors="true"
                  InheritResolution="true"
                  InheritDescription="true"
                  InheritRedirectDiskDrives="false"
                  InheritRedirectSound="false"
                  InheritSoundQuality="false"
                  InheritDisplayWallpaper="false"
                  InheritDisplayThemes="false"
                  InheritEnableFontSmoothing="false"
                  InheritRDPMinutesToIdleTimeout="false"
                  InheritRDGatewayUsageMethod="true"
                  InheritRDPAuthenticationLevel="true" />
            <Node Name="DB-PRD-01"
                  Type="Connection"
                  Hostname="db-prd-01.corp.local"
                  Protocol="RDP"
                  InheritProtocol="false"
                  InheritUsername="true"
                  InheritDomain="true"
                  InheritPassword="true"
                  InheritPort="true"
                  InheritColors="true"
                  InheritResolution="true"
                  InheritDescription="true"
                  InheritRDGatewayUsageMethod="true"
                  InheritRDPAuthenticationLevel="true" />
            <Node Name="core-sw-01"
                  Type="Connection"
                  Hostname="core-sw-01.corp.local"
                  Protocol="SSH2"
                  Port="22"
                  InheritProtocol="false"
                  InheritPort="false" />
          </Node>
          <Node Name="Lab"
                Type="Container"
                Protocol="SSH2"
                InheritProtocol="false">
            <Node Name="lab-jump"
                  Type="Connection"
                  Hostname="lab-jump.corp.local"
                  InheritProtocol="true" />
          </Node>
        </mrng:Connections>
        """;

    private readonly string _folder;

    public MremoteNgImporterTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), $"patchbay-mrng-{Guid.NewGuid():N}");
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

    // ── The tree ────────────────────────────────────────────────────────

    [Fact]
    public void The_tree_comes_across()
    {
        ImportResult result = MremoteNgImporter.Import(Sample);

        Assert.Equal("Corp servers", result.Root.Name);
        Assert.Equal(2, result.GroupCount);
        Assert.Equal(2, result.ServerCount);
        Assert.Equal(["Production", "Lab"], result.Root.ChildGroups.Select(g => g.Name));
    }

    [Fact]
    public void A_container_becomes_a_group_that_carries_what_it_owns()
    {
        GroupNode production = Group("Production");

        Assert.Equal("Change freeze at the weekend.", production.Notes);
        Assert.Equal("svc_rdp", production.Settings.UserName);
        Assert.Equal("CORP", production.Settings.Domain);
        Assert.Equal(ColourDepth.TrueColour32, production.Settings.ColourDepth);
        Assert.Equal(1600, production.Settings.DesktopWidth);
        Assert.Equal(900, production.Settings.DesktopHeight);
    }

    /// <summary>
    /// The whole reason this format maps cleanly. mRemoteNG says "inherit" per
    /// setting rather than per block, which is what Patchbay says with a null —
    /// so an inherited setting needs no resolution at import time and goes on
    /// working the way it was set up.
    /// </summary>
    [Fact]
    public void A_setting_marked_inherit_is_left_null()
    {
        ConnectionSettings web = Server("WEB-PRD-01").Settings;

        Assert.Null(web.UserName);
        Assert.Null(web.Domain);
        Assert.Null(web.ColourDepth);
        Assert.Null(web.DesktopWidth);
        Assert.Null(web.GatewayUsage);
        Assert.Null(web.ServerAuthentication);
    }

    [Fact]
    public void A_setting_the_node_owns_overrides_the_one_above_it()
    {
        Assert.Equal(3390, Server("WEB-PRD-01").Settings.Port);
        Assert.Equal(3389, Group("Production").Settings.Port);
    }

    [Fact]
    public void A_connection_that_inherits_everything_carries_nothing_of_its_own()
    {
        ConnectionSettings db = Server("DB-PRD-01").Settings;

        Assert.Null(db.Port);
        Assert.Null(db.UserName);
        Assert.Null(db.ColourDepth);
        Assert.Null(db.RedirectDrives);
    }

    [Fact]
    public void An_inherited_description_does_not_become_a_note()
    {
        Assert.Null(Server("WEB-PRD-01").Notes);
    }

    // ── Settings ────────────────────────────────────────────────────────

    /// <summary>
    /// These read the right way round, unlike a <c>.rdp</c>, where four of the
    /// same settings are written as "disable" and mean the opposite of what
    /// they say.
    /// </summary>
    [Fact]
    public void The_experience_settings_are_not_inverted()
    {
        ConnectionSettings web = Server("WEB-PRD-01").Settings;

        Assert.True(web.DesktopBackground);
        Assert.False(web.VisualStyles);
        Assert.True(web.FontSmoothing);
    }

    [Fact]
    public void Sound_and_its_quality_come_across()
    {
        ConnectionSettings web = Server("WEB-PRD-01").Settings;

        Assert.Equal(AudioMode.PlayRemotely, web.AudioMode);
        Assert.Equal(AudioQuality.High, web.AudioQuality);
    }

    [Fact]
    public void The_idle_timeout_comes_across()
    {
        Assert.Equal(30, Server("WEB-PRD-01").Settings.IdleTimeoutMinutes);
    }

    [Fact]
    public void The_gateway_comes_across()
    {
        ConnectionSettings production = Group("Production").Settings;

        Assert.Equal("rdg.corp.local", production.GatewayHostName);
        Assert.Equal(GatewayUsage.WhenDirectFails, production.GatewayUsage);
        Assert.True(production.GatewayUseSameCredentials);
        Assert.Equal(ServerAuthentication.Require, production.ServerAuthentication);
    }

    /// <summary>
    /// One attribute holding two different answers: whether the gateway gets
    /// the same account, and what it will take as proof. A smart card is not
    /// a "no" to the first question, it is an answer to the second.
    /// </summary>
    [Fact]
    public void A_gateway_that_wants_a_smart_card_sets_the_other_setting()
    {
        ConnectionSettings settings = Settings("""
            RDGatewayUseConnectionCredentials="SmartCard"
            InheritRDGatewayUseConnectionCredentials="false"
            """);

        Assert.Null(settings.GatewayUseSameCredentials);
        Assert.Equal(GatewayCredentialSource.SmartCard, settings.GatewayCredentialSource);
    }

    [Fact]
    public void A_fixed_resolution_becomes_a_size_and_turns_scaling_off()
    {
        ConnectionSettings settings = Settings("""Resolution="Res1280x1024" InheritResolution="false" """);

        Assert.Equal(1280, settings.DesktopWidth);
        Assert.Equal(1024, settings.DesktopHeight);
        Assert.False(settings.UseSmartSizing);
    }

    [Fact]
    public void Smart_sizing_is_a_resolution_in_this_format()
    {
        ConnectionSettings settings = Settings("""Resolution="SmartSize" InheritResolution="false" """);

        Assert.True(settings.UseSmartSizing);
        Assert.Null(settings.DesktopWidth);
    }

    [Fact]
    public void A_resolution_that_is_neither_a_size_nor_a_way_of_scaling_is_noted()
    {
        ImportResult result = OneConnection("""Resolution="FitToWindow" InheritResolution="false" """);

        Assert.Null(Only(result).Settings.DesktopWidth);
        Assert.Contains(result.Warnings, w => w.Contains("full screen", StringComparison.Ordinal));
    }

    [Fact]
    public void A_colour_depth_Patchbay_does_not_offer_is_left_to_inherit()
    {
        ImportResult result = OneConnection("""Colors="Colors256" InheritColors="false" """);

        Assert.Null(Only(result).Settings.ColourDepth);
        Assert.Contains(result.Warnings, w => w.Contains("Colors256", StringComparison.Ordinal));
    }

    [Fact]
    public void A_port_nothing_could_connect_to_is_left_to_inherit()
    {
        ImportResult result = OneConnection("""Port="70000" InheritPort="false" """);

        Assert.Null(Only(result).Settings.Port);
        Assert.Contains(result.Warnings, w => w.Contains("70000", StringComparison.Ordinal));
    }

    // ── What is left out, and said out loud ─────────────────────────────

    [Fact]
    public void A_connection_that_is_not_a_remote_desktop_is_left_out_and_named()
    {
        ImportResult result = MremoteNgImporter.Import(Sample);

        Assert.DoesNotContain(
            result.Root.DescendantServers(),
            s => s.HostName.StartsWith("core-sw-01", StringComparison.Ordinal));

        Assert.Contains(result.Warnings, w => w.Contains("SSH2", StringComparison.Ordinal));
    }

    /// <summary>
    /// A folder that says SSH with connections that inherit is an ordinary
    /// file. Reading each of those as RDP because the connection itself named
    /// no protocol would fill the tree with connections that cannot work.
    /// </summary>
    [Fact]
    public void A_connection_inheriting_a_protocol_that_is_not_RDP_is_left_out()
    {
        ImportResult result = MremoteNgImporter.Import(Sample);

        Assert.DoesNotContain(
            result.Root.DescendantServers(),
            s => s.Name.Contains("lab-jump", StringComparison.Ordinal));
    }

    [Fact]
    public void An_entry_with_no_address_is_left_out_and_counted()
    {
        ImportResult result = MremoteNgImporter.Import("""
            <mrng:Connections xmlns:mrng="http://mremoteng.org" Name="X" ConfVersion="2.6">
              <Node Name="Nowhere" Type="Connection" Protocol="RDP" InheritProtocol="false" />
            </mrng:Connections>
            """);

        Assert.Equal(0, result.ServerCount);
        Assert.Contains(result.Warnings, w => w.Contains("no address", StringComparison.Ordinal));
    }

    [Fact]
    public void Saved_passwords_are_counted_and_the_connections_ask_instead()
    {
        ImportResult result = MremoteNgImporter.Import(Sample);

        Assert.Equal(CredentialMode.Prompt, Server("WEB-PRD-01").Settings.CredentialMode);
        Assert.Contains(
            result.Warnings,
            w => w.Contains("Saved passwords were not imported (2", StringComparison.Ordinal));
    }

    /// <summary>
    /// Counted rather than refused. This file is an inventory somebody asked
    /// to import, not a single connection that arrived by email, so the
    /// redirections they configured come across — and the count is what tells
    /// them the file does it.
    /// </summary>
    [Fact]
    public void Connections_that_hand_this_computer_over_are_imported_and_counted()
    {
        ImportResult result = MremoteNgImporter.Import(Sample);

        Assert.True(Server("WEB-PRD-01").Settings.RedirectDrives);
        Assert.Contains(
            result.Warnings,
            w => w.Contains("offer this computer's drives", StringComparison.Ordinal));
    }

    [Fact]
    public void Connections_set_to_skip_the_identity_check_are_counted()
    {
        ImportResult result = OneConnection(
            """RDPAuthenticationLevel="NoAuth" InheritRDPAuthenticationLevel="false" """);

        Assert.Equal(ServerAuthentication.Connect, Only(result).Settings.ServerAuthentication);
        Assert.Contains(
            result.Warnings,
            w => w.Contains("without checking the identity", StringComparison.Ordinal));
    }

    [Fact]
    public void Switching_off_network_level_authentication_is_counted()
    {
        ImportResult result = OneConnection("""UseCredSsp="false" InheritUseCredSsp="false" """);

        Assert.Contains(
            result.Warnings,
            w => w.Contains("network level authentication", StringComparison.Ordinal));
    }

    /// <summary>
    /// mRemoteNG runs these on the computer doing the connecting, not on the
    /// far end, which makes them worth naming rather than counting.
    /// </summary>
    [Fact]
    public void A_tool_a_connection_runs_on_this_computer_is_named()
    {
        ImportResult result = OneConnection("""PreExtApp="Wake on LAN" InheritPreExtApp="false" """);

        Assert.Contains(result.Warnings, w => w.Contains("Wake on LAN", StringComparison.Ordinal));
    }

    // ── The file itself ─────────────────────────────────────────────────

    [Fact]
    public void A_file_that_is_not_a_confCons_is_refused()
    {
        ImportException ex = Assert.Throws<ImportException>(
            () => MremoteNgImporter.Import("""<RDCMan schemaVersion="3"><file /></RDCMan>"""));

        Assert.Contains("not an mRemoteNG file", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_configuration_older_than_anything_readable_is_refused()
    {
        Assert.Throws<ImportException>(() => MremoteNgImporter.Import("""
            <mrng:Connections xmlns:mrng="http://mremoteng.org" Name="X" ConfVersion="0.4" />
            """));
    }

    [Fact]
    public void A_newer_configuration_is_read_and_says_so()
    {
        ImportResult result = MremoteNgImporter.Import("""
            <mrng:Connections xmlns:mrng="http://mremoteng.org" Name="X" ConfVersion="9.9" />
            """);

        Assert.Contains(result.Warnings, w => w.Contains("newer mRemoteNG", StringComparison.Ordinal));
    }

    /// <summary>
    /// With full file encryption there is no XML to fail on, so the message
    /// has to come from somewhere else. "This file is not valid XML" sends
    /// somebody looking for a corrupt file when what they have is a working
    /// one they need a password for.
    /// </summary>
    [Fact]
    public void A_fully_encrypted_file_says_so_rather_than_looking_corrupt()
    {
        using MemoryStream stream = new(Encoding.UTF8.GetBytes(
            "SGVsbG8sIHRoaXMgaXMgbm90IFhNTCBhdCBhbGwsIGl0IGlzIGEgYmxvYg=="));

        ImportException ex = Assert.Throws<ImportException>(() => MremoteNgImporter.Import(stream));

        Assert.Contains("whole file is encrypted", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_connections_of_the_same_name_do_not_collide()
    {
        ImportResult result = MremoteNgImporter.Import("""
            <mrng:Connections xmlns:mrng="http://mremoteng.org" Name="X" ConfVersion="2.6">
              <Node Name="WEB" Type="Connection" Hostname="web-01" Protocol="RDP" InheritProtocol="false" />
              <Node Name="WEB" Type="Connection" Hostname="web-02" Protocol="RDP" InheritProtocol="false" />
            </mrng:Connections>
            """);

        Assert.Equal(["WEB", "WEB (2)"], result.Root.ChildServers.Select(s => s.Name));
    }

    // ── Choosing which reader gets the file ─────────────────────────────

    [Fact]
    public void An_xml_file_goes_to_the_mRemoteNG_reader()
    {
        ImportResult result = ConnectionImport.From(Save("confCons.xml", Sample));

        Assert.Equal(2, result.ServerCount);
    }

    /// <summary>
    /// <c>.xml</c> names no format, so the router hands it over and the reader
    /// refuses what is not its own. That is a reader checking its input rather
    /// than a router guessing from the contents.
    /// </summary>
    [Fact]
    public void An_xml_file_that_is_not_a_confCons_is_refused_by_the_reader()
    {
        ImportException ex = Assert.Throws<ImportException>(
            () => ConnectionImport.From(Save("servers.xml", """<RDCMan schemaVersion="3" />""")));

        Assert.Contains("not an mRemoteNG file", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void All_three_formats_can_be_imported_together()
    {
        ImportResult result = ConnectionImport.From(
        [
            Save("confCons.xml", Sample),
            Save("WEB-01.rdp", "full address:s:web-01\n"),
            Save("Corp.rdg", """
                <RDCMan programVersion="2.83" schemaVersion="3">
                  <file>
                    <properties><name>RDCMan servers</name></properties>
                    <server><properties><name>old-01.corp.local</name></properties></server>
                  </file>
                </RDCMan>
                """),
        ]);

        GroupNode group = Assert.IsType<GroupNode>(result.Node);

        Assert.Equal(4, result.ServerCount);
        Assert.Equal(["Corp servers", "RDCMan servers"], group.ChildGroups.Select(g => g.Name));
        Assert.Equal(["WEB-01"], group.ChildServers.Select(s => s.Name));
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private static GroupNode Group(string name) =>
        MremoteNgImporter.Import(Sample).Root.Descendants().OfType<GroupNode>()
            .Single(g => g.Name == name);

    private static ServerNode Server(string name) =>
        MremoteNgImporter.Import(Sample).Root.DescendantServers().Single(s => s.Name == name);

    private static ServerNode Only(ImportResult result) =>
        Assert.IsType<ServerNode>(Assert.Single(result.Root.Children));

    /// <summary>A file holding one connection with whatever the test is about.</summary>
    private static ImportResult OneConnection(string attributes) => MremoteNgImporter.Import($"""
        <mrng:Connections xmlns:mrng="http://mremoteng.org" Name="X" ConfVersion="2.6">
          <Node Name="WEB-01" Type="Connection" Hostname="web-01" Protocol="RDP"
                InheritProtocol="false" {attributes} />
        </mrng:Connections>
        """);

    private static ConnectionSettings Settings(string attributes) =>
        Only(OneConnection(attributes)).Settings;

    private string Save(string name, string text)
    {
        string path = Path.Combine(_folder, name);

        File.WriteAllText(path, text);

        return path;
    }
}
