using System.Text.Json.Serialization;

namespace Patchbay.Core.Model;

/// <summary>
/// The whole connection tree, as loaded from or saved to a single file.
/// </summary>
public sealed class ConnectionDocument
{
    /// <summary>
    /// Bumped whenever the on-disk shape changes in a way older readers cannot
    /// handle. Checked on load so a newer document fails loudly rather than
    /// being silently half-read and then saved back over, which is how people
    /// lose connection lists.
    /// </summary>
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>
    /// The implicit top-level group. Its settings act as document-wide
    /// defaults, one level below <see cref="ConnectionSettings.Defaults"/>.
    /// </summary>
    public GroupNode Root { get; set; } = new() { Name = "Connections" };

    [JsonIgnore]
    public IEnumerable<ServerNode> AllServers => Root.DescendantServers();

    [JsonIgnore]
    public IEnumerable<GroupNode> AllGroups => Root.Descendants().OfType<GroupNode>();

    /// <summary>Finds any node by id. Returns null when it is not in this document.</summary>
    public ConnectionNode? FindById(Guid id) =>
        Root.Id == id ? Root : Root.Descendants().FirstOrDefault(n => n.Id == id);

    /// <summary>
    /// Restores every <see cref="ConnectionNode.Parent"/> by walking the tree.
    ///
    /// Parent links are deliberately not serialised — they would make the
    /// document a cycle — so a freshly deserialised tree has none, and
    /// inheritance would silently resolve to defaults for every node. The
    /// deserialiser always calls this; it is public so that importers (M1-13
    /// onwards), which build trees by hand, can call it too.
    /// </summary>
    public void RebuildParentLinks()
    {
        Root.Parent = null;
        Relink(Root);

        static void Relink(GroupNode group)
        {
            foreach (ConnectionNode child in group.Children)
            {
                child.Parent = group;

                if (child is GroupNode nested)
                {
                    Relink(nested);
                }
            }
        }
    }
}
