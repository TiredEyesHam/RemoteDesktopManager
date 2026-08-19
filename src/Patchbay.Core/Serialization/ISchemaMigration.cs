using System.Text.Json.Nodes;

namespace Patchbay.Core.Serialization;

/// <summary>
/// Upgrades a document one schema version forward.
///
/// Migrations work on the JSON tree, not on the model. That is deliberate: an
/// old document cannot be deserialised into the current classes — that is the
/// entire reason it needs migrating — so anything typed would be describing
/// the shape it is trying to leave behind.
/// </summary>
public interface ISchemaMigration
{
    /// <summary>The version this migration reads. It always produces
    /// <c>FromVersion + 1</c>, which keeps the chain gap-free by construction.</summary>
    int FromVersion { get; }

    /// <summary>
    /// Short description of what changes, used in logs so an upgrade that goes
    /// wrong can be traced to a specific step.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Rewrites the document in place, or returns a new one. Must not assume
    /// any property is present — it is operating on a file someone may have
    /// hand-edited.
    /// </summary>
    JsonObject Apply(JsonObject document);
}
