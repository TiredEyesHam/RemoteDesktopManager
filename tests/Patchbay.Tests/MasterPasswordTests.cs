using System.Security.Cryptography;
using Patchbay.Core.Model;
using Patchbay.Core.Security;
using Patchbay.Core.Serialization;

namespace Patchbay.Tests;

/// <summary>
/// An optional document master password (M3-07).
///
/// <para>
/// What this closes is named in the threat model: DPAPI protects a saved
/// password against the file moving and against nothing else, because a local
/// administrator can read another account's store and the signed-in account's
/// own processes are inside the boundary by design. A key derived from
/// something nobody typed into Windows is outside both.
/// </para>
///
/// <para>
/// These tests run at the real iteration count rather than a reduced one. That
/// costs the suite a few seconds and buys the only thing worth having here:
/// the parameters under test are the parameters that ship.
/// </para>
/// </summary>
public class MasterPasswordTests
{
    private const string Master = "correct horse battery staple";
    private const string Password = "hunter2-correct-horse";

    private static byte[] ADocumentKey() =>
        RandomNumberGenerator.GetBytes(MasterKeyRecord.KeyLength);

    // ── Wrapping the document key ───────────────────────────────────────

    [Fact]
    public void The_right_master_password_recovers_the_document_key()
    {
        byte[] key = ADocumentKey();

        MasterKeyRecord record = MasterKeyRecord.Wrap(Master, key);
        MasterKeyResult opened = record.Unwrap(Master);

        Assert.True(opened.IsSuccess);

        using Secret recovered = opened.Key!;

        Assert.Equal(MasterKeyRecord.KeyLength, recovered.Length);
        Assert.Equal(Secret.FromUtf8(key), recovered);
    }

    [Fact]
    public void The_wrong_master_password_says_so_and_nothing_more()
    {
        MasterKeyRecord record = MasterKeyRecord.Wrap(Master, ADocumentKey());

        MasterKeyResult opened = record.Unwrap(Master + "!");

        Assert.Equal(MasterKeyStatus.WrongPassword, opened.Status);
        Assert.Null(opened.Key);
        Assert.True(opened.IsWorthRetrying);
    }

    [Fact]
    public void An_empty_password_is_refused_without_deriving_anything()
    {
        // Nothing ever wrapped a key with one, so the answer is known before
        // six hundred thousand iterations of finding it out.
        MasterKeyRecord record = MasterKeyRecord.Wrap(Master, ADocumentKey());

        Assert.Equal(MasterKeyStatus.WrongPassword, record.Unwrap(string.Empty).Status);
        Assert.Throws<ArgumentException>(() => MasterKeyRecord.Wrap(string.Empty, ADocumentKey()));
    }

    [Fact]
    public void Two_documents_with_the_same_password_share_no_key_material()
    {
        // The salt is what makes one cracking effort useless against the next
        // document, and what stops two files with the same password looking
        // identical to anybody reading them.
        byte[] key = ADocumentKey();

        MasterKeyRecord one = MasterKeyRecord.Wrap(Master, key);
        MasterKeyRecord other = MasterKeyRecord.Wrap(Master, key);

        Assert.NotEqual(one.Salt, other.Salt);
        Assert.NotEqual(one.WrappedKey, other.WrappedKey);
    }

    [Fact]
    public void The_record_holds_the_key_only_in_wrapped_form()
    {
        byte[] key = ADocumentKey();

        MasterKeyRecord record = MasterKeyRecord.Wrap(Master, key);

        byte[] wrapped = Convert.FromBase64String(record.WrappedKey);

        Assert.Equal(
            MasterKeyRecord.NonceLength + MasterKeyRecord.KeyLength + MasterKeyRecord.TagLength,
            wrapped.Length);
        Assert.DoesNotContain(Master, record.WrappedKey, StringComparison.Ordinal);
        Assert.False(Contains(wrapped, key));
    }

    [Fact]
    public void The_iteration_count_is_at_least_the_published_figure()
    {
        // A guard on a number somebody could lower for a faster test suite and
        // never put back. Six hundred thousand is OWASP's for PBKDF2-HMAC-SHA256.
        Assert.True(MasterKeyRecord.DefaultIterations >= 600_000);

        MasterKeyRecord record = MasterKeyRecord.Wrap(Master, ADocumentKey());

        Assert.Equal(MasterKeyRecord.DefaultIterations, record.Iterations);
        Assert.Equal(MasterKeyRecord.SaltLength, Convert.FromBase64String(record.Salt).Length);
        Assert.Equal(MasterKeyRecord.Pbkdf2Sha256, record.Kdf);
    }

    // ── A record that has been got at ───────────────────────────────────

    [Fact]
    public void A_key_derived_by_a_function_this_build_lacks_is_not_damage()
    {
        // The same distinction as SecretUnprotectStatus.TooNew: protected by a
        // newer Patchbay is intact, and telling somebody to restore from
        // backup would be telling them to throw away a working document.
        MasterKeyRecord record = MasterKeyRecord.Wrap(Master, ADocumentKey());
        record.Kdf = "argon2id";

        MasterKeyResult opened = record.Unwrap(Master);

        Assert.Equal(MasterKeyStatus.UnknownKdf, opened.Status);
        Assert.False(opened.IsWorthRetrying);
        Assert.Contains("newer version", opened.Notice, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(MasterKeyRecord.MinimumIterations - 1)]
    [InlineData(MasterKeyRecord.MaximumIterations + 1)]
    public void An_iteration_count_outside_anything_sane_is_refused(int iterations)
    {
        // The ceiling is the one that matters at run time. The count comes out
        // of a file somebody else may have written, and a document asking for
        // two billion iterations is not a document, it is a hang.
        MasterKeyRecord record = MasterKeyRecord.Wrap(Master, ADocumentKey());
        record.Iterations = iterations;

        Assert.Equal(MasterKeyStatus.Damaged, record.Unwrap(Master).Status);
    }

    [Fact]
    public void Editing_the_iteration_count_within_range_fails_the_tag()
    {
        // Indistinguishable from a wrong password, and honestly so: both
        // produce a key that will not open the wrapping. The parameters are
        // authenticated alongside the wrapped key, so this fails even though
        // the count is a plausible one.
        MasterKeyRecord record = MasterKeyRecord.Wrap(Master, ADocumentKey());
        record.Iterations = MasterKeyRecord.DefaultIterations + 1;

        Assert.Equal(MasterKeyStatus.WrongPassword, record.Unwrap(Master).Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not base64 at all")]
    [InlineData("AAAA")]
    public void A_salt_that_is_not_one_is_damage(string salt)
    {
        MasterKeyRecord record = MasterKeyRecord.Wrap(Master, ADocumentKey());
        record.Salt = salt;

        Assert.Equal(MasterKeyStatus.Damaged, record.Unwrap(Master).Status);
    }

    [Fact]
    public void A_truncated_wrapped_key_is_damage_rather_than_a_wrong_password()
    {
        MasterKeyRecord record = MasterKeyRecord.Wrap(Master, ADocumentKey());

        byte[] wrapped = Convert.FromBase64String(record.WrappedKey);
        record.WrappedKey = Convert.ToBase64String(wrapped.AsSpan(0, wrapped.Length - 1));

        MasterKeyResult opened = record.Unwrap(Master);

        Assert.Equal(MasterKeyStatus.Damaged, opened.Status);
        Assert.Contains("backup", opened.Notice, StringComparison.Ordinal);
    }

    [Fact]
    public void A_flipped_bit_in_the_wrapped_key_is_caught()
    {
        MasterKeyRecord record = MasterKeyRecord.Wrap(Master, ADocumentKey());

        byte[] wrapped = Convert.FromBase64String(record.WrappedKey);
        wrapped[MasterKeyRecord.NonceLength] ^= 0x01;
        record.WrappedKey = Convert.ToBase64String(wrapped);

        Assert.Equal(MasterKeyStatus.WrongPassword, record.Unwrap(Master).Status);
    }

    // ── Encrypting a password under the document key ────────────────────

    [Fact]
    public void A_password_survives_a_round_trip_through_the_document_key()
    {
        using MasterPasswordProtector protector = Unlocked(ADocumentKey());

        using Secret secret = Secret.From(Password);
        string stored = protector.Protect(secret);

        Assert.StartsWith("pb1:master:", stored, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, stored, StringComparison.Ordinal);

        SecretUnprotectResult opened = protector.Unprotect(stored);

        Assert.True(opened.IsSuccess);

        using Secret recovered = opened.Secret!;

        Assert.Equal(Password, recovered.RevealAsString());
    }

    [Fact]
    public void The_same_password_twice_encrypts_to_something_different()
    {
        // A fresh nonce per secret. The alternative leaks the relationship
        // between two passwords and the authentication key with it, and it
        // would also tell anybody reading the file which two accounts share a
        // password.
        using MasterPasswordProtector protector = Unlocked(ADocumentKey());
        using Secret secret = Secret.From(Password);

        Assert.NotEqual(protector.Protect(secret), protector.Protect(secret));
    }

    [Fact]
    public void A_password_encrypted_under_another_document_key_will_not_open()
    {
        using MasterPasswordProtector theirs = Unlocked(ADocumentKey());
        using MasterPasswordProtector mine = Unlocked(ADocumentKey());

        using Secret secret = Secret.From(Password);

        Assert.Equal(
            SecretUnprotectStatus.Unreadable,
            mine.Unprotect(theirs.Protect(secret)).Status);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    [InlineData(-1)]
    public void A_blob_that_has_been_edited_will_not_open(int offset)
    {
        using MasterPasswordProtector protector = Unlocked(ADocumentKey());
        using Secret secret = Secret.From(Password);

        Assert.True(SecretEnvelope.TryParse(protector.Protect(secret), out SecretEnvelope? envelope));

        byte[] payload = envelope!.Payload.ToArray();
        payload[offset < 0 ? payload.Length + offset : offset] ^= 0x01;

        string edited = SecretEnvelope.Create(MasterPasswordProtector.SchemeName, payload).ToString();

        Assert.Equal(SecretUnprotectStatus.Unreadable, protector.Unprotect(edited).Status);
    }

    [Fact]
    public void A_payload_too_short_to_be_one_is_refused_rather_than_indexed_into()
    {
        using MasterPasswordProtector protector = Unlocked(ADocumentKey());

        string stub = SecretEnvelope.Create(MasterPasswordProtector.SchemeName, [1, 2, 3]).ToString();

        Assert.Equal(SecretUnprotectStatus.Unreadable, protector.Unprotect(stub).Status);
    }

    [Fact]
    public void A_locked_protector_will_not_save_and_says_which_kind_of_stuck_it_is()
    {
        using MasterPasswordProtector protector = new();

        Assert.False(protector.IsAvailable);

        SecretProtectionException refused = Assert.Throws<SecretProtectionException>(
            () => protector.Protect(Secret.From(Password)));

        Assert.Contains("master password", refused.Message, StringComparison.Ordinal);

        // Not Unavailable, which would send somebody off to investigate their
        // machine's data protection over a document that needs one keystroke.
        Assert.Equal(
            SecretUnprotectStatus.Locked,
            protector.Unprotect("pb1:master:AAAAAAAAAAAAAAAAAAAA").Status);
    }

    [Fact]
    public void Locking_gives_the_key_up()
    {
        MasterPasswordProtector protector = Unlocked(ADocumentKey());

        Assert.True(protector.IsAvailable);

        protector.Lock();

        Assert.False(protector.IsAvailable);

        protector.Lock();

        Assert.False(protector.IsAvailable);
    }

    [Fact]
    public void A_document_key_of_the_wrong_size_is_refused()
    {
        using MasterPasswordProtector protector = new();

        Assert.Throws<ArgumentException>(() => protector.Unlock(Secret.From("too short")));
    }

    // ── Turning it on ───────────────────────────────────────────────────

    [Fact]
    public void Setting_a_master_password_moves_every_saved_password_behind_it()
    {
        MachineStore machine = new();
        using DocumentProtection protection = new(machine);

        ConnectionDocument document = ADocumentWith(machine, 3);

        MasterPasswordChange change = protection.Set(document, Master);

        Assert.True(change.IsSuccess);
        Assert.Equal(3, change.Moved);
        Assert.Equal(0, change.LeftAlone);
        Assert.NotNull(document.MasterKey);

        Assert.All(
            document.Credentials,
            profile => Assert.StartsWith("pb1:master:", profile.ProtectedPassword!, StringComparison.Ordinal));

        // And they still read back as themselves.
        for (int i = 0; i < 3; i++)
        {
            Assert.Equal($"{Password}-{i}", Read(protection, document.Credentials[i]));
        }
    }

    [Fact]
    public void A_password_this_account_cannot_read_is_left_exactly_where_it_is()
    {
        // M3-01's rule, and the reason the change reports counts. A blob this
        // Windows account cannot open is very likely one another account can,
        // so it is not re-encrypted, not dropped, and not hidden from the
        // person who just turned a master password on believing it covered
        // everything.
        MachineStore machine = new();
        using DocumentProtection protection = new(machine);

        ConnectionDocument document = ADocumentWith(machine, 2);
        document.Credentials.Add(new CredentialProfile
        {
            Name = "somebody else's",
            ProtectedPassword = MachineStore.SomebodyElses,
        });

        MasterPasswordChange change = protection.Set(document, Master);

        Assert.Equal(2, change.Moved);
        Assert.Equal(1, change.LeftAlone);
        Assert.Equal(MachineStore.SomebodyElses, document.Credentials[2].ProtectedPassword);
        Assert.Contains("could not be read", change.Notice, StringComparison.Ordinal);
    }

    [Fact]
    public void A_master_password_shorter_than_the_floor_changes_nothing()
    {
        MachineStore machine = new();
        using DocumentProtection protection = new(machine);

        ConnectionDocument document = ADocumentWith(machine, 1);
        string before = document.Credentials[0].ProtectedPassword!;

        MasterPasswordChange change = protection.Set(document, "short");

        Assert.Equal(MasterPasswordChangeStatus.PasswordTooShort, change.Status);
        Assert.False(change.IsSuccess);
        Assert.Null(document.MasterKey);
        Assert.Equal(before, document.Credentials[0].ProtectedPassword);
        Assert.False(protection.IsProtected);
    }

    [Fact]
    public void Setting_one_on_a_document_that_has_one_is_a_bug_rather_than_an_outcome()
    {
        using DocumentProtection protection = new(new MachineStore());

        ConnectionDocument document = new();
        protection.Set(document, Master);

        Assert.Throws<InvalidOperationException>(() => protection.Set(document, "another one"));
    }

    [Fact]
    public void A_document_with_no_saved_passwords_can_still_have_a_master_password()
    {
        using DocumentProtection protection = new(new MachineStore());

        ConnectionDocument document = new();

        MasterPasswordChange change = protection.Set(document, Master);

        Assert.True(change.IsSuccess);
        Assert.Equal(0, change.Moved);
        Assert.True(protection.IsProtected);
        Assert.True(protection.IsUnlocked);
    }

    [Fact]
    public void A_master_password_works_where_the_machine_has_no_protection_at_all()
    {
        // The case that makes this more than a second way to do DPAPI: an
        // account with no working data protection cannot save a password at
        // all until now.
        using DocumentProtection protection = new(UnavailableSecretProtector.Instance);

        Assert.False(protection.IsAvailable);

        ConnectionDocument document = new();

        Assert.True(protection.Set(document, Master).IsSuccess);
        Assert.True(protection.IsAvailable);

        using Secret secret = Secret.From(Password);
        CredentialProfile profile = new() { ProtectedPassword = protection.Protect(secret) };

        Assert.Equal(Password, Read(protection, profile));
    }

    // ── Locking and unlocking ───────────────────────────────────────────

    [Fact]
    public void Opening_a_protected_document_leaves_it_locked()
    {
        MachineStore machine = new();
        ConnectionDocument document = Protected(machine, 2);

        using DocumentProtection reopened = new(machine);
        reopened.Open(document);

        Assert.True(reopened.IsProtected);
        Assert.False(reopened.IsUnlocked);
        Assert.True(reopened.NeedsUnlocking);

        SecretUnprotectResult opened = reopened.Unprotect(document.Credentials[0].ProtectedPassword);

        Assert.Equal(SecretUnprotectStatus.Locked, opened.Status);
        Assert.True(opened.ShouldPreserveStoredValue);
    }

    [Fact]
    public void The_master_password_opens_it_and_the_wrong_one_does_not()
    {
        MachineStore machine = new();
        ConnectionDocument document = Protected(machine, 1);

        using DocumentProtection reopened = new(machine);
        reopened.Open(document);

        Assert.Equal(MasterKeyStatus.WrongPassword, reopened.Unlock("not it"));
        Assert.False(reopened.IsUnlocked);

        Assert.Equal(MasterKeyStatus.Unlocked, reopened.Unlock(Master));
        Assert.True(reopened.IsUnlocked);
        Assert.Equal($"{Password}-0", Read(reopened, document.Credentials[0]));
    }

    [Fact]
    public void A_locked_document_refuses_to_save_rather_than_quietly_using_the_machine()
    {
        // The failure worth designing against. Falling back to DPAPI here
        // would look like success and would silently undo the protection
        // somebody deliberately turned on.
        MachineStore machine = new();
        ConnectionDocument document = Protected(machine, 1);

        using DocumentProtection reopened = new(machine);
        reopened.Open(document);

        Assert.True(machine.IsAvailable);
        Assert.False(reopened.IsAvailable);

        SecretProtectionException refused = Assert.Throws<SecretProtectionException>(
            () => reopened.Protect(Secret.From(Password)));

        Assert.Contains("master password", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Locking_again_puts_the_passwords_back_out_of_reach()
    {
        MachineStore machine = new();
        ConnectionDocument document = Protected(machine, 1);

        using DocumentProtection protection = new(machine);
        protection.Open(document);
        protection.Unlock(Master);

        Assert.NotNull(Read(protection, document.Credentials[0]));

        protection.Lock();

        Assert.True(protection.IsProtected);
        Assert.True(protection.NeedsUnlocking);
        Assert.Null(Read(protection, document.Credentials[0]));
    }

    [Fact]
    public void Opening_a_second_document_does_not_leave_the_first_ones_key_behind()
    {
        MachineStore machine = new();
        ConnectionDocument first = Protected(machine, 1);

        using DocumentProtection protection = new(machine);
        protection.Open(first);
        protection.Unlock(Master);

        Assert.True(protection.IsUnlocked);

        protection.Open(new ConnectionDocument());

        Assert.False(protection.IsProtected);
        Assert.False(protection.IsUnlocked);
    }

    [Fact]
    public void Unlocking_a_document_that_has_no_master_password_says_so()
    {
        using DocumentProtection protection = new(new MachineStore());
        protection.Open(new ConnectionDocument());

        Assert.Equal(MasterKeyStatus.NotProtected, protection.Unlock(Master));
    }

    // ── Changing it ─────────────────────────────────────────────────────

    [Fact]
    public void Changing_the_master_password_moves_no_saved_passwords()
    {
        // The whole reason there are two keys. A document with three hundred
        // saved passwords changes its master password by rewrapping thirty-two
        // bytes, and a crash cannot leave half of them under each password.
        MachineStore machine = new();
        using DocumentProtection protection = new(machine);

        ConnectionDocument document = ADocumentWith(machine, 3);
        protection.Set(document, Master);

        string[] before = [.. document.Credentials.Select(c => c.ProtectedPassword!)];

        MasterPasswordChange change = protection.Change(document, Master, "a whole new master");

        Assert.Equal(MasterPasswordChangeStatus.Changed, change.Status);
        Assert.Equal(0, change.Moved);
        Assert.Equal(before, document.Credentials.Select(c => c.ProtectedPassword));

        // The new password opens it and the old one no longer does.
        using DocumentProtection reopened = new(machine);
        reopened.Open(document);

        Assert.Equal(MasterKeyStatus.WrongPassword, reopened.Unlock(Master));
        Assert.Equal(MasterKeyStatus.Unlocked, reopened.Unlock("a whole new master"));
        Assert.Equal($"{Password}-1", Read(reopened, document.Credentials[1]));
    }

    [Fact]
    public void Changing_it_is_asked_for_the_current_one_even_when_already_unlocked()
    {
        // Somebody who walked up to an unlocked screen should not be able to
        // change the master password without knowing it.
        MachineStore machine = new();
        using DocumentProtection protection = new(machine);

        ConnectionDocument document = ADocumentWith(machine, 1);
        protection.Set(document, Master);

        Assert.True(protection.IsUnlocked);

        MasterKeyRecord before = document.MasterKey!;
        MasterPasswordChange change = protection.Change(document, "not the master", "a whole new master");

        Assert.Equal(MasterPasswordChangeStatus.WrongPassword, change.Status);
        Assert.Same(before, document.MasterKey);
        Assert.Equal(MasterKeyStatus.Unlocked, protection.Unlock(Master));
    }

    [Fact]
    public void A_replacement_shorter_than_the_floor_is_refused_before_anything_is_derived()
    {
        MachineStore machine = new();
        using DocumentProtection protection = new(machine);

        ConnectionDocument document = new();
        protection.Set(document, Master);

        MasterKeyRecord before = document.MasterKey!;

        Assert.Equal(
            MasterPasswordChangeStatus.PasswordTooShort,
            protection.Change(document, Master, "short").Status);
        Assert.Same(before, document.MasterKey);
    }

    [Fact]
    public void Changing_one_that_does_not_exist_is_a_bug_rather_than_an_outcome()
    {
        using DocumentProtection protection = new(new MachineStore());

        Assert.Throws<InvalidOperationException>(
            () => protection.Change(new ConnectionDocument(), Master, "a whole new master"));
    }

    // ── Turning it off ──────────────────────────────────────────────────

    [Fact]
    public void Removing_it_puts_the_saved_passwords_back_into_machine_protection()
    {
        MachineStore machine = new();
        using DocumentProtection protection = new(machine);

        ConnectionDocument document = ADocumentWith(machine, 2);
        protection.Set(document, Master);

        MasterPasswordChange change = protection.Remove(document, Master);

        Assert.Equal(MasterPasswordChangeStatus.Unprotected, change.Status);
        Assert.Equal(2, change.Moved);
        Assert.Null(document.MasterKey);
        Assert.False(protection.IsProtected);
        Assert.False(protection.IsUnlocked);

        Assert.All(
            document.Credentials,
            profile => Assert.StartsWith(
                $"pb1:{MachineStore.Name}:", profile.ProtectedPassword!, StringComparison.Ordinal));
        Assert.Equal($"{Password}-0", Read(protection, document.Credentials[0]));
    }

    [Fact]
    public void Removing_it_needs_the_master_password_even_when_unlocked()
    {
        MachineStore machine = new();
        using DocumentProtection protection = new(machine);

        ConnectionDocument document = ADocumentWith(machine, 1);
        protection.Set(document, Master);

        MasterPasswordChange change = protection.Remove(document, "not the master");

        Assert.Equal(MasterPasswordChangeStatus.WrongPassword, change.Status);
        Assert.NotNull(document.MasterKey);
        Assert.True(protection.IsProtected);
    }

    [Fact]
    public void Removing_it_is_refused_when_there_is_nowhere_to_put_the_passwords()
    {
        // The alternatives are losing them or writing them in the clear, and
        // M3-02 already settled that argument.
        using DocumentProtection protection = new(UnavailableSecretProtector.Instance);

        ConnectionDocument document = new();
        protection.Set(document, Master);

        using Secret secret = Secret.From(Password);
        document.Credentials.Add(new CredentialProfile { ProtectedPassword = protection.Protect(secret) });

        MasterPasswordChange change = protection.Remove(document, Master);

        Assert.Equal(MasterPasswordChangeStatus.NowhereToPutPasswords, change.Status);
        Assert.NotNull(document.MasterKey);
        Assert.Equal(Password, Read(protection, document.Credentials[0]));
    }

    [Fact]
    public void Removing_it_from_a_document_with_nothing_saved_is_allowed_anywhere()
    {
        // Nothing has to move, so there is no reason to need somewhere to put
        // it. Refusing here would strand a document on a machine with no data
        // protection.
        using DocumentProtection protection = new(UnavailableSecretProtector.Instance);

        ConnectionDocument document = new();
        protection.Set(document, Master);

        Assert.Equal(MasterPasswordChangeStatus.Unprotected, protection.Remove(document, Master).Status);
        Assert.Null(document.MasterKey);
    }

    // ── Reading a document that holds more than one scheme ───────────────

    [Fact]
    public void A_blob_is_read_by_whichever_scheme_wrote_it()
    {
        MachineStore machine = new();
        using DocumentProtection protection = new(machine);

        using Secret secret = Secret.From(Password);
        CredentialProfile machineOne = new() { ProtectedPassword = machine.Protect(secret) };

        ConnectionDocument document = new();
        document.Credentials.Add(machineOne);
        protection.Set(document, Master);

        // Now add one back in the machine's scheme, as a colleague's copy of
        // the document would arrive.
        CredentialProfile theirs = new() { ProtectedPassword = machine.Protect(secret) };
        document.Credentials.Add(theirs);

        Assert.Equal(Password, Read(protection, document.Credentials[0]));
        Assert.Equal(Password, Read(protection, theirs));
    }

    [Fact]
    public void A_scheme_nothing_here_answers_to_is_left_alone()
    {
        using DocumentProtection protection = new(new MachineStore());

        string elsewhere = SecretEnvelope.Create("credman", [1, 2, 3, 4]).ToString();

        SecretUnprotectResult opened = protection.Unprotect(elsewhere);

        Assert.Equal(SecretUnprotectStatus.WrongScheme, opened.Status);
        Assert.True(opened.ShouldPreserveStoredValue);
    }

    [Fact]
    public void A_document_written_by_a_newer_envelope_is_refused_politely()
    {
        using DocumentProtection protection = new(new MachineStore());

        Assert.Equal(
            SecretUnprotectStatus.TooNew,
            protection.Unprotect("pb9:master:AAAAAAAAAAAAAAAAAAAA").Status);
        Assert.Equal(
            SecretUnprotectStatus.NotASecret,
            protection.Unprotect("just a password someone typed").Status);
    }

    [Fact]
    public void The_scheme_it_writes_with_follows_whether_the_document_is_protected()
    {
        MachineStore machine = new();
        using DocumentProtection protection = new(machine);

        Assert.Equal(MachineStore.Name, protection.Scheme);

        ConnectionDocument document = new();
        protection.Set(document, Master);

        Assert.Equal(MasterPasswordProtector.SchemeName, protection.Scheme);

        protection.Remove(document, Master);

        Assert.Equal(MachineStore.Name, protection.Scheme);
    }

    // ── Through the file ────────────────────────────────────────────────

    [Fact]
    public void A_protected_document_survives_a_round_trip_and_still_opens()
    {
        MachineStore machine = new();
        ConnectionDocument saved = Protected(machine, 2);

        string json = ConnectionDocumentSerializer.Serialize(saved);
        ConnectionDocument loaded = ConnectionDocumentSerializer.Deserialize(json);

        Assert.NotNull(loaded.MasterKey);
        Assert.Equal(saved.MasterKey!.Salt, loaded.MasterKey!.Salt);
        Assert.Equal(saved.MasterKey.WrappedKey, loaded.MasterKey.WrappedKey);
        Assert.Equal(MasterKeyRecord.Pbkdf2Sha256, loaded.MasterKey.Kdf);

        using DocumentProtection protection = new(machine);
        protection.Open(loaded);

        Assert.Equal(MasterKeyStatus.Unlocked, protection.Unlock(Master));
        Assert.Equal($"{Password}-1", Read(protection, loaded.Credentials[1]));
    }

    [Fact]
    public void The_file_holds_neither_the_passwords_nor_the_master_password()
    {
        ConnectionDocument document = Protected(new MachineStore(), 2);

        string json = ConnectionDocumentSerializer.Serialize(document);

        Assert.DoesNotContain(Master, json, StringComparison.Ordinal);
        Assert.DoesNotContain($"{Password}-0", json, StringComparison.Ordinal);
        Assert.DoesNotContain($"{Password}-1", json, StringComparison.Ordinal);

        // What it does hold, and is meant to.
        Assert.Contains("masterKey", json, StringComparison.Ordinal);
        Assert.Contains(MasterKeyRecord.Pbkdf2Sha256, json, StringComparison.Ordinal);
    }

    [Fact]
    public void A_protected_document_is_stamped_with_the_schema_version_that_knows_about_it()
    {
        // The bump is the guard. A build that has never heard of masterKey
        // would drop the field on load and lose it on the next save, taking
        // every password in the document with it, so it must refuse to open
        // the file at all.
        ConnectionDocument document = Protected(new MachineStore(), 1);

        Assert.Equal(2, document.SchemaVersion);
        Assert.Equal(2, SchemaMigrator.ReadVersion(ConnectionDocumentSerializer.Serialize(document)));
    }

    // ── Nothing prints a key ────────────────────────────────────────────

    [Fact]
    public void Nothing_about_a_result_prints_the_key()
    {
        MasterKeyRecord record = MasterKeyRecord.Wrap(Master, ADocumentKey());
        MasterKeyResult opened = record.Unwrap(Master);

        using Secret key = opened.Key!;

        Assert.Equal("MasterKeyResult { Status = Unlocked }", opened.ToString());
        Assert.DoesNotContain(Master, opened.ToString(), StringComparison.Ordinal);
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private static MasterPasswordProtector Unlocked(byte[] key)
    {
        MasterPasswordProtector protector = new();
        protector.Unlock(Secret.FromUtf8(key));
        return protector;
    }

    /// <summary>A document holding <paramref name="passwords"/> saved sign-ins.</summary>
    private static ConnectionDocument ADocumentWith(ISecretProtector protector, int passwords)
    {
        ConnectionDocument document = new();

        for (int i = 0; i < passwords; i++)
        {
            using Secret secret = Secret.From($"{Password}-{i}");

            document.Credentials.Add(new CredentialProfile
            {
                Name = $"account {i}",
                UserName = $"user{i}",
                ProtectedPassword = protector.Protect(secret),
            });
        }

        return document;
    }

    /// <summary>The same, already behind a master password and closed again.</summary>
    private static ConnectionDocument Protected(ISecretProtector machine, int passwords)
    {
        ConnectionDocument document = ADocumentWith(machine, passwords);

        using DocumentProtection protection = new(machine);
        protection.Set(document, Master);

        return document;
    }

    private static string? Read(DocumentProtection protector, CredentialProfile profile)
    {
        SecretUnprotectResult opened = protector.Unprotect(profile.ProtectedPassword);

        if (!opened.IsSuccess || opened.Secret is not { } secret)
        {
            return null;
        }

        using (secret)
        {
            return secret.RevealAsString();
        }
    }

    private static bool Contains(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle) =>
        haystack.IndexOf(needle) >= 0;

    /// <summary>
    /// Stands in for the machine's own store. It reverses the bytes, which is
    /// not protection and is not meant to be: what is under test is the
    /// routing and the re-protection around it.
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
}
