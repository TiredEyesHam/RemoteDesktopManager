using System.Text.Json;
using System.Text.Json.Nodes;
using Patchbay.Core.Model;

namespace Patchbay.Core.Serialization;

/// <summary>
/// Runs a document forward through the migration chain until it reaches the
/// version this build understands.
/// </summary>
public static class SchemaMigrator
{
    /// <summary>
    /// The migrations shipped with this build, in order. Empty at schema
    /// version 1 — there is nothing older to upgrade from yet. The chain is
    /// exercised by tests regardless, so the first real migration is a matter
    /// of adding one class rather than building the machinery under pressure.
    /// </summary>
    public static IReadOnlyList<ISchemaMigration> Registered { get; } = [];

    /// <summary>Reads the schema version from raw JSON without deserialising it.</summary>
    /// <exception cref="ConnectionDocumentException">The JSON is not an object, or is malformed.</exception>
    public static int ReadVersion(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        JsonObject root = Parse(json);

        // A document with no version predates versioning; treat it as 1 rather
        // than refusing to open it.
        if (!root.TryGetPropertyValue("schemaVersion", out JsonNode? node) || node is null)
        {
            return 1;
        }

        return node.GetValue<int>();
    }

    /// <summary>
    /// Applies every migration needed to bring <paramref name="json"/> up to
    /// <see cref="ConnectionDocument.CurrentSchemaVersion"/>.
    /// </summary>
    /// <returns>
    /// The upgraded JSON, and the version it started at — null when it was
    /// already current and nothing ran.
    /// </returns>
    /// <exception cref="ConnectionDocumentException">
    /// The document is newer than this build, or the chain has a gap.
    /// </exception>
    public static (string Json, int? MigratedFromVersion) Migrate(
        string json,
        IReadOnlyList<ISchemaMigration>? migrations = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        migrations ??= Registered;

        int startingVersion = ReadVersion(json);
        int target = ConnectionDocument.CurrentSchemaVersion;

        if (startingVersion > target)
        {
            throw new ConnectionDocumentException(
                $"This document uses schema version {startingVersion}, but this build of Patchbay "
                + $"understands version {target}. Update Patchbay to open it — opening it now would "
                + "discard settings on the next save.");
        }

        if (startingVersion == target)
        {
            return (json, null);
        }

        JsonObject document = Parse(json);

        for (int version = startingVersion; version < target; version++)
        {
            ISchemaMigration? step = migrations.FirstOrDefault(m => m.FromVersion == version);

            if (step is null)
            {
                throw new ConnectionDocumentException(
                    $"Cannot upgrade this document from schema version {version} to {version + 1}: "
                    + "that step is missing from this build. This usually means the file came from a "
                    + "different branch of Patchbay.");
            }

            document = step.Apply(document)
                ?? throw new ConnectionDocumentException(
                    $"The migration from schema version {version} to {version + 1} returned nothing.");

            document["schemaVersion"] = version + 1;
        }

        return (document.ToJsonString(ConnectionDocumentSerializer.Options), startingVersion);
    }

    private static JsonObject Parse(string json)
    {
        try
        {
            JsonNode? node = JsonNode.Parse(
                json,
                nodeOptions: null,
                documentOptions: new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                });

            return node as JsonObject
                ?? throw new ConnectionDocumentException(
                    "The connection document must be a JSON object.");
        }
        catch (JsonException ex)
        {
            throw new ConnectionDocumentException(
                $"The connection document could not be read: {ex.Message}", ex);
        }
    }
}
