using Patchbay.Core.Inheritance;
using Patchbay.Core.Model;
using Patchbay.Core.Serialization;

namespace Patchbay.Tests;

public class SerializationTests
{
    private static ConnectionDocument BuildDocument()
    {
        ConnectionDocument doc = new();

        GroupNode prod = new() { Name = "Production" };
        prod.Settings.UserName = @"CORP\svc_rdadmin";
        prod.Settings.GatewayHostName = "rdg.corp.local";
        prod.Settings.GatewayUsage = GatewayUsage.Always;

        ServerNode web = new() { Name = "WEB-PRD-01", HostName = "10.20.4.11" };
        ServerNode sql = new() { Name = "SQL-PRD-01", HostName = "10.20.4.31", Notes = "AG primary" };
        sql.Settings.DesktopWidth = 2560;
        sql.Settings.DesktopHeight = 1440;
        sql.Tags.Add("database");

        prod.Add(web);
        prod.Add(sql);
        doc.Root.Add(prod);

        return doc;
    }

    [Fact]
    public void Round_trip_preserves_the_tree_shape()
    {
        ConnectionDocument original = BuildDocument();

        ConnectionDocument restored = ConnectionDocumentSerializer.Deserialize(
            ConnectionDocumentSerializer.Serialize(original));

        Assert.Equal(ConnectionDocument.CurrentSchemaVersion, restored.SchemaVersion);
        Assert.Single(restored.Root.Children);

        GroupNode prod = Assert.IsType<GroupNode>(restored.Root.Children[0]);
        Assert.Equal("Production", prod.Name);
        Assert.Equal(2, prod.Children.Count);
        Assert.Equal(2, restored.AllServers.Count());
    }

    [Fact]
    public void Round_trip_preserves_node_identity_and_values()
    {
        ConnectionDocument original = BuildDocument();
        ServerNode originalSql = original.AllServers.Single(s => s.Name == "SQL-PRD-01");

        ConnectionDocument restored = ConnectionDocumentSerializer.Deserialize(
            ConnectionDocumentSerializer.Serialize(original));
        ServerNode restoredSql = restored.AllServers.Single(s => s.Name == "SQL-PRD-01");

        Assert.Equal(originalSql.Id, restoredSql.Id);
        Assert.Equal("10.20.4.31", restoredSql.HostName);
        Assert.Equal("AG primary", restoredSql.Notes);
        Assert.Equal(2560, restoredSql.Settings.DesktopWidth);
        Assert.Equal(["database"], restoredSql.Tags);
    }

    /// <summary>
    /// The failure this guards against is quiet and severe: parent links are
    /// not serialised, so without RebuildParentLinks every node resolves to
    /// defaults and every inherited credential and gateway silently vanishes.
    /// </summary>
    [Fact]
    public void Deserialising_restores_parent_links_so_inheritance_still_works()
    {
        ConnectionDocument restored = ConnectionDocumentSerializer.Deserialize(
            ConnectionDocumentSerializer.Serialize(BuildDocument()));

        ServerNode web = restored.AllServers.Single(s => s.Name == "WEB-PRD-01");

        Assert.NotNull(web.Parent);
        Assert.Equal("Production", web.Parent!.Name);
        Assert.Same(restored.Root, web.Parent.Parent);
        Assert.Equal("Connections / Production / WEB-PRD-01", web.DisplayPath);

        EffectiveSettings effective = SettingsResolver.Resolve(web);

        Assert.Equal(@"CORP\svc_rdadmin", effective.Values.UserName);
        Assert.Equal("rdg.corp.local", effective.Values.GatewayHostName);
        Assert.Equal(SettingOrigin.Inherited, effective.OriginOf(nameof(ConnectionSettings.UserName)));
    }

    [Fact]
    public void Node_kind_is_written_as_a_discriminator()
    {
        string json = ConnectionDocumentSerializer.Serialize(BuildDocument());

        Assert.Contains("\"$kind\": \"group\"", json, StringComparison.Ordinal);
        Assert.Contains("\"$kind\": \"server\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Enums_are_written_as_names_not_numbers()
    {
        string json = ConnectionDocumentSerializer.Serialize(BuildDocument());

        Assert.Contains("\"gatewayUsage\": \"Always\"", json, StringComparison.Ordinal);
    }

    /// <summary>
    /// Null means inherit, so an absent key must mean the same thing. Writing
    /// nulls out would bloat the file and add nothing.
    /// </summary>
    [Fact]
    public void Inherited_settings_are_omitted_rather_than_written_as_null()
    {
        string json = ConnectionDocumentSerializer.Serialize(BuildDocument());

        Assert.DoesNotContain("null", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"redirectPrinters\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Comments_and_trailing_commas_are_tolerated_on_read()
    {
        const string Json = """
            {
              // Someone hand-edited this file.
              "schemaVersion": 1,
              "root": {
                "$kind": "group",
                "name": "Connections",
                "children": [
                  {
                    "$kind": "server",
                    "name": "DC-01",
                    "hostName": "10.10.1.5",
                  },
                ],
              },
            }
            """;

        ConnectionDocument doc = ConnectionDocumentSerializer.Deserialize(Json);

        Assert.Equal("DC-01", doc.AllServers.Single().Name);
    }

    [Fact]
    public void A_newer_schema_version_is_refused_with_an_actionable_message()
    {
        string json = ConnectionDocumentSerializer.Serialize(
            new ConnectionDocument { SchemaVersion = ConnectionDocument.CurrentSchemaVersion + 1 });

        ConnectionDocumentException ex = Assert.Throws<ConnectionDocumentException>(
            () => ConnectionDocumentSerializer.Deserialize(json));

        Assert.Contains("Update Patchbay", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Malformed_json_is_reported_as_a_document_error()
    {
        ConnectionDocumentException ex = Assert.Throws<ConnectionDocumentException>(
            () => ConnectionDocumentSerializer.Deserialize("{ \"schemaVersion\": "));

        Assert.IsAssignableFrom<System.Text.Json.JsonException>(ex.InnerException);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Empty_input_is_refused(string? json)
    {
        Assert.ThrowsAny<ArgumentException>(() => ConnectionDocumentSerializer.Deserialize(json!));
    }
}
