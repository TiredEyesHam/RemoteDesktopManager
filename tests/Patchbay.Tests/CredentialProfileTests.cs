using System.Text;
using Patchbay.Core.Model;
using Patchbay.Core.Security;
using Patchbay.Core.Serialization;
using Patchbay.Core.Sessions;

namespace Patchbay.Tests;

/// <summary>
/// Named sign-ins, and turning one into the credentials for an attempt
/// (M3-01).
///
/// The interesting cases are all the ones where something is missing: a
/// profile deleted while nodes still name it, a password saved by a different
/// Windows account, a document that predates profiles entirely. None of those
/// may refuse a connection, and none may destroy what is stored.
/// </summary>
public class CredentialProfileTests
{
    private const string Password = "hunter2-correct-horse";

    private static (ConnectionDocument Document, CredentialProfile Profile) WithProfile(
        CredentialVault vault,
        bool withPassword = true)
    {
        CredentialProfile profile = new()
        {
            Name = "Domain admin",
            UserName = "svc-deploy",
            Domain = "CORP",
        };

        if (withPassword)
        {
            vault.SavePassword(profile, Password);
        }

        ConnectionDocument document = new();
        document.Credentials.Add(profile);

        return (document, profile);
    }

    private static ConnectionSettings Using(Guid? id) => new()
    {
        CredentialMode = CredentialMode.Profile,
        CredentialProfileId = id,
    };

    // ── The profile itself ──────────────────────────────────────────────

    [Fact]
    public void A_new_profile_has_an_id_of_its_own()
        => Assert.NotEqual(Guid.Empty, new CredentialProfile().Id);

    [Fact]
    public void An_account_reads_the_way_a_person_writes_it()
        => Assert.Equal("CORP\\svc-deploy", new CredentialProfile
        {
            UserName = "svc-deploy",
            Domain = "CORP",
        }.Display);

    [Fact]
    public void A_local_account_is_shown_without_a_domain()
        => Assert.Equal("admin", new CredentialProfile { UserName = "admin" }.Display);

    [Fact]
    public void A_label_carries_the_name_and_the_account()
        => Assert.Equal("Domain admin (CORP\\svc-deploy)", new CredentialProfile
        {
            Name = "Domain admin",
            UserName = "svc-deploy",
            Domain = "CORP",
        }.Label);

    [Fact]
    public void A_profile_with_no_name_is_labelled_by_its_account()
        => Assert.Equal("admin", new CredentialProfile { UserName = "admin" }.Label);

    [Fact]
    public void A_duplicate_is_a_new_profile_and_not_a_second_reference()
    {
        CredentialProfile original = new() { Name = "Admin", UserName = "a", ProtectedPassword = "pb1:x:y" };
        CredentialProfile copy = original.CloneAsNew();

        Assert.NotEqual(original.Id, copy.Id);
        Assert.Equal(original.Name, copy.Name);
        Assert.Equal(original.ProtectedPassword, copy.ProtectedPassword);
    }

    [Fact]
    public void A_profile_does_not_print_what_it_is_holding()
    {
        CredentialProfile profile = new() { Name = "Admin", ProtectedPassword = "pb1:reverse:AAAA" };

        Assert.DoesNotContain("AAAA", profile.ToString(), StringComparison.Ordinal);
        Assert.Contains("password saved", profile.ToString(), StringComparison.Ordinal);
    }

    // ── Where they live ─────────────────────────────────────────────────

    [Fact]
    public void A_document_starts_with_no_profiles()
        => Assert.Empty(new ConnectionDocument().Credentials);

    [Fact]
    public void A_profile_is_found_by_id()
    {
        (ConnectionDocument document, CredentialProfile profile) = WithProfile(Vault());

        Assert.Same(profile, document.FindCredential(profile.Id));
    }

    [Fact]
    public void An_id_nothing_holds_finds_nothing()
        => Assert.Null(new ConnectionDocument().FindCredential(Guid.NewGuid()));

    [Fact]
    public void The_nodes_using_a_profile_can_be_listed_before_it_is_deleted()
    {
        (ConnectionDocument document, CredentialProfile profile) = WithProfile(Vault());

        ServerNode uses = new() { Name = "web-01", HostName = "web-01" };
        uses.Settings.CredentialProfileId = profile.Id;

        ServerNode doesNot = new() { Name = "web-02", HostName = "web-02" };

        document.Root.Children.Add(uses);
        document.Root.Children.Add(doesNot);

        Assert.Equal([uses], document.NodesUsingCredential(profile.Id));
    }

    [Fact]
    public void A_node_that_only_inherits_a_profile_is_not_listed_as_using_it()
    {
        // What has to be edited to stop using a profile is the node that names
        // it, which is the group. Listing the children would send somebody to
        // fifty servers to change one setting.
        (ConnectionDocument document, CredentialProfile profile) = WithProfile(Vault());

        GroupNode group = new() { Name = "Production" };
        group.Settings.CredentialProfileId = profile.Id;

        ServerNode child = new() { Name = "web-01", HostName = "web-01" };
        group.Children.Add(child);
        document.Root.Children.Add(group);

        Assert.Equal([group], document.NodesUsingCredential(profile.Id));
    }

    // ── Saving a password ───────────────────────────────────────────────

    [Fact]
    public void A_saved_password_is_never_the_plaintext()
    {
        (_, CredentialProfile profile) = WithProfile(Vault());

        Assert.NotNull(profile.ProtectedPassword);
        Assert.DoesNotContain(Password, profile.ProtectedPassword, StringComparison.Ordinal);
        Assert.StartsWith("pb1:", profile.ProtectedPassword, StringComparison.Ordinal);
    }

    [Fact]
    public void A_password_that_could_not_be_protected_is_not_stored_at_all()
    {
        // The fallback that must not exist. Storing plaintext here changes
        // nothing on screen and leaves a password in a file people back up.
        CredentialVault vault = new(new ReversingProtector { Working = false });
        CredentialProfile profile = new() { Name = "Admin" };

        Assert.Throws<SecretProtectionException>(() => vault.SavePassword(profile, Password));
        Assert.Null(profile.ProtectedPassword);
    }

    [Fact]
    public void A_failed_save_leaves_the_previous_password_alone()
    {
        CredentialProfile profile = new() { Name = "Admin" };
        Vault().SavePassword(profile, Password);
        string? saved = profile.ProtectedPassword;

        CredentialVault broken = new(new ReversingProtector { Working = false });

        Assert.Throws<SecretProtectionException>(() => broken.SavePassword(profile, "new-one"));
        Assert.Equal(saved, profile.ProtectedPassword);
    }

    [Fact]
    public void Forgetting_a_password_keeps_the_account()
    {
        (_, CredentialProfile profile) = WithProfile(Vault());

        CredentialVault.ClearPassword(profile);

        Assert.False(profile.HasPassword);
        Assert.Equal("svc-deploy", profile.UserName);
        Assert.Equal("CORP", profile.Domain);
    }

    [Fact]
    public void Saving_is_not_offered_where_it_cannot_work()
        => Assert.False(new CredentialVault(new ReversingProtector { Working = false }).CanSavePasswords);

    // ── Resolving one ───────────────────────────────────────────────────

    [Fact]
    public void A_profile_resolves_to_the_sign_in_it_holds()
    {
        CredentialVault vault = Vault();
        (ConnectionDocument document, CredentialProfile profile) = WithProfile(vault);

        CredentialResolution resolved = vault.Resolve(document, Using(profile.Id));

        Assert.Equal(CredentialResolutionStatus.Resolved, resolved.Status);
        Assert.Equal("svc-deploy", resolved.Credentials.UserName);
        Assert.Equal("CORP", resolved.Credentials.Domain);
        Assert.Equal(Password, resolved.Credentials.Password);
        Assert.True(resolved.IsComplete);
        Assert.Null(resolved.Notice);
    }

    [Fact]
    public void A_profile_with_no_saved_password_resolves_and_is_not_complete()
    {
        // Configured correctly, and still needs asking. That is M3-05, not an
        // error here.
        CredentialVault vault = Vault();
        (ConnectionDocument document, CredentialProfile profile) = WithProfile(vault, withPassword: false);

        CredentialResolution resolved = vault.Resolve(document, Using(profile.Id));

        Assert.Equal(CredentialResolutionStatus.Resolved, resolved.Status);
        Assert.Equal("svc-deploy", resolved.Credentials.UserName);
        Assert.False(resolved.Credentials.HasPassword);
        Assert.False(resolved.IsComplete);
    }

    [Fact]
    public void Prompting_asks_for_no_profile_at_all()
    {
        CredentialResolution resolved = Vault().Resolve(
            new ConnectionDocument(),
            new ConnectionSettings { CredentialMode = CredentialMode.Prompt });

        Assert.Equal(CredentialResolutionStatus.NoProfile, resolved.Status);
        Assert.Equal(SessionCredentials.None, resolved.Credentials);
    }

    [Fact]
    public void Single_sign_on_asks_for_no_profile_either()
    {
        CredentialResolution resolved = Vault().Resolve(
            new ConnectionDocument(),
            new ConnectionSettings { CredentialMode = CredentialMode.CurrentUser });

        Assert.Equal(CredentialResolutionStatus.NoProfile, resolved.Status);
        Assert.Equal(SessionCredentials.None, resolved.Credentials);
    }

    [Fact]
    public void A_deleted_profile_says_so_and_still_permits_connecting()
    {
        CredentialResolution resolved = Vault().Resolve(
            new ConnectionDocument(),
            Using(Guid.NewGuid()));

        Assert.Equal(CredentialResolutionStatus.ProfileMissing, resolved.Status);
        Assert.False(resolved.IsComplete);
        Assert.NotNull(resolved.Notice);
    }

    [Fact]
    public void Naming_no_profile_while_configured_to_use_one_is_the_same_as_a_deleted_one()
    {
        // The same thing has to happen next either way: ask.
        CredentialResolution resolved = Vault().Resolve(new ConnectionDocument(), Using(null));

        Assert.Equal(CredentialResolutionStatus.ProfileMissing, resolved.Status);
    }

    [Fact]
    public void A_password_from_another_account_is_reported_and_not_overwritten()
    {
        CredentialVault vault = Vault();
        (ConnectionDocument document, CredentialProfile profile) = WithProfile(vault, withPassword: false);

        // 0xFF is what the test protector refuses, standing in for a DPAPI
        // blob written by a different Windows account.
        profile.ProtectedPassword = "pb1:" + ReversingProtector.Name + ":" + Convert.ToBase64String([0xFF, 0x01]);
        string? stored = profile.ProtectedPassword;

        CredentialResolution resolved = vault.Resolve(document, Using(profile.Id));

        Assert.Equal(CredentialResolutionStatus.PasswordUnreadable, resolved.Status);
        Assert.True(resolved.ShouldPreserveStoredPassword);
        Assert.Equal(stored, profile.ProtectedPassword);

        // The account is still known, so a prompt can be filled in.
        Assert.Equal("svc-deploy", resolved.Credentials.UserName);
        Assert.False(resolved.Credentials.HasPassword);
        Assert.NotNull(resolved.Notice);
    }

    [Fact]
    public void A_resolution_does_not_print_the_password()
    {
        CredentialVault vault = Vault();
        (ConnectionDocument document, CredentialProfile profile) = WithProfile(vault);

        Assert.DoesNotContain(
            Password,
            vault.Resolve(document, Using(profile.Id)).ToString(),
            StringComparison.Ordinal);
    }

    // ── Round trip ──────────────────────────────────────────────────────

    [Fact]
    public void Profiles_survive_being_written_and_read_back()
    {
        CredentialVault vault = Vault();
        (ConnectionDocument document, CredentialProfile profile) = WithProfile(vault);

        ConnectionDocument reloaded = ConnectionDocumentSerializer.Deserialize(
            ConnectionDocumentSerializer.Serialize(document),
            []);

        CredentialProfile? back = reloaded.FindCredential(profile.Id);

        Assert.NotNull(back);
        Assert.Equal("Domain admin", back.Name);
        Assert.Equal("svc-deploy", back.UserName);
        Assert.Equal(profile.ProtectedPassword, back.ProtectedPassword);
        Assert.Equal(Password, vault.Resolve(reloaded, Using(profile.Id)).Credentials.Password);
    }

    [Fact]
    public void The_written_document_never_holds_the_plaintext()
    {
        (ConnectionDocument document, _) = WithProfile(Vault());

        Assert.DoesNotContain(
            Password,
            ConnectionDocumentSerializer.Serialize(document),
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_document_written_before_profiles_existed_reads_as_having_none()
    {
        // The reason this needed no schema bump: absent and empty mean the
        // same thing, so an older file is not a migration.
        const string Json = """{"schemaVersion":1,"root":{"name":"Connections","children":[]}}""";

        Assert.Empty(ConnectionDocumentSerializer.Deserialize(Json, []).Credentials);
    }

    private static CredentialVault Vault() => new(new ReversingProtector());

    /// <summary>
    /// Reverses the bytes. Not protection, and not meant to be — what is under
    /// test here is the profile and the vault, not the platform call. A
    /// payload starting <c>0xFF</c> is refused, which is how a blob written by
    /// another Windows account behaves when DPAPI is asked to open it here.
    /// </summary>
    private sealed class ReversingProtector : SecretProtector
    {
        public const string Name = "reverse";

        public bool Working { get; init; } = true;

        public override string Scheme => Name;

        public override bool IsAvailable => Working;

        protected override byte[] ProtectCore(string secret)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(secret);
            Array.Reverse(bytes);
            return bytes;
        }

        protected override SecretUnprotectResult UnprotectCore(ReadOnlySpan<byte> payload)
        {
            if (payload.Length > 0 && payload[0] == 0xFF)
            {
                return SecretUnprotectResult.Failed(SecretUnprotectStatus.Unreadable);
            }

            byte[] bytes = payload.ToArray();
            Array.Reverse(bytes);
            return SecretUnprotectResult.Success(Encoding.UTF8.GetString(bytes));
        }
    }
}
