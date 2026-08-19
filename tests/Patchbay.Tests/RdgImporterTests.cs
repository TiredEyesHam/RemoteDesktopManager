using Patchbay.Core.Import;
using Patchbay.Core.Inheritance;
using Patchbay.Core.Model;

namespace Patchbay.Tests;

public class RdgImporterTests
{
    /// <summary>
    /// A .rdg in the shape RDCMan 2.83 writes: settings blocks that either
    /// carry values or defer to the parent, a display name that differs from
    /// the address, and credentials that must not come across.
    /// </summary>
    private const string Sample = """
        <?xml version="1.0" encoding="utf-8"?>
        <RDCMan programVersion="2.83" schemaVersion="3">
          <file>
            <credentialsProfiles>
              <credentialsProfile inherit="None">
                <profileName scope="File">corp-admin</profileName>
                <userName>svc_rdp</userName>
                <domain>CORP</domain>
                <password>AQAAANCMnd8BFdERjHoAwE/Cl+sBAAAA</password>
              </credentialsProfile>
            </credentialsProfiles>
            <properties>
              <expanded>True</expanded>
              <name>Corp servers</name>
            </properties>
            <logonCredentials inherit="FromParent" />
            <connectionSettings inherit="FromParent" />
            <gatewaySettings inherit="FromParent" />
            <remoteDesktop inherit="FromParent" />
            <localResources inherit="FromParent" />
            <group>
              <properties>
                <expanded>True</expanded>
                <name>Production</name>
                <comment>Change freeze at the weekend.</comment>
              </properties>
              <logonCredentials inherit="None">
                <profileName scope="Local">Custom</profileName>
                <userName>rdpadmin</userName>
                <domain>CORP</domain>
                <password>AQAAANCMnd8BFdERjHoAwE/Cl+sBAAAA</password>
              </logonCredentials>
              <connectionSettings inherit="None">
                <connectToConsole>False</connectToConsole>
                <startProgram />
                <workingDir />
                <port>3389</port>
              </connectionSettings>
              <gatewaySettings inherit="None">
                <enabled>True</enabled>
                <hostName>rdg.corp.local</hostName>
                <logonMethod>NTLM</logonMethod>
                <localBypass>True</localBypass>
              </gatewaySettings>
              <remoteDesktop inherit="None">
                <sameSizeAsClientArea>False</sameSizeAsClientArea>
                <desktopWidth>1920</desktopWidth>
                <desktopHeight>1080</desktopHeight>
                <colorDepth>24</colorDepth>
              </remoteDesktop>
              <localResources inherit="None">
                <audioRedirection>2</audioRedirection>
                <redirectClipboard>False</redirectClipboard>
                <redirectDrives>False</redirectDrives>
                <redirectPrinters>False</redirectPrinters>
                <redirectSmartCards>True</redirectSmartCards>
              </localResources>
              <server>
                <properties>
                  <name>10.20.4.11</name>
                  <displayName>WEB-PRD-01</displayName>
                  <comment>Front end.</comment>
                </properties>
                <logonCredentials inherit="FromParent" />
                <connectionSettings inherit="FromParent" />
                <gatewaySettings inherit="FromParent" />
                <remoteDesktop inherit="FromParent" />
                <localResources inherit="FromParent" />
              </server>
              <server>
                <properties>
                  <name>sql-prd-01.corp.local</name>
                </properties>
                <connectionSettings inherit="None">
                  <port>3390</port>
                  <connectToConsole>True</connectToConsole>
                </connectionSettings>
                <remoteDesktop inherit="None">
                  <sameSizeAsClientArea>True</sameSizeAsClientArea>
                  <colorDepth>32</colorDepth>
                </remoteDesktop>
              </server>
            </group>
            <server>
              <properties>
                <name>bench-01</name>
                <displayName>Bench</displayName>
              </properties>
            </server>
          </file>
        </RDCMan>
        """;

    private static ImportResult ImportSample() => RdgImporter.Import(Sample);

    [Fact]
    public void The_file_name_becomes_the_imported_group_name() =>
        Assert.Equal("Corp servers", ImportSample().Root.Name);

    [Fact]
    public void Groups_and_servers_are_counted()
    {
        ImportResult result = ImportSample();

        Assert.Equal(1, result.GroupCount);
        Assert.Equal(3, result.ServerCount);
        Assert.Equal(3, result.Root.DescendantServers().Count());
    }

    /// <summary>
    /// In a .rdg, 'name' is the address and 'displayName' is what appears in
    /// the tree. Getting these the wrong way round produces a tree that looks
    /// entirely correct and connects to nothing.
    /// </summary>
    [Fact]
    public void The_address_comes_from_name_and_the_label_from_displayName()
    {
        ServerNode web = ImportSample().Root.DescendantServers().First(s => s.HostName == "10.20.4.11");

        Assert.Equal("WEB-PRD-01", web.Name);
        Assert.Equal("Front end.", web.Notes);
    }

    [Fact]
    public void A_server_with_no_display_name_is_labelled_with_its_address()
    {
        ServerNode sql = ImportSample().Root
            .DescendantServers()
            .First(s => s.HostName == "sql-prd-01.corp.local");

        Assert.Equal("sql-prd-01.corp.local", sql.Name);
    }

    [Fact]
    public void Group_comments_become_notes()
    {
        GroupNode production = ImportSample().Root.ChildGroups.Single();

        Assert.Equal("Production", production.Name);
        Assert.Equal("Change freeze at the weekend.", production.Notes);
    }

    [Fact]
    public void A_group_that_owns_its_settings_keeps_them()
    {
        GroupNode production = ImportSample().Root.ChildGroups.Single();
        ConnectionSettings settings = production.Settings;

        Assert.Equal(3389, settings.Port);
        Assert.False(settings.ConnectToConsole);
        Assert.Equal("rdg.corp.local", settings.GatewayHostName);
        Assert.Equal(1920, settings.DesktopWidth);
        Assert.Equal(1080, settings.DesktopHeight);
        Assert.False(settings.UseSmartSizing);
        Assert.Equal(ColourDepth.TrueColour24, settings.ColourDepth);
        Assert.Equal(AudioMode.DoNotPlay, settings.AudioMode);
        Assert.False(settings.RedirectClipboard);
    }

    /// <summary>
    /// The mapping that makes the whole importer work. RDCMan says
    /// inherit="FromParent"; Patchbay says null. Both mean the same thing, so
    /// a block marked FromParent must leave every property unset rather than
    /// being resolved and copied down.
    /// </summary>
    [Fact]
    public void A_block_marked_FromParent_is_left_to_inherit()
    {
        ServerNode web = ImportSample().Root.DescendantServers().First(s => s.HostName == "10.20.4.11");

        Assert.Null(web.Settings.Port);
        Assert.Null(web.Settings.GatewayHostName);
        Assert.Null(web.Settings.ColourDepth);
        Assert.Null(web.Settings.UserName);
    }

    /// <summary>
    /// The proof that the mapping survives contact with the resolver: the
    /// imported server inherits the group's gateway without the importer ever
    /// having written one onto it.
    /// </summary>
    [Fact]
    public void Inheritance_still_resolves_after_an_import()
    {
        ImportResult result = ImportSample();
        ServerNode web = result.Root.DescendantServers().First(s => s.HostName == "10.20.4.11");

        EffectiveSettings effective = SettingsResolver.Resolve(web);

        Assert.Equal("rdg.corp.local", effective.Values.GatewayHostName);
        Assert.Equal("Production", effective.DescribeOrigin(nameof(ConnectionSettings.GatewayHostName)));
        Assert.Equal(ColourDepth.TrueColour24, effective.Values.ColourDepth);
        Assert.Equal(SettingOrigin.Inherited, effective.OriginOf(nameof(ConnectionSettings.Port)));
    }

    [Fact]
    public void A_server_can_override_its_group()
    {
        ServerNode sql = ImportSample().Root
            .DescendantServers()
            .First(s => s.HostName == "sql-prd-01.corp.local");

        Assert.Equal(3390, sql.Settings.Port);
        Assert.True(sql.Settings.ConnectToConsole);
        Assert.Equal(ColourDepth.TrueColour32, sql.Settings.ColourDepth);
        Assert.True(sql.Settings.UseSmartSizing);
    }

    /// <summary>
    /// A gateway that is bypassed for local addresses is the "try direct
    /// first" arrangement, not the "always tunnel" one.
    /// </summary>
    [Fact]
    public void A_gateway_bypassed_locally_maps_to_connecting_directly_first()
    {
        GroupNode production = ImportSample().Root.ChildGroups.Single();

        Assert.Equal(GatewayUsage.WhenDirectFails, production.Settings.GatewayUsage);
    }

    [Fact]
    public void A_gateway_with_no_local_bypass_is_always_used()
    {
        const string Xml = """
            <RDCMan schemaVersion="3"><file><properties><name>f</name></properties>
              <server>
                <properties><name>h</name></properties>
                <gatewaySettings inherit="None">
                  <enabled>True</enabled><hostName>rdg</hostName><localBypass>False</localBypass>
                </gatewaySettings>
              </server>
            </file></RDCMan>
            """;

        ServerNode server = RdgImporter.Import(Xml).Root.DescendantServers().Single();

        Assert.Equal(GatewayUsage.Always, server.Settings.GatewayUsage);
    }

    [Fact]
    public void User_names_and_domains_come_across()
    {
        GroupNode production = ImportSample().Root.ChildGroups.Single();

        Assert.Equal("rdpadmin", production.Settings.UserName);
        Assert.Equal("CORP", production.Settings.Domain);
    }

    /// <summary>
    /// Passwords are DPAPI blobs tied to the account that saved them. They are
    /// counted and reported, never decrypted and never written into a Patchbay
    /// document — which has nowhere safe to put them until M3.
    /// </summary>
    [Fact]
    public void Passwords_are_not_imported_and_the_fact_is_reported()
    {
        ImportResult result = ImportSample();

        Assert.DoesNotContain(
            result.Root.Descendants().Append(result.Root),
            node => node.Settings.CredentialProfileId is not null);

        string warning = Assert.Single(result.Warnings, w => w.Contains("password", StringComparison.OrdinalIgnoreCase));

        Assert.Contains("1 connections", warning, StringComparison.Ordinal);
        Assert.Contains("1 credential profiles", warning, StringComparison.Ordinal);
    }

    /// <summary>
    /// A connection that had a stored password was not meant to prompt, so
    /// after import it has to be the one thing that is honest: a prompt.
    /// </summary>
    [Fact]
    public void A_connection_that_had_a_saved_password_falls_back_to_prompting()
    {
        GroupNode production = ImportSample().Root.ChildGroups.Single();

        Assert.Equal(CredentialMode.Prompt, production.Settings.CredentialMode);
    }

    [Fact]
    public void Settings_Patchbay_does_not_model_are_reported_once()
    {
        ImportResult result = ImportSample();

        string warning = Assert.Single(result.Warnings, w => w.Contains("does not handle", StringComparison.Ordinal));

        Assert.Contains("smart-card", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void A_clean_file_produces_no_warnings()
    {
        const string Xml = """
            <RDCMan schemaVersion="3"><file><properties><name>f</name></properties>
              <server><properties><name>host-a</name></properties></server>
            </file></RDCMan>
            """;

        Assert.Empty(RdgImporter.Import(Xml).Warnings);
    }

    /// <summary>
    /// RDCMan allowed two things in a group to share a display name; Patchbay
    /// does not, and its own editor would refuse to save the result. Renaming
    /// on the way in is better than importing a tree that cannot be edited.
    /// </summary>
    [Fact]
    public void Siblings_that_shared_a_name_are_made_unique()
    {
        const string Xml = """
            <RDCMan schemaVersion="3"><file><properties><name>f</name></properties>
              <server><properties><name>10.0.0.1</name><displayName>WEB</displayName></properties></server>
              <server><properties><name>10.0.0.2</name><displayName>WEB</displayName></properties></server>
            </file></RDCMan>
            """;

        string[] names = [.. RdgImporter.Import(Xml).Root.DescendantServers().Select(s => s.Name)];

        Assert.Equal(["WEB", "WEB (2)"], names);
    }

    [Fact]
    public void An_entry_with_no_address_is_left_out_and_reported()
    {
        const string Xml = """
            <RDCMan schemaVersion="3"><file><properties><name>f</name></properties>
              <server><properties><displayName>Nameless</displayName></properties></server>
              <server><properties><name>host-a</name></properties></server>
            </file></RDCMan>
            """;

        ImportResult result = RdgImporter.Import(Xml);

        Assert.Equal(1, result.ServerCount);
        Assert.Contains(result.Warnings, w => w.Contains("no address", StringComparison.Ordinal));
    }

    [Fact]
    public void A_newer_schema_version_is_read_anyway_with_a_warning()
    {
        string xml = Sample.Replace("schemaVersion=\"3\"", "schemaVersion=\"9\"", StringComparison.Ordinal);

        ImportResult result = RdgImporter.Import(xml);

        Assert.Equal(3, result.ServerCount);
        Assert.Contains(result.Warnings, w => w.Contains("newer RDCMan", StringComparison.Ordinal));
    }

    [Fact]
    public void A_schema_version_older_than_anything_known_is_refused()
    {
        string xml = Sample.Replace("schemaVersion=\"3\"", "schemaVersion=\"0\"", StringComparison.Ordinal);

        Assert.Throws<ImportException>(() => RdgImporter.Import(xml));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not xml at all")]
    [InlineData("<RDCMan schemaVersion=\"3\"><file>")]
    public void Rubbish_input_is_refused_with_something_readable(string xml)
    {
        ImportException ex = Assert.Throws<ImportException>(() => RdgImporter.Import(xml));

        Assert.False(string.IsNullOrWhiteSpace(ex.Message));
    }

    [Fact]
    public void A_file_that_is_not_an_rdg_says_so()
    {
        ImportException ex = Assert.Throws<ImportException>(
            () => RdgImporter.Import("<configuration><appSettings /></configuration>"));

        Assert.Contains("not an RDCMan file", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_rdg_with_no_file_section_says_so()
    {
        ImportException ex = Assert.Throws<ImportException>(
            () => RdgImporter.Import("""<RDCMan schemaVersion="3"><connected /></RDCMan>"""));

        Assert.Contains("no 'file' section", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unrecognised_colour_depth_is_left_to_inherit()
    {
        const string Xml = """
            <RDCMan schemaVersion="3"><file><properties><name>f</name></properties>
              <server>
                <properties><name>h</name></properties>
                <remoteDesktop inherit="None"><colorDepth>8</colorDepth></remoteDesktop>
              </server>
            </file></RDCMan>
            """;

        ImportResult result = RdgImporter.Import(Xml);

        Assert.Null(result.Root.DescendantServers().Single().Settings.ColourDepth);
        Assert.Contains(result.Warnings, w => w.Contains("colour depth of 8", StringComparison.Ordinal));
    }

    [Fact]
    public void The_imported_tree_is_detached_and_ready_to_graft_on()
    {
        ImportResult result = ImportSample();

        Assert.Null(result.Root.Parent);

        // Parent links inside it must already be correct, or nothing in the
        // imported subtree inherits anything.
        Assert.All(result.Root.Descendants(), node => Assert.NotNull(node.Parent));
    }

    [Fact]
    public void Missing_files_are_reported_rather_than_thrown_raw()
    {
        string missing = Path.Combine(Path.GetTempPath(), $"patchbay-missing-{Guid.NewGuid():N}.rdg");

        Assert.Throws<ImportException>(() => RdgImporter.ImportFile(missing));
    }
}
