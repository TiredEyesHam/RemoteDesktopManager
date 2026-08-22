using Patchbay.Core.Model;
using Patchbay.Core.Security;

namespace Patchbay.Core.Editing;

/// <summary>
/// What became of deleting a saved sign-in (M3-10).
/// </summary>
/// <param name="Deleted">Whether there was one to delete.</param>
/// <param name="Detached">
/// How many nodes were pointing at it and have been put back to asking each
/// time. Zero is the ordinary case and is worth saying anyway: "used by
/// nothing" is the answer somebody wants before they press Delete.
/// </param>
public readonly record struct CredentialDeletion(bool Deleted, int Detached);

/// <summary>
/// Adding, copying and removing saved sign-ins (M3-10).
///
/// Separate from <see cref="CredentialVault"/> because none of this touches a
/// secret. The vault owns the protector and is the only thing that can read or
/// write a password; this owns the list, and could not decrypt anything if it
/// wanted to.
///
/// <para>
/// Deleting is the one that has to ask, since M3-04. Removing a profile whose
/// password lives in the document removes the password with it; removing one
/// whose password lives in Windows Credential Manager leaves it there for
/// ever, with nothing in Patchbay that still knows it exists. So the deletion
/// hands the profile to the vault to be released first, rather than reaching
/// past it — which keeps the rule intact and is why the vault is a parameter
/// rather than something this class went and found.
/// </para>
/// </summary>
public static class CredentialOperations
{
    /// <summary>
    /// A name no other profile in the document is using, by adding a number
    /// where one is needed.
    ///
    /// Names are not identifiers and nothing breaks if two match, which is why
    /// this is a courtesy rather than a rule. What it prevents is a picker
    /// with two identical rows in it and no way to tell which is which.
    /// </summary>
    public static string UniqueName(
        ConnectionDocument document,
        string desired,
        CredentialProfile? ignore = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        string wanted = string.IsNullOrWhiteSpace(desired) ? "Saved sign-in" : desired.Trim();

        if (!IsTaken(wanted))
        {
            return wanted;
        }

        for (int suffix = 2; ; suffix++)
        {
            string candidate = $"{wanted} {suffix}";

            if (!IsTaken(candidate))
            {
                return candidate;
            }
        }

        bool IsTaken(string name) => document.Credentials.Any(c =>
            !ReferenceEquals(c, ignore)
            && string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Adds a profile, giving it a name nothing else is using.
    /// </summary>
    public static CredentialProfile Add(ConnectionDocument document, string name = "Saved sign-in")
    {
        ArgumentNullException.ThrowIfNull(document);

        CredentialProfile profile = new() { Name = UniqueName(document, name) };
        document.Credentials.Add(profile);

        return profile;
    }

    /// <summary>
    /// Copies a profile, including its protected password.
    ///
    /// The password travels because it is already protected and re-protecting
    /// would need the plaintext nothing here has. That makes a copy as useful
    /// as the original, which is the point: the usual reason to copy one is to
    /// keep the same password against a different account name.
    /// </summary>
    public static CredentialProfile Duplicate(ConnectionDocument document, CredentialProfile profile)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(profile);

        CredentialProfile copy = profile.CloneAsNew();
        copy.Name = UniqueName(document, $"{profile.Name} copy");

        document.Credentials.Insert(document.Credentials.IndexOf(profile) + 1, copy);

        return copy;
    }

    /// <summary>
    /// Removes a profile, releases its saved password, and puts every
    /// connection that named it back to asking each time.
    ///
    /// <para>
    /// Detaching rather than leaving the reference dangling, which is the
    /// decision worth arguing about. A node still holding the id of a deleted
    /// profile resolves to <c>ProfileMissing</c>, which is handled and
    /// explains itself, so nothing would break. What would happen is that the
    /// connection stays configured to use a saved sign-in for ever, and the
    /// only way to find out is to try connecting. Putting it back to
    /// <see cref="CredentialMode.Prompt"/> leaves it in a state somebody can
    /// see in the editor and act on.
    /// </para>
    ///
    /// <para>
    /// Only nodes whose own settings name it are touched. A node inheriting
    /// the profile from a group is fixed by fixing the group, and writing an
    /// override onto fifty servers to express that would be worse than the
    /// problem.
    /// </para>
    /// </summary>
    /// <param name="vault">
    /// Asked to release the profile's saved password before the profile goes.
    /// Required rather than optional, because the caller that forgets it is
    /// the caller that leaves a password in Windows with nothing pointing at
    /// it, and nothing about the result would look wrong.
    /// </param>
    public static CredentialDeletion Delete(
        ConnectionDocument document,
        Guid id,
        CredentialVault vault)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(vault);

        if (document.FindCredential(id) is not { } profile)
        {
            return new CredentialDeletion(Deleted: false, Detached: 0);
        }

        int detached = 0;

        foreach (ConnectionNode node in document.NodesUsingCredential(id).ToList())
        {
            node.Settings.CredentialProfileId = null;

            if (node.Settings.CredentialMode is CredentialMode.Profile)
            {
                node.Settings.CredentialMode = CredentialMode.Prompt;
            }

            detached++;
        }

        vault.ClearPassword(profile);
        document.Credentials.Remove(profile);

        return new CredentialDeletion(Deleted: true, Detached: detached);
    }
}
