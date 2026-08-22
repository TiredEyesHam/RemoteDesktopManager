using Patchbay.Core.Editing;
using Patchbay.Core.Model;
using Patchbay.Core.Security;

namespace Patchbay.Tests;

/// <summary>
/// Keeping saved passwords somewhere other than the document (M3-04).
///
/// <para>
/// What the real store does that no store before it did is keep the secret
/// outside the file. That is one sentence and three new failure modes: the
/// document can point at an entry that is gone, the store can hold an entry
/// the document has forgotten, and moving between stores can do either. All
/// three are exercised here against a stand-in that behaves like Windows
/// Credential Manager and has no Windows in it — the parts worth getting wrong
/// are which entry gets released and when, and none of them are P/Invoke.
/// </para>
/// </summary>
public class CredentialStoreTests
{
    private const string Password = "hunter2-correct-horse";
    private const string Master = "correct horse battery staple";

    // ── Releasing what the document stops pointing at ───────────────────

    [Fact]
    public void Saving_a_password_puts_it_in_the_store_and_a_name_in_the_document()
    {
        (ExternalStore store, DocumentProtection protection, ConnectionDocument document) = Outside();
        CredentialVault vault = new(protection);
        CredentialProfile profile = CredentialOperations.Add(document, "Admin");

        vault.SavePassword(profile, Secret.From(Password));

        Assert.Equal(1, store.Held);
        Assert.Equal(Password, Read(protection, profile));

        // The thing in the file is a reference and nothing else. Sixteen bytes
        // of identifier is what stops a hand-edited document naming any
        // credential on the machine and having Patchbay read it back.
        Assert.True(SecretEnvelope.TryParse(profile.ProtectedPassword, out SecretEnvelope? envelope));
        Assert.Equal(ExternalStore.Name, envelope.Scheme);
        Assert.Equal(16, envelope.Payload.Length);
    }

    [Fact]
    public void Replacing_a_password_releases_the_one_it_replaced()
    {
        // The failure without this is invisible and permanent: every password
        // ever changed stays in Windows for ever, under a name nothing refers
        // to, and the person sees them piling up in the control panel.
        (ExternalStore store, DocumentProtection protection, ConnectionDocument document) = Outside();
        CredentialVault vault = new(protection);
        CredentialProfile profile = CredentialOperations.Add(document, "Admin");

        vault.SavePassword(profile, Secret.From(Password));
        vault.SavePassword(profile, Secret.From("a-different-one"));

        Assert.Equal(1, store.Held);
        Assert.Equal("a-different-one", Read(protection, profile));
    }

    [Fact]
    public void A_replacement_that_fails_leaves_the_old_password_where_it_is()
    {
        // Release comes after the new one has landed, so a store that refuses
        // mid-way leaves a working password rather than neither.
        (ExternalStore store, DocumentProtection protection, ConnectionDocument document) = Outside();
        CredentialVault vault = new(protection);
        CredentialProfile profile = CredentialOperations.Add(document, "Admin");

        vault.SavePassword(profile, Secret.From(Password));
        store.Refusing = true;

        Assert.Throws<SecretProtectionException>(
            () => vault.SavePassword(profile, Secret.From("a-different-one")));

        store.Refusing = false;

        Assert.Equal(1, store.Held);
        Assert.Equal(Password, Read(protection, profile));
    }

    [Fact]
    public void Forgetting_a_password_releases_the_entry_behind_it()
    {
        (ExternalStore store, DocumentProtection protection, ConnectionDocument document) = Outside();
        CredentialVault vault = new(protection);
        CredentialProfile profile = CredentialOperations.Add(document, "Admin");

        vault.SavePassword(profile, Secret.From(Password));
        vault.ClearPassword(profile);

        Assert.Equal(0, store.Held);
        Assert.False(profile.HasPassword);
    }

    [Fact]
    public void Deleting_a_profile_releases_the_entry_behind_it()
    {
        (ExternalStore store, DocumentProtection protection, ConnectionDocument document) = Outside();
        CredentialVault vault = new(protection);
        CredentialProfile profile = CredentialOperations.Add(document, "Admin");

        vault.SavePassword(profile, Secret.From(Password));
        CredentialOperations.Delete(document, profile.Id, vault);

        Assert.Equal(0, store.Held);
        Assert.Empty(document.Credentials);
    }

    [Fact]
    public void Being_asked_to_release_the_same_thing_twice_is_not_an_error()
    {
        // Ordinary rather than defensive: a document restored from a backup
        // refers to entries a later version already released.
        (ExternalStore store, DocumentProtection protection, ConnectionDocument document) = Outside();
        CredentialVault vault = new(protection);
        CredentialProfile profile = CredentialOperations.Add(document, "Admin");

        vault.SavePassword(profile, Secret.From(Password));

        string envelope = profile.ProtectedPassword!;

        protection.Forget(envelope);
        protection.Forget(envelope);

        Assert.Equal(0, store.Held);
    }

    [Fact]
    public void A_store_is_never_asked_to_release_another_schemes_blob()
    {
        // Deleting on a looser test than reading is how a scheme forgets
        // somebody else's secret, and a bad delete cannot be taken back.
        ExternalStore store = new();
        MachineStore machine = new();

        using DocumentProtection protection = new(machine, store);
        ConnectionDocument document = new();
        protection.Open(document);

        CredentialVault vault = new(protection);
        CredentialProfile profile = CredentialOperations.Add(document, "Admin");

        vault.SavePassword(profile, Secret.From(Password));

        Assert.Equal(0, store.Held);

        protection.Forget(profile.ProtectedPassword);
        protection.Forget("pb1:nobody:AAAA");
        protection.Forget("not an envelope at all");
        protection.Forget(null);
    }

    [Fact]
    public void A_store_that_keeps_the_secret_in_the_document_releases_nothing()
    {
        // The base class does nothing, which is correct for every store that
        // encrypts: clearing the field is the whole of forgetting.
        MachineStore machine = new();

        using DocumentProtection protection = new(machine);
        ConnectionDocument document = new();
        protection.Open(document);

        CredentialVault vault = new(protection);
        CredentialProfile profile = CredentialOperations.Add(document, "Admin");

        vault.SavePassword(profile, Secret.From(Password));
        vault.ClearPassword(profile);

        Assert.False(profile.HasPassword);
    }

    // ── Choosing where they live ────────────────────────────────────────

    [Fact]
    public void Moving_to_another_store_carries_the_passwords_and_says_how_many()
    {
        (ExternalStore store, MachineStore machine, DocumentProtection protection, ConnectionDocument document) =
            Both(passwords: 3);

        SecretStoreChange result = protection.UseMachineStore(document, ExternalStore.Name);

        Assert.Equal(SecretStoreChangeStatus.Moved, result.Status);
        Assert.Equal(3, result.Moved);
        Assert.Equal(0, result.LeftAlone);
        Assert.Equal(3, store.Held);
        Assert.All(document.Credentials, profile => Assert.Equal(Password, Read(protection, profile)));

        _ = machine;
    }

    [Fact]
    public void Moving_away_from_a_store_releases_what_it_was_holding()
    {
        (ExternalStore store, _, DocumentProtection protection, ConnectionDocument document) =
            Both(passwords: 2);

        protection.UseMachineStore(document, ExternalStore.Name);

        Assert.Equal(2, store.Held);

        protection.UseMachineStore(document, MachineStore.Name);

        // Overwriting the reference in the document does not delete what it
        // referred to. If this ever regresses, every move leaves the whole
        // document's worth of passwords behind in Windows.
        Assert.Equal(0, store.Held);
        Assert.All(document.Credentials, profile => Assert.Equal(Password, Read(protection, profile)));
    }

    [Fact]
    public void A_password_this_account_cannot_read_is_left_exactly_where_it_is()
    {
        (ExternalStore _, MachineStore _, DocumentProtection protection, ConnectionDocument document) =
            Both(passwords: 1);

        document.Credentials.Add(new CredentialProfile
        {
            Name = "Somebody else's",
            ProtectedPassword = MachineStore.SomebodyElses,
        });

        SecretStoreChange result = protection.UseMachineStore(document, ExternalStore.Name);

        Assert.Equal(1, result.Moved);
        Assert.Equal(1, result.LeftAlone);
        Assert.Equal(MachineStore.SomebodyElses, document.Credentials[1].ProtectedPassword);
        Assert.Contains("left where", result.Notice, StringComparison.Ordinal);
    }

    [Fact]
    public void The_choice_is_written_down_so_the_next_password_goes_the_same_way()
    {
        (_, _, DocumentProtection protection, ConnectionDocument document) = Both(passwords: 0);

        protection.UseMachineStore(document, ExternalStore.Name);

        Assert.Equal(ExternalStore.Name, document.CredentialStore);
        Assert.Equal(ExternalStore.Name, protection.MachineStoreScheme);
        Assert.True(protection.UsesExternalStore);
    }

    [Fact]
    public void Reopening_a_document_honours_where_it_said_its_passwords_are()
    {
        (ExternalStore store, MachineStore machine, DocumentProtection protection, ConnectionDocument document) =
            Both(passwords: 1);

        protection.UseMachineStore(document, ExternalStore.Name);
        protection.Dispose();

        using DocumentProtection reopened = new(machine, store);
        reopened.Open(document);

        Assert.Equal(ExternalStore.Name, reopened.MachineStoreScheme);
        Assert.Equal(Password, Read(reopened, document.Credentials[0]));
    }

    [Fact]
    public void Choosing_the_store_already_in_use_changes_nothing()
    {
        (_, _, DocumentProtection protection, ConnectionDocument document) = Both(passwords: 1);

        protection.UseMachineStore(document, ExternalStore.Name);
        SecretStoreChange again = protection.UseMachineStore(document, ExternalStore.Name);

        Assert.Equal(SecretStoreChangeStatus.AlreadyThere, again.Status);
        Assert.True(again.IsSuccess);
        Assert.False(again.ChangedTheDocument);
    }

    [Fact]
    public void A_store_this_build_does_not_have_is_refused_rather_than_guessed_at()
    {
        (_, _, DocumentProtection protection, ConnectionDocument document) = Both(passwords: 1);

        SecretStoreChange result = protection.UseMachineStore(document, "keyvault");

        Assert.Equal(SecretStoreChangeStatus.NoSuchStore, result.Status);
        Assert.Null(document.CredentialStore);
    }

    [Fact]
    public void A_store_that_is_not_working_takes_nothing_with_it()
    {
        (ExternalStore store, _, DocumentProtection protection, ConnectionDocument document) =
            Both(passwords: 2);

        store.Working = false;

        SecretStoreChange result = protection.UseMachineStore(document, ExternalStore.Name);

        Assert.Equal(SecretStoreChangeStatus.Unavailable, result.Status);
        Assert.Null(document.CredentialStore);
        Assert.All(document.Credentials, profile => Assert.Equal(Password, Read(protection, profile)));
    }

    [Fact]
    public void A_store_that_says_yes_and_then_refuses_moves_nothing()
    {
        // Available and then refusing anyway: a policy that changed under the
        // application, a full store, a password longer than the store takes.
        // Reading comes before writing, so this is a refusal rather than a
        // document with half its passwords in each place.
        (ExternalStore store, _, DocumentProtection protection, ConnectionDocument document) =
            Both(passwords: 2);

        store.Refusing = true;

        SecretStoreChange result = protection.UseMachineStore(document, ExternalStore.Name);

        Assert.Equal(SecretStoreChangeStatus.Unavailable, result.Status);
        Assert.Null(document.CredentialStore);
        Assert.Equal(0, store.Held);

        store.Refusing = false;

        Assert.All(document.Credentials, profile => Assert.Equal(Password, Read(protection, profile)));
    }

    [Fact]
    public void Removing_a_master_password_onto_a_refusing_store_leaves_it_on()
    {
        // The one direction where giving up halfway would be worst: the
        // passwords have to end up somewhere, and "nowhere" is not an option
        // that leaves them readable.
        (ExternalStore store, _, DocumentProtection protection, ConnectionDocument document) =
            Both(passwords: 2);

        protection.UseMachineStore(document, ExternalStore.Name);
        protection.Set(document, Master);

        store.Refusing = true;

        MasterPasswordChange result = protection.Remove(document, Master);

        Assert.Equal(MasterPasswordChangeStatus.NowhereToPutPasswords, result.Status);
        Assert.True(protection.IsProtected);
        Assert.All(document.Credentials, profile => Assert.Equal(Password, Read(protection, profile)));
    }

    [Fact]
    public void A_document_behind_a_master_password_does_not_pretend_to_choose()
    {
        (_, _, DocumentProtection protection, ConnectionDocument document) = Both(passwords: 1);

        protection.Set(document, Master);

        SecretStoreChange result = protection.UseMachineStore(document, ExternalStore.Name);

        Assert.Equal(SecretStoreChangeStatus.Locked, result.Status);
        Assert.Null(document.CredentialStore);
        Assert.Contains("master password", result.Notice, StringComparison.Ordinal);
    }

    [Fact]
    public void A_document_naming_a_store_this_build_lacks_refuses_to_save()
    {
        // Not a fallback. Quietly writing the next password to a different
        // store than the one somebody chose is the invisible failure M3-02 is
        // about, and here it would also be a downgrade of a deliberate choice.
        ConnectionDocument document = new() { CredentialStore = "keyvault" };

        using DocumentProtection protection = new(new MachineStore());
        protection.Open(document);

        Assert.True(protection.NamesAnUnknownStore);
        Assert.False(protection.IsAvailable);
        Assert.Throws<SecretProtectionException>(() => protection.Protect(Secret.From(Password)));

        // And it is a state to get out of rather than a dead end: choosing a
        // store this build does have puts the document back to working.
        Assert.True(protection.UseMachineStore(document, MachineStore.Name).IsSuccess);
        Assert.False(protection.NamesAnUnknownStore);
        Assert.True(protection.IsAvailable);
    }

    // ── Reading a document whose passwords are in more than one place ───

    [Fact]
    public void A_password_left_behind_in_the_old_store_is_still_readable()
    {
        // Which is what makes a half-finished move survivable rather than a
        // document with half its passwords lost.
        (ExternalStore _, MachineStore _, DocumentProtection protection, ConnectionDocument document) =
            Both(passwords: 1);

        string inTheDocument = document.Credentials[0].ProtectedPassword!;

        protection.UseMachineStore(document, ExternalStore.Name);

        document.Credentials.Add(new CredentialProfile
        {
            Name = "Left behind",
            ProtectedPassword = inTheDocument,
        });

        Assert.Equal(Password, Read(protection, document.Credentials[0]));
        Assert.Equal(Password, Read(protection, document.Credentials[1]));
    }

    [Fact]
    public void An_entry_the_store_no_longer_has_is_missing_rather_than_unreadable()
    {
        // Different sentences and different places to go and look. Unreadable
        // means the secret is there and shut; this means it is not there.
        (ExternalStore store, DocumentProtection protection, ConnectionDocument document) = Outside();
        CredentialVault vault = new(protection);
        CredentialProfile profile = CredentialOperations.Add(document, "Admin");

        vault.SavePassword(profile, Secret.From(Password));
        store.Empty();

        SecretUnprotectResult opened = protection.Unprotect(profile.ProtectedPassword);

        Assert.Equal(SecretUnprotectStatus.Missing, opened.Status);
        Assert.True(opened.ShouldPreserveStoredValue);
        Assert.Contains("Credential Manager", opened.Notice, StringComparison.Ordinal);
    }

    [Fact]
    public void A_reference_somebody_has_edited_by_hand_is_unreadable_not_missing()
    {
        (_, DocumentProtection protection, _) = Outside();

        string edited = SecretEnvelope.Create(ExternalStore.Name, [0x01, 0x02, 0x03]).ToString();

        Assert.Equal(SecretUnprotectStatus.Unreadable, protection.Unprotect(edited).Status);
    }

    // ── Entries nothing refers to ───────────────────────────────────────

    [Fact]
    public void An_entry_nothing_refers_to_is_swept_and_one_in_use_is_not()
    {
        (ExternalStore store, DocumentProtection protection, ConnectionDocument document) = Outside();
        CredentialVault vault = new(protection);

        CredentialProfile kept = CredentialOperations.Add(document, "Kept");
        vault.SavePassword(kept, Secret.From(Password));

        // An entry written and then abandoned without being released — a crash
        // between saving the password and saving the document.
        store.Strand();

        Assert.Equal(2, protection.ExternalSecretCount);
        Assert.Equal(1, protection.ForgetOrphanedSecrets(document));
        Assert.Equal(1, protection.ExternalSecretCount);
        Assert.Equal(Password, Read(protection, kept));
    }

    [Fact]
    public void Sweeping_again_finds_nothing_left_to_do()
    {
        (ExternalStore store, DocumentProtection protection, ConnectionDocument document) = Outside();

        store.Strand();
        protection.ForgetOrphanedSecrets(document);

        Assert.Equal(0, protection.ForgetOrphanedSecrets(document));
    }

    [Fact]
    public void A_sweep_never_sees_past_the_document_it_was_opened_against()
    {
        // The one that matters. Patchbay opens one document at a time and a
        // person may have several, all filing entries in the same Windows
        // store. A sweep that deleted every entry the open document did not
        // name would delete the other document's passwords, silently, while
        // tidying up.
        ExternalStore store = new();

        ConnectionDocument theirs = new();
        using (DocumentProtection other = new(store))
        {
            other.Open(theirs);

            CredentialVault vault = new(other);
            vault.SavePassword(CredentialOperations.Add(theirs, "Theirs"), Secret.From(Password));
        }

        ConnectionDocument mine = new();
        using DocumentProtection protection = new(store);
        protection.Open(mine);

        Assert.Equal(0, protection.ExternalSecretCount);
        Assert.Equal(0, protection.ForgetOrphanedSecrets(mine));
        Assert.Equal(1, store.Held);
    }

    // ── With a master password over the top ─────────────────────────────

    [Fact]
    public void Turning_on_a_master_password_takes_the_passwords_out_of_the_store()
    {
        (ExternalStore store, _, DocumentProtection protection, ConnectionDocument document) =
            Both(passwords: 2);

        protection.UseMachineStore(document, ExternalStore.Name);
        MasterPasswordChange result = protection.Set(document, Master);

        Assert.Equal(MasterPasswordChangeStatus.Protected, result.Status);
        Assert.Equal(2, result.Moved);

        // Both halves. The passwords are behind the master password, and the
        // entries they used to live in are gone rather than left in Windows
        // holding a copy of everything the document just protected.
        Assert.Equal(0, store.Held);
        Assert.All(document.Credentials, profile => Assert.Equal(Password, Read(protection, profile)));
    }

    [Fact]
    public void Removing_it_puts_them_back_in_the_store_the_document_chose()
    {
        (ExternalStore store, _, DocumentProtection protection, ConnectionDocument document) =
            Both(passwords: 2);

        protection.UseMachineStore(document, ExternalStore.Name);
        protection.Set(document, Master);
        MasterPasswordChange result = protection.Remove(document, Master);

        Assert.Equal(MasterPasswordChangeStatus.Unprotected, result.Status);
        Assert.Equal(2, store.Held);
        Assert.All(document.Credentials, profile => Assert.Equal(Password, Read(protection, profile)));
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    /// <summary>A document already keeping its passwords outside itself.</summary>
    private static (ExternalStore Store, DocumentProtection Protection, ConnectionDocument Document) Outside()
    {
        ExternalStore store = new();
        DocumentProtection protection = new(store);
        ConnectionDocument document = new();

        protection.Open(document);

        return (store, protection, document);
    }

    /// <summary>
    /// A document with both stores available, keeping <paramref name="passwords"/>
    /// saved sign-ins in the machine one.
    /// </summary>
    private static (ExternalStore Store, MachineStore Machine, DocumentProtection Protection, ConnectionDocument Document)
        Both(int passwords)
    {
        ExternalStore store = new();
        MachineStore machine = new();
        DocumentProtection protection = new(machine, store);
        ConnectionDocument document = new();

        protection.Open(document);

        CredentialVault vault = new(protection);

        for (int i = 0; i < passwords; i++)
        {
            vault.SavePassword(CredentialOperations.Add(document, $"account {i}"), Secret.From(Password));
        }

        return (store, machine, protection, document);
    }

    private static string? Read(DocumentProtection protection, CredentialProfile profile)
    {
        SecretUnprotectResult opened = protection.Unprotect(profile.ProtectedPassword);

        if (!opened.IsSuccess || opened.Secret is not { } secret)
        {
            return null;
        }

        using (secret)
        {
            return secret.RevealAsString();
        }
    }

    /// <summary>
    /// Stands in for the machine's own store, which keeps the ciphertext in
    /// the document. It reverses the bytes, which is not encryption and does
    /// not need to be: what is under test is the routing above it.
    /// </summary>
    private sealed class MachineStore : SecretProtector
    {
        public const string Name = "machine";

        /// <summary>
        /// A blob in this scheme that this store will not open, standing for
        /// one written by another Windows account.
        /// </summary>
        public static string SomebodyElses { get; } =
            SecretEnvelope.Create(Name, [0xFF, 0x01, 0x02, 0x03]).ToString();

        public override string Scheme => Name;

        protected override byte[] ProtectCore(ReadOnlySpan<byte> utf8)
        {
            byte[] bytes = utf8.ToArray();
            Array.Reverse(bytes);
            return bytes;
        }

        protected override SecretUnprotectResult UnprotectCore(ReadOnlySpan<byte> payload)
        {
            if (payload[0] == 0xFF)
            {
                return SecretUnprotectResult.Failed(SecretUnprotectStatus.Unreadable);
            }

            byte[] bytes = payload.ToArray();
            Array.Reverse(bytes);
            return SecretUnprotectResult.Success(Secret.FromUtf8(bytes));
        }
    }

    /// <summary>
    /// Stands in for Windows Credential Manager: it keeps the password and the
    /// document gets a name for it. Entries are filed under a document, which
    /// is the property the sweep depends on and therefore the one worth
    /// modelling rather than glossing over.
    /// </summary>
    private sealed class ExternalStore : SecretProtector, IExternalSecretStore
    {
        public const string Name = "external";

        private readonly Dictionary<(Guid Document, Guid Entry), byte[]> _entries = [];

        private Guid _document;

        /// <summary>Whether this machine can use the store at all.</summary>
        public bool Working { get; set; } = true;

        /// <summary>Whether a write should fail, as one refused by policy would.</summary>
        public bool Refusing { get; set; }

        /// <summary>How many entries the store holds altogether, for any document.</summary>
        public int Held => _entries.Count;

        public override string Scheme => Name;

        public override bool IsAvailable => Working;

        public int Count => _entries.Keys.Count(key => key.Document == _document);

        public void Open(Guid documentId) => _document = documentId;

        /// <summary>Everything gone, as a machine that is not the one that saved them has.</summary>
        public void Empty() => _entries.Clear();

        /// <summary>An entry written and then abandoned without being released.</summary>
        public void Strand() => _entries[(_document, Guid.NewGuid())] = [0x00];

        public int ForgetOrphans(IEnumerable<string?> inUse)
        {
            HashSet<Guid> wanted = [];

            foreach (string? stored in inUse)
            {
                if (SecretEnvelope.TryParse(stored, out SecretEnvelope? envelope)
                    && string.Equals(envelope.Scheme, Name, StringComparison.Ordinal)
                    && envelope.Payload.Length == 16)
                {
                    wanted.Add(new Guid(envelope.Payload.Span));
                }
            }

            List<(Guid Document, Guid Entry)> orphans =
                [.. _entries.Keys.Where(key => key.Document == _document && !wanted.Contains(key.Entry))];

            foreach ((Guid Document, Guid Entry) key in orphans)
            {
                _entries.Remove(key);
            }

            return orphans.Count;
        }

        protected override byte[] ProtectCore(ReadOnlySpan<byte> utf8)
        {
            if (Refusing)
            {
                throw new SecretProtectionException("The store refused.");
            }

            Guid entry = Guid.NewGuid();

            _entries[(_document, entry)] = utf8.ToArray();

            return entry.ToByteArray();
        }

        protected override SecretUnprotectResult UnprotectCore(ReadOnlySpan<byte> payload)
        {
            if (payload.Length != 16)
            {
                return SecretUnprotectResult.Failed(SecretUnprotectStatus.Unreadable);
            }

            return _entries.TryGetValue((_document, new Guid(payload)), out byte[]? bytes)
                ? SecretUnprotectResult.Success(Secret.FromUtf8(bytes))
                : SecretUnprotectResult.Failed(SecretUnprotectStatus.Missing);
        }

        protected override void ForgetCore(ReadOnlySpan<byte> payload)
        {
            if (payload.Length == 16)
            {
                _entries.Remove((_document, new Guid(payload)));
            }
        }
    }
}
