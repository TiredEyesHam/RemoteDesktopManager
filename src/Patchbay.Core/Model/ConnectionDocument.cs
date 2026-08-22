using System.Text.Json.Serialization;
using Patchbay.Core.Security;

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
    public const int CurrentSchemaVersion = 3;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>
    /// Which document this is, as opposed to where it lives (M3-04).
    ///
    /// <para>
    /// Needed the moment a secret stopped living in the file. Windows
    /// Credential Manager holds the passwords and the document holds names for
    /// them, so entries have to be filed under something — and a person may
    /// have several documents, all of whose entries land in the same Windows
    /// store. The file path is the obvious candidate and is not stable: a
    /// document that is renamed or moved would abandon every password it had.
    /// </para>
    ///
    /// <para>
    /// A document written before this existed gets a new one on load, which is
    /// correct precisely because no build that lacked this property could have
    /// written anything that refers to it.
    /// </para>
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

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

    /// <summary>
    /// The document key, wrapped under a master password (M3-07), or null when
    /// this document has none.
    ///
    /// <para>
    /// This one <em>did</em> take a schema bump, unlike
    /// <see cref="Credentials"/>, and the difference is what happens when a
    /// build that has never heard of it opens the file. An unknown property is
    /// dropped on deserialisation and gone on the next save, which for a list
    /// of profiles means losing some settings and for this means losing the
    /// only copy of the key to every password in the document. That is exactly
    /// the failure the version check was put in for — "opening it now would
    /// discard settings on the next save" — so schema 2 is what a reader must
    /// understand before it is allowed to write this file back.
    /// </para>
    ///
    /// <para>
    /// Schema 3 was forced by <see cref="Id"/> for the same reason and not by
    /// <see cref="CredentialStore"/>, which is harmless to drop: the next password
    /// goes somewhere else and every existing one still says which scheme
    /// wrote it. Dropping the id is not harmless. A build that had never heard
    /// of it would write the file back without one, the next load would mint a
    /// fresh one, and every password the document keeps in Windows Credential
    /// Manager would be filed under an id nothing refers to any more — present,
    /// unreachable, and invisible to the sweep that exists to clear them up.
    /// </para>
    /// </summary>
    public MasterKeyRecord? MasterKey { get; set; }

    /// <summary>
    /// Which machine store new passwords go to (M3-04) — a
    /// <see cref="SecretEnvelope"/> scheme name, or null for whichever this
    /// build offers first.
    ///
    /// <para>
    /// Named for the credentials and not for the secrets, which is not a
    /// preference. <c>SecretStore</c> was the first name and the architecture
    /// gate rejected it: <c>SecretNames</c> treats any member whose name
    /// contains "Secret" as holding one, so this scheme name would have been
    /// masked in every log line that ever destructured a document and the type
    /// holding it would have owed an override it has no other reason to write.
    /// The gate is right to be crude about it — being wrong the other way
    /// prints a password — so the model takes the name that does not collide.
    /// </para>
    ///
    /// <para>
    /// A property of the document rather than of the application, because the
    /// alternative is a preference that silently applies to whatever file
    /// happens to be open. It is not a claim about what the document contains:
    /// existing passwords are read from whatever scheme each of them names,
    /// and a document is routinely mixed. This says only where the next one is
    /// written.
    /// </para>
    ///
    /// <para>
    /// Naming a store this build does not have is not an error and does not
    /// fall back. Quietly writing the next password somewhere other than where
    /// somebody chose is the invisible failure <c>M3-02</c> is about, so
    /// saving refuses and says which store is missing.
    /// </para>
    /// </summary>
    public string? CredentialStore { get; set; }

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
