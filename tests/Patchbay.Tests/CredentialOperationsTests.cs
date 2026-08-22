using Patchbay.Core.Editing;
using Patchbay.Core.Model;
using Patchbay.Core.Security;

namespace Patchbay.Tests;

/// <summary>
/// Managing the list of saved sign-ins (M3-10).
///
/// None of this touches a secret — the vault owns the protector and this owns
/// the list. What it does own is the question nobody wants to answer twice:
/// what happens to the fifty connections pointing at a profile somebody has
/// just deleted.
///
/// Deleting takes a vault since M3-04, because a profile whose password lives
/// in Windows rather than in the document has to have it released. These tests
/// hand over one built on the protector that refuses, which is enough: it can
/// be asked to forget and there is nothing to forget.
/// </summary>
public class CredentialOperationsTests
{
    private static ServerNode Server(string name, Guid? profileId = null, CredentialMode? mode = null)
    {
        ServerNode server = new() { Name = name, HostName = name };
        server.Settings.CredentialProfileId = profileId;
        server.Settings.CredentialMode = mode;

        return server;
    }

    // ── Names ───────────────────────────────────────────────────────────

    [Fact]
    public void A_first_profile_keeps_the_name_it_was_given()
        => Assert.Equal("Domain admin", CredentialOperations.Add(new ConnectionDocument(), "Domain admin").Name);

    [Fact]
    public void A_second_profile_of_the_same_name_is_numbered()
    {
        // Not because names are identifiers, but because a picker with two
        // identical rows gives nobody a way to tell which is which.
        ConnectionDocument document = new();
        CredentialOperations.Add(document, "Domain admin");

        Assert.Equal("Domain admin 2", CredentialOperations.Add(document, "Domain admin").Name);
    }

    [Fact]
    public void Numbering_keeps_going_past_the_second()
    {
        ConnectionDocument document = new();
        CredentialOperations.Add(document, "Admin");
        CredentialOperations.Add(document, "Admin");

        Assert.Equal("Admin 3", CredentialOperations.Add(document, "Admin").Name);
    }

    [Fact]
    public void Case_does_not_make_a_name_different()
    {
        // Numbered because "admin" collides with "Admin", and spelt the way it
        // was typed rather than the way the existing one was: the collision is
        // the document's business, the capitals are the person's.
        Assert.Equal("admin 2", CredentialOperations.UniqueName(WithNamed("Admin"), "admin"));
    }

    [Fact]
    public void A_profile_does_not_collide_with_itself()
    {
        // Renaming a profile to what it is already called must not number it.
        ConnectionDocument document = new();
        CredentialProfile profile = CredentialOperations.Add(document, "Admin");

        Assert.Equal("Admin", CredentialOperations.UniqueName(document, "Admin", profile));
    }

    [Fact]
    public void A_nameless_profile_gets_something_to_call_it()
        => Assert.Equal("Saved sign-in", CredentialOperations.Add(new ConnectionDocument(), "   ").Name);

    // ── Copies ──────────────────────────────────────────────────────────

    [Fact]
    public void A_copy_keeps_the_password_and_takes_a_new_id()
    {
        // The usual reason to copy one is to keep the same password against a
        // different account name.
        ConnectionDocument document = new();
        CredentialProfile original = CredentialOperations.Add(document, "Admin");
        original.UserName = "svc-deploy";
        original.ProtectedPassword = "pb1:dpapi:AAAA";

        CredentialProfile copy = CredentialOperations.Duplicate(document, original);

        Assert.NotEqual(original.Id, copy.Id);
        Assert.Equal("Admin copy", copy.Name);
        Assert.Equal("svc-deploy", copy.UserName);
        Assert.Equal(original.ProtectedPassword, copy.ProtectedPassword);
    }

    [Fact]
    public void A_copy_lands_next_to_what_it_came_from()
    {
        ConnectionDocument document = new();
        CredentialProfile first = CredentialOperations.Add(document, "First");
        CredentialOperations.Add(document, "Last");

        CredentialProfile copy = CredentialOperations.Duplicate(document, first);

        Assert.Equal(1, document.Credentials.IndexOf(copy));
    }

    // ── Deleting ────────────────────────────────────────────────────────

    [Fact]
    public void Deleting_something_that_is_not_there_says_so()
    {
        CredentialDeletion result = CredentialOperations.Delete(new ConnectionDocument(), Guid.NewGuid(), Vault);

        Assert.False(result.Deleted);
        Assert.Equal(0, result.Detached);
    }

    [Fact]
    public void Deleting_an_unused_profile_touches_nothing_else()
    {
        ConnectionDocument document = new();
        CredentialProfile profile = CredentialOperations.Add(document, "Admin");
        document.Root.Children.Add(Server("web-01"));

        CredentialDeletion result = CredentialOperations.Delete(document, profile.Id, Vault);

        Assert.True(result.Deleted);
        Assert.Equal(0, result.Detached);
        Assert.Empty(document.Credentials);
    }

    [Fact]
    public void Deleting_a_profile_puts_what_used_it_back_to_asking()
    {
        // A node left holding the id of a deleted profile would resolve to
        // ProfileMissing for ever, and the only way to find out is to try
        // connecting. Prompt is a state somebody can see in the editor.
        ConnectionDocument document = new();
        CredentialProfile profile = CredentialOperations.Add(document, "Admin");

        ServerNode uses = Server("web-01", profile.Id, CredentialMode.Profile);
        document.Root.Children.Add(uses);

        CredentialDeletion result = CredentialOperations.Delete(document, profile.Id, Vault);

        Assert.Equal(1, result.Detached);
        Assert.Null(uses.Settings.CredentialProfileId);
        Assert.Equal(CredentialMode.Prompt, uses.Settings.CredentialMode);
    }

    [Fact]
    public void Deleting_counts_everything_that_named_it()
    {
        ConnectionDocument document = new();
        CredentialProfile profile = CredentialOperations.Add(document, "Admin");

        document.Root.Children.Add(Server("web-01", profile.Id, CredentialMode.Profile));
        document.Root.Children.Add(Server("web-02", profile.Id, CredentialMode.Profile));
        document.Root.Children.Add(Server("web-03"));

        Assert.Equal(2, CredentialOperations.Delete(document, profile.Id, Vault).Detached);
    }

    [Fact]
    public void A_node_that_only_inherits_the_profile_is_left_alone()
    {
        // Fixing the group fixes the children. Writing an override onto fifty
        // servers to say so would be worse than the problem.
        ConnectionDocument document = new();
        CredentialProfile profile = CredentialOperations.Add(document, "Admin");

        GroupNode group = new() { Name = "Production" };
        group.Settings.CredentialProfileId = profile.Id;
        group.Settings.CredentialMode = CredentialMode.Profile;

        ServerNode child = Server("web-01");
        group.Children.Add(child);
        document.Root.Children.Add(group);

        CredentialDeletion result = CredentialOperations.Delete(document, profile.Id, Vault);

        Assert.Equal(1, result.Detached);
        Assert.Null(child.Settings.CredentialMode);
    }

    [Fact]
    public void A_node_naming_the_profile_without_using_it_keeps_its_own_mode()
    {
        // Set to prompt but still carrying an id, which is what M3-05 leaves
        // behind when somebody switches mode without clearing the picker. The
        // id goes; the mode was already what it should be and is not rewritten.
        ConnectionDocument document = new();
        CredentialProfile profile = CredentialOperations.Add(document, "Admin");

        ServerNode node = Server("web-01", profile.Id, CredentialMode.CurrentUser);
        document.Root.Children.Add(node);

        CredentialOperations.Delete(document, profile.Id, Vault);

        Assert.Null(node.Settings.CredentialProfileId);
        Assert.Equal(CredentialMode.CurrentUser, node.Settings.CredentialMode);
    }

    private static readonly CredentialVault Vault = new(UnavailableSecretProtector.Instance);

    private static ConnectionDocument WithNamed(string name)
    {
        ConnectionDocument document = new();
        CredentialOperations.Add(document, name);

        return document;
    }
}
