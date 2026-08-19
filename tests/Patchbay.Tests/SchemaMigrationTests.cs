using System.Text.Json.Nodes;
using Patchbay.Core.Model;
using Patchbay.Core.Serialization;

namespace Patchbay.Tests;

public class SchemaMigrationTests
{
    /// <summary>
    /// Stand-in for a real migration. Renames a property, which is the most
    /// common shape a migration takes, and records that it ran.
    /// </summary>
    private sealed class RenameHostMigration(int fromVersion) : ISchemaMigration
    {
        public int FromVersion { get; } = fromVersion;

        public string Description => "Rename 'address' to 'hostName'";

        public bool WasApplied { get; private set; }

        public JsonObject Apply(JsonObject document)
        {
            WasApplied = true;
            Rename(document);
            return document;

            static void Rename(JsonNode? node)
            {
                switch (node)
                {
                    case JsonObject obj:
                        if (obj.TryGetPropertyValue("address", out JsonNode? address))
                        {
                            obj.Remove("address");
                            obj["hostName"] = address?.DeepClone();
                        }

                        foreach (KeyValuePair<string, JsonNode?> property in obj.ToList())
                        {
                            Rename(property.Value);
                        }

                        break;

                    case JsonArray array:
                        foreach (JsonNode? item in array)
                        {
                            Rename(item);
                        }

                        break;
                }
            }
        }
    }

    private const string VersionZeroDocument = """
        {
          "schemaVersion": 0,
          "root": {
            "name": "Connections",
            "children": [
              { "$kind": "server", "name": "DC-01", "address": "10.10.1.5" }
            ]
          }
        }
        """;

    [Fact]
    public void No_migrations_ship_at_the_current_schema_version()
    {
        // Schema 1 is the first, so there is nothing to upgrade from yet. This
        // will change; the point is that the chain below is already tested.
        Assert.Empty(SchemaMigrator.Registered);
        Assert.Equal(1, ConnectionDocument.CurrentSchemaVersion);
    }

    [Fact]
    public void Version_is_read_without_deserialising()
    {
        Assert.Equal(0, SchemaMigrator.ReadVersion(VersionZeroDocument));
        Assert.Equal(1, SchemaMigrator.ReadVersion("""{ "schemaVersion": 1 }"""));
    }

    /// <summary>A file predating versioning is assumed to be version 1, not rejected.</summary>
    [Fact]
    public void A_document_with_no_version_is_treated_as_version_one()
    {
        Assert.Equal(1, SchemaMigrator.ReadVersion("""{ "root": { "name": "Connections" } }"""));
    }

    [Fact]
    public void A_current_document_passes_through_untouched()
    {
        string json = ConnectionDocumentSerializer.Serialize(new ConnectionDocument());

        (string result, int? migratedFrom) = SchemaMigrator.Migrate(json);

        Assert.Null(migratedFrom);
        Assert.Same(json, result);
    }

    [Fact]
    public void A_migration_runs_and_the_version_is_stamped_forward()
    {
        RenameHostMigration migration = new(fromVersion: 0);

        (string result, int? migratedFrom) = SchemaMigrator.Migrate(VersionZeroDocument, [migration]);

        Assert.True(migration.WasApplied);
        Assert.Equal(0, migratedFrom);
        Assert.Equal(1, SchemaMigrator.ReadVersion(result));
        Assert.Contains("hostName", result, StringComparison.Ordinal);
        Assert.DoesNotContain("address", result, StringComparison.Ordinal);
    }

    [Fact]
    public void A_migrated_document_deserialises_into_the_current_model()
    {
        (ConnectionDocument document, int? migratedFrom) =
            ConnectionDocumentSerializer.DeserializeWithMigrationInfo(
                VersionZeroDocument,
                [new RenameHostMigration(fromVersion: 0)]);

        Assert.Equal(0, migratedFrom);
        Assert.Equal(1, document.SchemaVersion);

        ServerNode server = document.AllServers.Single();
        Assert.Equal("DC-01", server.Name);
        Assert.Equal("10.10.1.5", server.HostName);

        // Parent links must still be rebuilt after a migrated load.
        Assert.Same(document.Root, server.Parent);
    }

    /// <summary>
    /// A missing step must stop the load. Skipping it would deserialise an old
    /// shape into the new model, drop everything that did not line up, and then
    /// write that loss back to disk on the next save.
    /// </summary>
    [Fact]
    public void A_gap_in_the_chain_is_refused()
    {
        const string VersionMinusTwo = """{ "schemaVersion": -1, "root": { "name": "Connections" } }""";

        ConnectionDocumentException ex = Assert.Throws<ConnectionDocumentException>(
            () => SchemaMigrator.Migrate(VersionMinusTwo, [new RenameHostMigration(fromVersion: 0)]));

        Assert.Contains("-1 to 0", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_newer_document_than_this_build_understands_is_refused()
    {
        string json = $$"""{ "schemaVersion": {{ConnectionDocument.CurrentSchemaVersion + 1}} }""";

        ConnectionDocumentException ex = Assert.Throws<ConnectionDocumentException>(
            () => SchemaMigrator.Migrate(json));

        Assert.Contains("Update Patchbay", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_non_object_document_is_refused()
    {
        Assert.Throws<ConnectionDocumentException>(() => SchemaMigrator.Migrate("[1, 2, 3]"));
    }

    [Fact]
    public void Malformed_json_is_refused_with_the_parse_error_attached()
    {
        ConnectionDocumentException ex = Assert.Throws<ConnectionDocumentException>(
            () => SchemaMigrator.ReadVersion("{ nope"));

        Assert.IsAssignableFrom<System.Text.Json.JsonException>(ex.InnerException);
    }
}
