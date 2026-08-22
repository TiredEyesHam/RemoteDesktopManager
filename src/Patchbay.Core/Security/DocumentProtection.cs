using System.Security.Cryptography;
using Patchbay.Core.Model;

namespace Patchbay.Core.Security;

/// <summary>
/// A document's protection, whichever it is using (M3-07, M3-04). One of these
/// per open document, and it is the <see cref="ISecretProtector"/> everything
/// else holds — <see cref="CredentialVault"/> included, which goes on not
/// knowing which store is behind it.
///
/// <para>
/// <b>Three choices, not two.</b> A document keeps its saved passwords in
/// Windows data protection, in Windows Credential Manager, or behind its own
/// master password. The first two are machine stores and interchangeable from
/// here; the third is not a store on this machine at all, which is the whole
/// of what it buys and the whole of what it costs. Choosing between the
/// machine stores is <see cref="UseMachineStore"/>; the master password sits
/// over whichever is chosen and takes precedence while it is on.
/// </para>
///
/// <para>
/// <b>Why routing exists at all.</b> A document holds blobs from more than one
/// scheme as a matter of course, not as a transitional state.
/// <see cref="SecretEnvelope"/> was built for that on day one and this is the
/// thing that uses it. Turning on a master password re-protects every saved
/// password it can read — and cannot read one saved by a different Windows
/// account, which <c>M3-01</c> requires be left exactly where it is. So the
/// document ends up genuinely mixed, permanently, and the reader has to
/// dispatch on what each blob says it is rather than on what the document is
/// set to.
/// </para>
///
/// <para>
/// <b>Writing is not routed.</b> Reads dispatch by scheme; writes go to one
/// place. A document with a master password writes with it or refuses, and
/// never quietly falls back to machine protection — which would be the
/// invisible failure <c>M3-02</c> is about, with the difference that here it
/// would also be a silent downgrade of the protection the person deliberately
/// turned on.
/// </para>
///
/// <para>
/// <b>Locked is a state, not an error.</b> A document with a master password
/// nobody has typed yet opens, shows its tree, and connects to everything that
/// does not need a saved password. Only the passwords are out of reach, and
/// they report <see cref="SecretUnprotectStatus.Locked"/>, which says what to
/// do about it.
/// </para>
/// </summary>
public sealed class DocumentProtection : ISecretProtector, IDisposable
{
    /// <summary>
    /// The shortest master password Patchbay will set.
    ///
    /// <para>
    /// A floor rather than a policy: no character classes, no expiry, no
    /// rules about punctuation, all of which push people towards passwords
    /// that are harder to remember and no harder to guess. What it stops is
    /// the case where somebody protects a document with three characters and
    /// believes the six hundred thousand iterations behind it are doing
    /// something.
    /// </para>
    /// </summary>
    public const int MinimumPasswordLength = 8;

    private readonly ISecretProtector[] _stores;
    private readonly MasterPasswordProtector _master = new();

    private ISecretProtector _machine;
    private MasterKeyRecord? _record;

    /// <param name="machineStores">
    /// The machine-held stores this build has, in the order they should be
    /// offered — Windows data protection and Windows Credential Manager in the
    /// application (M3-02, M3-04), and none in a test. The first is what a
    /// document that has never said otherwise uses.
    ///
    /// <para>
    /// Given none, everything falls to the protector that refuses, so
    /// <c>Core</c> can be exercised without a platform under it.
    /// </para>
    /// </param>
    public DocumentProtection(params ISecretProtector[]? machineStores)
    {
        _stores = machineStores is { Length: > 0 }
            ? [.. machineStores]
            : [UnavailableSecretProtector.Instance];

        _machine = _stores[0];
    }

    /// <summary>Whether this document has a master password at all.</summary>
    public bool IsProtected => _record is not null;

    /// <summary>Whether the document key is in hand.</summary>
    public bool IsUnlocked => _master.IsAvailable;

    /// <summary>
    /// Whether somebody needs to type the master password before the saved
    /// passwords are usable. The one question the shell asks on opening a
    /// document.
    /// </summary>
    public bool NeedsUnlocking => IsProtected && !IsUnlocked;

    /// <summary>
    /// Whether the machine can protect a secret by itself — which decides
    /// whether a master password can be removed, and whether a document
    /// without one can save passwords at all.
    /// </summary>
    public bool CanUseMachineProtection => _machine.IsAvailable;

    /// <inheritdoc />
    public string Scheme => IsProtected ? MasterPasswordProtector.SchemeName : _machine.Scheme;

    /// <inheritdoc />
    public bool IsAvailable => IsProtected ? IsUnlocked : _machine.IsAvailable;

    /// <summary>
    /// Which machine stores this build has, in the order to offer them. The
    /// panel reads <see cref="ISecretProtector.Scheme"/> and
    /// <see cref="ISecretProtector.IsAvailable"/> off these; it does not need
    /// to know what any of them is.
    /// </summary>
    public IReadOnlyList<ISecretProtector> MachineStores => _stores;

    /// <summary>
    /// Which machine store new passwords go to when there is no master
    /// password — and where they would go if one were removed.
    /// </summary>
    public string MachineStoreScheme => _machine.Scheme;

    /// <summary>
    /// Whether this document names a store this build does not have (M3-04) —
    /// a file written by a later Patchbay, or by one built with a store this
    /// one was not. Nothing can be saved until another is chosen, and the
    /// difference between that and a broken machine is worth a sentence.
    /// </summary>
    public bool NamesAnUnknownStore { get; private set; }

    /// <summary>
    /// Whether this document's saved passwords are kept outside the file
    /// (M3-04). What it changes for a caller is what a copy of the document
    /// contains: with an external store, nothing.
    /// </summary>
    public bool UsesExternalStore => _machine is IExternalSecretStore;

    /// <summary>
    /// How many entries the machine's external stores are holding for this
    /// document (M3-04), referred to or not. Zero for a document that keeps
    /// its passwords in itself, which is every document until somebody says
    /// otherwise.
    /// </summary>
    public int ExternalSecretCount => _stores.OfType<IExternalSecretStore>().Sum(store => store.Count);

    /// <summary>
    /// Adopts a freshly loaded document's master key, locked, and its choice
    /// of machine store. Any previous document's key is erased first, because
    /// two documents open in succession must not share one.
    /// </summary>
    public void Open(ConnectionDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        Lock();
        _record = document.MasterKey;

        // A document that says nothing gets the first store offered. One that
        // names a store this build does not have gets none — not the first
        // one, which would quietly write the next password somewhere other
        // than where somebody chose (M3-02) and would look exactly like it
        // worked.
        _machine = document.CredentialStore is null
            ? _stores[0]
            : Find(document.CredentialStore) ?? UnavailableSecretProtector.Instance;

        NamesAnUnknownStore =
            document.CredentialStore is not null && Find(document.CredentialStore) is null;

        // Every entry written from here on belongs to this document, and a
        // sweep will not see past it. Patchbay opens one document at a time
        // and a person may have several; a store that did not know which one
        // it was serving would offer to delete the other one's passwords.
        foreach (IExternalSecretStore store in _stores.OfType<IExternalSecretStore>())
        {
            store.Open(document.Id);
        }
    }

    /// <summary>
    /// Chooses where this document keeps its saved passwords, and moves the
    /// ones it can read (M3-04).
    ///
    /// <para>
    /// The same staged re-protection as <see cref="Set"/> and
    /// <see cref="Remove"/>, and the same rule about what does not move: a
    /// password this Windows account cannot read stays exactly where it is.
    /// The one thing this does that they do not is release what it left
    /// behind — a Credential Manager entry whose reference has just been
    /// overwritten is not deleted by overwriting it.
    /// </para>
    /// </summary>
    public SecretStoreChange UseMachineStore(ConnectionDocument document, string scheme)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(scheme);

        if (IsProtected)
        {
            // Nothing here is wrong exactly — the preference would be honoured
            // if the master password came off — but silently recording a
            // choice that changes nothing today is how somebody comes to
            // believe their passwords moved.
            return SecretStoreChange.Failed(SecretStoreChangeStatus.Locked);
        }

        if (Find(scheme) is not { } store)
        {
            return SecretStoreChange.Failed(SecretStoreChangeStatus.NoSuchStore);
        }

        if (ReferenceEquals(store, _machine)
            && string.Equals(document.CredentialStore, scheme, StringComparison.Ordinal))
        {
            return SecretStoreChange.Failed(SecretStoreChangeStatus.AlreadyThere);
        }

        if (!store.IsAvailable)
        {
            return SecretStoreChange.Failed(SecretStoreChangeStatus.Unavailable);
        }

        int moved;
        int leftAlone;

        try
        {
            (moved, leftAlone) = Reprotect(document.Credentials, store);
        }
        catch (SecretProtectionException)
        {
            // Available and then refusing anyway — a policy that changed, a
            // full store, a password longer than the store will take. Nothing
            // has been written, because reading comes before writing, so this
            // is a refusal rather than a half-finished move.
            return SecretStoreChange.Failed(SecretStoreChangeStatus.Unavailable);
        }

        _machine = store;
        document.CredentialStore = scheme;
        NamesAnUnknownStore = false;

        return SecretStoreChange.Done(moved, leftAlone);
    }

    /// <summary>
    /// Deletes the entries the machine's external stores hold for this
    /// document that nothing in it refers to any more, and returns how many
    /// went (M3-04).
    ///
    /// <para>
    /// Scoped to this document twice over: the stores were opened against it,
    /// and what is still wanted comes from its own profiles. A document open
    /// somewhere else is untouched, which is the whole reason entries carry a
    /// document with them.
    /// </para>
    /// </summary>
    public int ForgetOrphanedSecrets(ConnectionDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        string?[] inUse = [.. document.Credentials.Select(profile => profile.ProtectedPassword)];

        return _stores.OfType<IExternalSecretStore>().Sum(store => store.ForgetOrphans(inUse));
    }

    /// <summary>
    /// Tries the master password. On success the document key is taken and
    /// held here.
    /// </summary>
    /// <returns>
    /// The status only. The key deliberately does not come back out: this
    /// owns it, and a result carrying a key somebody else also holds is a
    /// double-erase waiting to be written. For the sentence to show, use
    /// <see cref="MasterKeyResult.NoticeFor"/>.
    /// </returns>
    public MasterKeyStatus Unlock(ReadOnlySpan<char> password)
    {
        if (_record is not { } record)
        {
            return MasterKeyStatus.NotProtected;
        }

        MasterKeyResult opened = record.Unwrap(password);

        if (opened.IsSuccess && opened.Key is { } key)
        {
            _master.Unlock(key);
        }

        return opened.Status;
    }

    /// <summary>
    /// Gives the document key up. The document stays protected and stays
    /// open; its saved passwords stop being readable until somebody types the
    /// master password again.
    /// </summary>
    public void Lock() => _master.Lock();

    /// <summary>
    /// Puts a master password on a document that has none, and re-protects
    /// every saved password it can read.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The document already has one. Changing it is
    /// <see cref="Change"/>, and offering "set" for a document that has one is
    /// a bug in the caller rather than something to explain to the person.
    /// </exception>
    public MasterPasswordChange Set(ConnectionDocument document, ReadOnlySpan<char> password)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (_record is not null)
        {
            throw new InvalidOperationException(
                "This document already has a master password. Use Change to replace it.");
        }

        if (password.Length < MinimumPasswordLength)
        {
            return MasterPasswordChange.Failed(MasterPasswordChangeStatus.PasswordTooShort);
        }

        // Thirty-two random bytes that nobody types and nothing derives. The
        // master password protects this; this protects the passwords. See
        // MasterKeyRecord for why the indirection is not ceremony.
        byte[] documentKey = GC.AllocateArray<byte>(MasterKeyRecord.KeyLength, pinned: true);
        MasterKeyRecord record;

        try
        {
            RandomNumberGenerator.Fill(documentKey);

            record = MasterKeyRecord.Wrap(password, documentKey);
            _master.Unlock(Secret.FromUtf8(documentKey));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(documentKey);
        }

        int moved;
        int leftAlone;

        try
        {
            (moved, leftAlone) = Reprotect(document.Credentials, _master);
        }
        catch
        {
            // Nothing has been written to the document yet, so the only thing
            // to undo is this object holding a key for a document that is not
            // protected.
            Lock();
            throw;
        }

        _record = record;
        document.MasterKey = record;

        return MasterPasswordChange.Done(MasterPasswordChangeStatus.Protected, moved, leftAlone);
    }

    /// <summary>
    /// Replaces the master password. Nothing else moves: the document key is
    /// unchanged and only its wrapping is redone, which is what makes this
    /// instant on a document with three hundred saved passwords in it and what
    /// keeps a crash from leaving half of them under each password.
    /// </summary>
    /// <exception cref="InvalidOperationException">The document has no master password.</exception>
    public MasterPasswordChange Change(
        ConnectionDocument document,
        ReadOnlySpan<char> current,
        ReadOnlySpan<char> replacement)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (_record is not { } record)
        {
            throw new InvalidOperationException(
                "This document has no master password. Use Set to give it one.");
        }

        if (replacement.Length < MinimumPasswordLength)
        {
            return MasterPasswordChange.Failed(MasterPasswordChangeStatus.PasswordTooShort);
        }

        // Asked for even when the document is already unlocked. Somebody who
        // walked up to an unlocked screen should not be able to change the
        // master password without knowing it.
        MasterKeyResult opened = record.Unwrap(current);

        if (!opened.IsSuccess || opened.Key is not { } key)
        {
            return MasterPasswordChange.Failed(Reason(opened.Status));
        }

        try
        {
            MasterKeyRecord replaced = Rewrap(replacement, key);

            _record = replaced;
            document.MasterKey = replaced;

            return MasterPasswordChange.Done(MasterPasswordChangeStatus.Changed, moved: 0, leftAlone: 0);
        }
        finally
        {
            key.Dispose();
        }
    }

    /// <summary>
    /// Takes the master password off, putting every saved password it can read
    /// back into machine protection.
    /// </summary>
    /// <exception cref="InvalidOperationException">The document has no master password.</exception>
    public MasterPasswordChange Remove(ConnectionDocument document, ReadOnlySpan<char> password)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (_record is not { } record)
        {
            throw new InvalidOperationException("This document has no master password to remove.");
        }

        MasterKeyResult opened = record.Unwrap(password);

        if (!opened.IsSuccess || opened.Key is not { } key)
        {
            return MasterPasswordChange.Failed(Reason(opened.Status));
        }

        bool anythingToMove = document.Credentials.Any(
            profile => IsScheme(profile, MasterPasswordProtector.SchemeName));

        if (anythingToMove && !_machine.IsAvailable)
        {
            key.Dispose();
            return MasterPasswordChange.Failed(MasterPasswordChangeStatus.NowhereToPutPasswords);
        }

        _master.Unlock(key);

        int moved;
        int leftAlone;

        try
        {
            (moved, leftAlone) = Reprotect(document.Credentials, _machine);
        }
        catch (SecretProtectionException)
        {
            // Nothing to undo: the record is still on the document and still
            // correct, so everything is still readable and still protected.
            // The machine store said it was available and then refused, which
            // for the person is the same problem as it not being available.
            return MasterPasswordChange.Failed(MasterPasswordChangeStatus.NowhereToPutPasswords);
        }

        _record = null;
        document.MasterKey = null;
        Lock();

        return MasterPasswordChange.Done(MasterPasswordChangeStatus.Unprotected, moved, leftAlone);
    }

    /// <inheritdoc />
    public string Protect(Secret secret)
    {
        ArgumentNullException.ThrowIfNull(secret);

        return IsProtected ? _master.Protect(secret) : _machine.Protect(secret);
    }

    /// <inheritdoc />
    public SecretUnprotectResult Unprotect(string? storedText)
    {
        // The envelope checks that are the same for every scheme happen once,
        // here, in the same order SecretProtector runs them: what a blob is,
        // then whether this build understands it, then who it belongs to.
        if (!SecretEnvelope.TryParse(storedText, out SecretEnvelope? envelope))
        {
            return SecretUnprotectResult.Failed(SecretUnprotectStatus.NotASecret);
        }

        if (envelope.Version > SecretEnvelope.CurrentVersion)
        {
            return SecretUnprotectResult.Failed(SecretUnprotectStatus.TooNew);
        }

        if (string.Equals(envelope.Scheme, MasterPasswordProtector.SchemeName, StringComparison.Ordinal))
        {
            return _master.Unprotect(storedText);
        }

        // Every machine store, not just the one being written to. A document
        // that has moved from one to the other still holds passwords in the
        // one it left, and they go on being readable — which is what makes
        // moving something that can be done a few at a time and stopped
        // halfway without losing anything.
        if (Find(envelope.Scheme) is { } store)
        {
            return store.Unprotect(storedText);
        }

        // A scheme no protector here answers to — a Credential Manager blob
        // (M3-04) in a build that has no Credential Manager, say. Somebody can
        // read it; this cannot, and it must be left alone.
        return SecretUnprotectResult.Failed(SecretUnprotectStatus.WrongScheme);
    }

    /// <inheritdoc />
    public void Forget(string? storedText)
    {
        if (!SecretEnvelope.TryParse(storedText, out SecretEnvelope? envelope)
            || envelope.Version > SecretEnvelope.CurrentVersion)
        {
            return;
        }

        // Routed exactly like a read, and to a store rather than to the store:
        // the envelope being released is usually the one just replaced, which
        // by definition belongs to wherever the document used to write.
        if (string.Equals(envelope.Scheme, MasterPasswordProtector.SchemeName, StringComparison.Ordinal))
        {
            _master.Forget(storedText);
            return;
        }

        Find(envelope.Scheme)?.Forget(storedText);
    }

    /// <inheritdoc />
    public void Dispose() => _master.Dispose();

    private ISecretProtector? Find(string? scheme) =>
        scheme is null
            ? null
            : _stores.FirstOrDefault(
                store => string.Equals(store.Scheme, scheme, StringComparison.Ordinal));

    /// <summary>
    /// Moves every saved password this can read into
    /// <paramref name="into"/>, leaving the ones it cannot exactly as they
    /// are.
    ///
    /// <para>
    /// Read everything first, write nothing until all of it has been read.
    /// The obvious loop — read one, write one — leaves a document with some
    /// secrets under the new scheme and some under the old if anything throws
    /// halfway, and the record that says which is which has not been written
    /// yet at that point. Staging costs one list and removes the whole class
    /// of half-converted file.
    /// </para>
    ///
    /// <para>
    /// Releasing comes last, after every field has been reassigned, and only
    /// for the ones that moved (M3-04). The order is the point: forgetting a
    /// Credential Manager entry before the replacement is safely in the
    /// document would destroy the password if anything in between threw, and
    /// the ones that did not move still refer to entries that are still
    /// wanted.
    /// </para>
    /// </summary>
    private (int Moved, int LeftAlone) Reprotect(
        IEnumerable<CredentialProfile> profiles,
        ISecretProtector into)
    {
        List<(CredentialProfile Profile, string Envelope, string? Released)> staged = [];
        int leftAlone = 0;

        foreach (CredentialProfile profile in profiles)
        {
            if (!profile.HasPassword || IsScheme(profile, into.Scheme))
            {
                continue;
            }

            SecretUnprotectResult opened = Unprotect(profile.ProtectedPassword);

            if (!opened.IsSuccess || opened.Secret is not { } password)
            {
                leftAlone++;
                continue;
            }

            try
            {
                staged.Add((profile, into.Protect(password), profile.ProtectedPassword));
            }
            finally
            {
                // Read for the length of one re-encryption and no longer
                // (M3-03). Every password in the document passes through here.
                password.Dispose();
            }
        }

        foreach ((CredentialProfile profile, string envelope, string? _) in staged)
        {
            profile.ProtectedPassword = envelope;
        }

        foreach ((CredentialProfile _, string _, string? released) in staged)
        {
            Forget(released);
        }

        return (staged.Count, leftAlone);
    }

    /// <summary>
    /// Wraps an existing document key under a new password.
    ///
    /// <para>
    /// The copy is here because a password and a key are both spans and
    /// <see cref="Secret.Reveal"/> can only lend one of them at a time — a
    /// span cannot be captured, which is the property that makes
    /// <c>Reveal</c> safe. So the key is copied into a pinned buffer for the
    /// length of one call and written over afterwards.
    /// </para>
    /// </summary>
    private static MasterKeyRecord Rewrap(ReadOnlySpan<char> password, Secret documentKey)
    {
        byte[] copy = GC.AllocateArray<byte>(MasterKeyRecord.KeyLength, pinned: true);

        try
        {
            documentKey.Reveal(copy, static (key, into) => key.CopyTo(into));

            return MasterKeyRecord.Wrap(password, copy);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(copy);
        }
    }

    private static bool IsScheme(CredentialProfile profile, string scheme) =>
        profile.HasPassword
        && SecretEnvelope.TryParse(profile.ProtectedPassword, out SecretEnvelope? envelope)
        && string.Equals(envelope.Scheme, scheme, StringComparison.Ordinal);

    private static MasterPasswordChangeStatus Reason(MasterKeyStatus status) => status switch
    {
        MasterKeyStatus.WrongPassword => MasterPasswordChangeStatus.WrongPassword,
        MasterKeyStatus.UnknownKdf => MasterPasswordChangeStatus.UnknownKdf,
        _ => MasterPasswordChangeStatus.Damaged,
    };
}
