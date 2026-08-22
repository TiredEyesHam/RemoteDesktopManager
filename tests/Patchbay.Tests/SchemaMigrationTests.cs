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
    public void The_registered_chain_reaches_the_current_version_without_a_gap()
    {
        // Migrate refuses a gap rather than stepping over it, which is the
        // right refusal and a miserable one to find out about from somebody
        // whose document will not open. One step per version, no more.
        for (int version = 1; version < ConnectionDocument.CurrentSchemaVersion; version++)
        {
            Assert.Single(SchemaMigrator.Registered, step => step.FromVersion == version);
        }

        // And nothing stray: a step from a version that does not exist would
        // never run, which looks like coverage and is not.
        Assert.DoesNotContain(
            SchemaMigrator.Registered,
            step => step.FromVersion < 1
                || step.FromVersion >= ConnectionDocument.CurrentSchemaVersion);
    }

    [Fact]
    public void A_document_written_before_master_keys_existed_upgrades_and_has_none()
    {
        // The first real migration, and it rewrites nothing: a version 1
        // document has no master key, which is exactly what a version 2
        // document with no master key means. What the bump buys is that an
        // older build refuses to open a file that does have one, rather than
        // dropping the field and taking every password in it down with it.
        const string Version1 = """{"schemaVersion":1,"root":{"name":"Connections","children":[]}}""";

        (ConnectionDocument document, int? migratedFrom) =
            ConnectionDocumentSerializer.DeserializeWithMigrationInfo(Version1);

        Assert.Equal(1, migratedFrom);
        Assert.Equal(ConnectionDocument.CurrentSchemaVersion, document.SchemaVersion);
        Assert.Null(document.MasterKey);
    }

    [Fact]
    public void A_document_written_before_it_had_an_identity_is_given_one()
    {
        // The whole of what schema 3 is for (M3-04). Windows Credential
        // Manager files an entry under the document that owns it, so a
        // document with no identity can own nothing — which is exactly true of
        // a version 2 document, and stops being true the moment this build
        // saves it.
        const string Version2 = """{"schemaVersion":2,"root":{"name":"Connections","children":[]}}""";

        (ConnectionDocument document, int? migratedFrom) =
            ConnectionDocumentSerializer.DeserializeWithMigrationInfo(Version2);

        Assert.Equal(2, migratedFrom);
        Assert.Equal(ConnectionDocument.CurrentSchemaVersion, document.SchemaVersion);
        Assert.NotEqual(Guid.Empty, document.Id);
        Assert.Null(document.CredentialStore);
    }

    [Fact]
    public void An_identity_survives_a_round_trip_rather_than_being_minted_again()
    {
        // The failure this guards is silent and total: a document whose id
        // changed on every load would abandon every password it keeps in
        // Windows, and nothing about the file would look wrong.
        ConnectionDocument document = new();

        ConnectionDocument reopened =
            ConnectionDocumentSerializer.Deserialize(ConnectionDocumentSerializer.Serialize(document));

        Assert.Equal(document.Id, reopened.Id);
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

        (string result, int? migratedFrom) =
            SchemaMigrator.Migrate(VersionZeroDocument, [migration, new MasterKeyMigration(), new SecretStoreMigration()]);

        Assert.True(migration.WasApplied);
        Assert.Equal(0, migratedFrom);
        Assert.Equal(ConnectionDocument.CurrentSchemaVersion, SchemaMigrator.ReadVersion(result));
        Assert.Contains("hostName", result, StringComparison.Ordinal);
        Assert.DoesNotContain("address", result, StringComparison.Ordinal);
    }

    [Fact]
    public void A_migrated_document_deserialises_into_the_current_model()
    {
        (ConnectionDocument document, int? migratedFrom) =
            ConnectionDocumentSerializer.DeserializeWithMigrationInfo(
                VersionZeroDocument,
                [new RenameHostMigration(fromVersion: 0), new MasterKeyMigration(), new SecretStoreMigration()]);

        Assert.Equal(0, migratedFrom);
        Assert.Equal(ConnectionDocument.CurrentSchemaVersion, document.SchemaVersion);

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
