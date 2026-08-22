using System.Text.Json.Nodes;

namespace Patchbay.Core.Serialization;

/// <summary>
/// Schema 2 to 3: a document has an identity, and may say where it keeps its
/// saved passwords (M3-04).
///
/// <para>
/// Like <see cref="MasterKeyMigration"/> it rewrites nothing, and for a
/// narrower reason than that one. A version 2 document has no identity because
/// nothing needed one, and minting it here rather than in the migration is
/// deliberate: <c>ConnectionDocument.Id</c> generates one on load, so a
/// document arrives with an identity whether it came through this step or not,
/// and there is exactly one piece of code deciding what an unidentified
/// document gets.
/// </para>
///
/// <para>
/// <b>What the bump is actually for.</b> Not the store preference — dropping
/// that loses a preference and nothing else. The identity. Windows Credential
/// Manager holds the password and the document holds a name for it, so an
/// entry is filed under the document that owns it. A build that had never
/// heard of the id would write the file back without one, the next load would
/// mint a fresh one, and every password that document keeps in Windows would
/// be filed under an id nothing refers to: still there, no longer reachable,
/// and invisible to the sweep whose whole job is clearing those up. That is
/// the same class of loss <c>M3-07</c> bumped for, arriving by a different
/// route.
/// </para>
/// </summary>
public sealed class SecretStoreMigration : ISchemaMigration
{
    /// <inheritdoc />
    public int FromVersion => 2;

    /// <inheritdoc />
    public string Description => "Give the document an identity and a choice of password store (M3-04)";

    /// <inheritdoc />
    public JsonObject Apply(JsonObject document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return document;
    }
}
