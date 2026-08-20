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

    /// <summary>
    /// Named sign-ins the tree can point at (M3-01). Kept beside the tree
    /// rather than in it, because a profile is not a place to connect to and
    /// putting it in the tree would make it inherit settings and appear in
    /// search results.
    ///
    /// Added without a schema bump: a document written before this existed
    /// deserialises with an empty list, which is what it meant.
    /// </summary>
    public List<CredentialProfile> Credentials { get; set; } = [];

    [JsonIgnore]
    public IEnumerable<ServerNode> AllServers => Root.DescendantServers();

    [JsonIgnore]
    public IEnumerable<GroupNode> AllGroups => Root.Descendants().OfType<GroupNode>();

    /// <summary>Finds any node by id. Returns null when it is not in this document.</summary>
    public ConnectionNode? FindById(Guid id) =>
        Root.Id == id ? Root : Root.Descendants().FirstOrDefault(n => n.Id == id);

    /// <summary>
    /// Finds a credential profile by id, or null when nothing here has that id.
    ///
    /// Null is an ordinary answer rather than a fault: a profile can be
    /// deleted while nodes still name it, and an imported tree can arrive
    /// naming profiles that were never in this document. See
    /// <c>CredentialResolutionStatus.ProfileMissing</c>.
    /// </summary>
    public CredentialProfile? FindCredential(Guid id) =>
        Credentials.FirstOrDefault(c => c.Id == id);

    /// <summary>
    /// Every node whose own settings name this profile, so that deleting one
    /// can say what it is about to break rather than finding out afterwards.
    ///
    /// Own settings only, deliberately: a node that inherits the profile from
    /// an ancestor is not what has to be edited to stop using it.
    /// </summary>
    public IEnumerable<ConnectionNode> NodesUsingCredential(Guid id) =>
        Root.Descendants().Prepend(Root).Where(n => n.Settings.CredentialProfileId == id);

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
