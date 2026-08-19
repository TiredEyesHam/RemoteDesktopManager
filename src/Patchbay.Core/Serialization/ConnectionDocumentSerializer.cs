using System.Text.Json;
using System.Text.Json.Serialization;
using Patchbay.Core.Model;

namespace Patchbay.Core.Serialization;

/// <summary>
/// Turns a <see cref="ConnectionDocument"/> into JSON and back.
///
/// The format is meant to be readable and diffable — people keep these in git,
/// and being able to review a hostname change in a pull request is worth more
/// than a few saved bytes. Hence indentation, string enums, and omitted nulls.
///
/// Omitting nulls is not only cosmetic: a null setting means "inherit", so a
/// document that wrote them all out would be mostly noise, and the absence of
/// a key carries the same meaning as its null.
/// </summary>
public static class ConnectionDocumentSerializer
{
    /// <summary>
    /// Shared, immutable once first used. Exposed so importers and tests use
    /// exactly the same configuration rather than an approximation of it.
    /// </summary>
    public static JsonSerializerOptions Options { get; } = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
        // Trailing commas and comments are tolerated on read: these files get
        // hand-edited, and refusing to load over a stray comma helps nobody.
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static string Serialize(ConnectionDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return JsonSerializer.Serialize(document, Options);
    }

    /// <summary>
    /// Migrates if needed, deserialises, and restores parent links.
    /// </summary>
    /// <exception cref="ConnectionDocumentException">
    /// The JSON is malformed, empty, or written by a newer version of Patchbay.
    /// </exception>
    public static ConnectionDocument Deserialize(
        string json,
        IReadOnlyList<ISchemaMigration>? migrations = null) =>
        DeserializeWithMigrationInfo(json, migrations).Document;

    /// <summary>
    /// As <see cref="Deserialize(string, IReadOnlyList{ISchemaMigration})"/>, but
    /// also reports the version the document started at when a migration ran.
    /// The store needs that to tell the person their file was upgraded.
    /// </summary>
    public static (ConnectionDocument Document, int? MigratedFromVersion) DeserializeWithMigrationInfo(
        string json,
        IReadOnlyList<ISchemaMigration>? migrations = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        // Runs before deserialisation, and owns the too-new check: an old
        // document cannot be read into the current classes at all, which is
        // precisely why it needs upgrading first.
        (string migrated, int? migratedFrom) = SchemaMigrator.Migrate(json, migrations);

        ConnectionDocument? document;

        try
        {
            document = JsonSerializer.Deserialize<ConnectionDocument>(migrated, Options);
        }
        catch (JsonException ex)
        {
            throw new ConnectionDocumentException(
                $"The connection document could not be read: {ex.Message}", ex);
        }

        if (document is null)
        {
            throw new ConnectionDocumentException("The connection document was empty.");
        }

        // Without this, every node's ancestry is empty and inheritance silently
        // resolves everything to defaults.
        document.RebuildParentLinks();

        return (document, migratedFrom);
    }
}
