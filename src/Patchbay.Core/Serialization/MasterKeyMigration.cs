using System.Text.Json.Nodes;

namespace Patchbay.Core.Serialization;

/// <summary>
/// Schema 1 to 2: a document may carry a wrapped master key (M3-07).
///
/// <para>
/// <b>It changes nothing, and that is the point.</b> A version 1 document has
/// no master key, which is exactly what a version 2 document with no master
/// key means, so there is nothing to rewrite. What the bump does is stop a
/// build that has never heard of <c>masterKey</c> from opening a file that has
/// one.
/// </para>
///
/// <para>
/// That matters more here than for any field added so far. An unrecognised
/// property is dropped on deserialisation and gone on the next save. For a
/// setting, that loses a setting. For this, it loses the only copy of the key
/// wrapping every password in the document, and no backup of the passwords
/// helps because they are encrypted with the key that was just discarded.
/// <c>SchemaMigrator</c> already refuses to open anything newer than the build
/// understands, with the reason spelled out — "opening it now would discard
/// settings on the next save" — and this is the version that makes the refusal
/// happen at the right moment.
/// </para>
///
/// <para>
/// It is also the first migration the chain has actually carried. The
/// machinery was built at <c>M1-08</c> and tested empty, so that the first
/// real one would be a class to add rather than a mechanism to design under
/// pressure.
/// </para>
/// </summary>
public sealed class MasterKeyMigration : ISchemaMigration
{
    /// <inheritdoc />
    public int FromVersion => 1;

    /// <inheritdoc />
    public string Description => "Allow a document to carry a master key (M3-07)";

    /// <inheritdoc />
    public JsonObject Apply(JsonObject document)
    {
        ArgumentNullException.ThrowIfNull(document);

        // Deliberately untouched. A document with no master key is a valid
        // version 2 document; writing an explicit null would only add a line
        // to every file that has never had one.
        return document;
    }
}
