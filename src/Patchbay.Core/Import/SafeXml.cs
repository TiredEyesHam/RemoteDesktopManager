using System.Xml;
using System.Xml.Linq;

namespace Patchbay.Core.Import;

/// <summary>
/// Reads XML from somewhere else without trusting it.
///
/// This is the reason the importer exists as its own file with its own tests.
/// Microsoft pulled RDCMan from download in 2020 over CVE-2020-0765: an XML
/// external entity flaw in exactly this parser, in exactly this file format.
/// A malicious <c>.rdg</c> — the sort of thing that gets emailed round a team
/// as "here are the servers" — could read files off the machine that opened
/// it and post them to a server of the attacker's choosing.
///
/// Every reader in Patchbay that touches a file someone else produced goes
/// through here. The settings below are not defence in depth for its own sake;
/// each one closes a different route:
///
/// <list type="bullet">
/// <item><c>DtdProcessing.Prohibit</c> — a document type declaration is
/// refused outright. Entities are declared in the DTD, so no DTD means no
/// entity expansion, which stops both external entities and the billion
/// laughs amplification attack at the door.</item>
/// <item><c>XmlResolver = null</c> — nothing is fetched from disk or the
/// network even if a reference somehow survives. This is what turns a missed
/// entity into a parse failure rather than an HTTP request.</item>
/// <item><c>MaxCharactersFromEntities = 0</c> — belt and braces on
/// expansion.</item>
/// <item><c>MaxCharactersInDocument</c> — a bound on the whole document, so a
/// malformed or hostile file cannot exhaust memory before it fails.</item>
/// </list>
///
/// Depth is bounded separately, by the code that walks the tree: nesting is
/// legal XML, and a few thousand levels of it turns a recursive parser into a
/// stack overflow, which is a crash the process cannot catch.
/// </summary>
public static class SafeXml
{
    /// <summary>
    /// Largest document accepted, in characters. A real connection file with
    /// several thousand hosts is under a megabyte; this is generous enough
    /// that no honest file hits it.
    /// </summary>
    public const long MaxCharacters = 64L * 1024 * 1024;

    /// <summary>
    /// How deep an element tree may nest before the file is refused. RDCMan
    /// files nest by group, and nobody has sixty-four levels of groups.
    /// </summary>
    public const int MaxDepth = 64;

    /// <summary>Loads a document with every unsafe feature switched off.</summary>
    /// <exception cref="ImportException">The stream is not usable XML.</exception>
    public static XDocument Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        XmlReaderSettings settings = new()
        {
            // No DTD, so no entities, so no XXE and no billion laughs.
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersFromEntities = 0,
            MaxCharactersInDocument = MaxCharacters,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            IgnoreWhitespace = true,
            CloseInput = false,
        };

        try
        {
            using XmlReader reader = XmlReader.Create(stream, settings);

            // LoadOptions.None: no base URI, no line info retained, and
            // nothing that would give the document a notion of where it came
            // from and therefore what it might resolve against.
            return XDocument.Load(reader, LoadOptions.None);
        }
        catch (XmlException ex)
        {
            throw new ImportException(
                $"This file is not valid XML, so it cannot be read: {ex.Message}", ex);
        }
        catch (InvalidOperationException ex)
        {
            throw new ImportException("This file could not be read as XML.", ex);
        }
    }

    /// <summary>
    /// Refuses a tree that nests further than <see cref="MaxDepth"/>. Called
    /// before anything walks it recursively.
    /// </summary>
    /// <exception cref="ImportException">The document nests too deeply.</exception>
    public static void GuardDepth(XElement root)
    {
        ArgumentNullException.ThrowIfNull(root);

        // Iterative on purpose: a recursive depth check would hit the same
        // stack overflow it is meant to prevent.
        Stack<(XElement Element, int Depth)> pending = new();
        pending.Push((root, 1));

        while (pending.Count > 0)
        {
            (XElement element, int depth) = pending.Pop();

            if (depth > MaxDepth)
            {
                throw new ImportException(
                    $"This file nests more than {MaxDepth} levels deep, which no real "
                    + "connection file does. It has not been imported.");
            }

            foreach (XElement child in element.Elements())
            {
                pending.Push((child, depth + 1));
            }
        }
    }
}
