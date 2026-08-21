using System.Security.Cryptography;

namespace Patchbay.Core.Security;

/// <summary>
/// Encrypts a password under the document key (M3-07). The one protector that
/// lives in <c>Core</c>, because it is the one that does not ask the operating
/// system for anything — which also makes it the one with real tests.
///
/// <para>
/// <b>What this defends against that DPAPI does not.</b> DPAPI's
/// <c>CurrentUser</c> scope is a boundary the machine enforces, so a local
/// administrator can step over it and the signed-in account's own processes
/// are inside it. The document key is not held by the machine at all: it comes
/// out of a password nobody typed into Windows, so a document protected this
/// way is unreadable to an administrator, unreadable on the account it was
/// written by until somebody types the password, and readable on any machine
/// by somebody who knows it. That last one is a feature and a liability at the
/// same time, and it is the honest trade — see <c>docs/THREAT-MODEL.md</c>.
/// </para>
///
/// <para>
/// <b>A fresh nonce per secret, and never a counter.</b> Reusing a nonce under
/// GCM does not leak one password, it leaks the relationship between two of
/// them and the authentication key along with it. Ninety-six random bits give
/// a collision worth worrying about somewhere past four billion saved
/// passwords, which a connection document will not reach; a counter would need
/// state that survives every crash, every restore from backup and every copy
/// of the file, and one of those would eventually not survive.
/// </para>
///
/// <para>
/// <b>The key while it is in use.</b> <see cref="AesGcm"/> takes its own copy
/// of the key material and keeps it where this code cannot write over it, so
/// the pinned buffer discipline of <c>M3-03</c> stops at the cipher's door.
/// That is why the cipher is disposed on <see cref="Lock"/> rather than left
/// alive for the process: the copy this codebase cannot erase is one it can at
/// least keep short. The <see cref="Secret"/> holding the key is erased at the
/// same moment, and everything about a running process remains out of scope.
/// </para>
/// </summary>
public sealed class MasterPasswordProtector : SecretProtector, IDisposable
{
    /// <summary>What this stamps on what it writes: <c>pb1:master:BASE64</c>.</summary>
    public const string SchemeName = "master";

    private Secret? _key;
    private AesGcm? _cipher;

    /// <inheritdoc />
    public override string Scheme => SchemeName;

    /// <summary>
    /// Whether the document key is in hand. False before the master password
    /// has been typed, and false again after <see cref="Lock"/>.
    /// </summary>
    public override bool IsAvailable => _cipher is not null;

    /// <inheritdoc />
    protected override string UnavailableMessage =>
        "This document is locked. Enter its master password before saving a password to it.";

    /// <inheritdoc />
    protected override SecretUnprotectStatus UnavailableStatus => SecretUnprotectStatus.Locked;

    /// <summary>
    /// Takes the document key. Ownership comes with it: the key is erased by
    /// <see cref="Lock"/> and must not be held anywhere else.
    /// </summary>
    public void Unlock(Secret documentKey)
    {
        ArgumentNullException.ThrowIfNull(documentKey);

        if (documentKey.Length != MasterKeyRecord.KeyLength)
        {
            throw new ArgumentException(
                $"A document key is {MasterKeyRecord.KeyLength} bytes.",
                nameof(documentKey));
        }

        Lock();

        _key = documentKey;

        // Built once rather than per secret, so that the key is revealed once
        // rather than on every save.
        documentKey.Reveal(
            this,
            static (key, self) => self._cipher = new AesGcm(key, MasterKeyRecord.TagLength));
    }

    /// <summary>
    /// Gives the key up. Idempotent, and the only way back to
    /// <see cref="IsAvailable"/> being false.
    /// </summary>
    public void Lock()
    {
        _cipher?.Dispose();
        _cipher = null;

        _key?.Dispose();
        _key = null;
    }

    /// <inheritdoc />
    public void Dispose() => Lock();

    /// <inheritdoc />
    protected override byte[] ProtectCore(ReadOnlySpan<byte> utf8)
    {
        byte[] payload = new byte[MasterKeyRecord.NonceLength + utf8.Length + MasterKeyRecord.TagLength];

        RandomNumberGenerator.Fill(payload.AsSpan(0, MasterKeyRecord.NonceLength));

        _cipher!.Encrypt(
            payload.AsSpan(0, MasterKeyRecord.NonceLength),
            utf8,
            payload.AsSpan(MasterKeyRecord.NonceLength, utf8.Length),
            payload.AsSpan(MasterKeyRecord.NonceLength + utf8.Length, MasterKeyRecord.TagLength),
            AssociatedData);

        return payload;
    }

    /// <inheritdoc />
    protected override SecretUnprotectResult UnprotectCore(ReadOnlySpan<byte> payload)
    {
        int overhead = MasterKeyRecord.NonceLength + MasterKeyRecord.TagLength;

        if (payload.Length <= overhead)
        {
            // Too short to hold a nonce, a tag and a byte of password. Not a
            // wrong key — a truncated blob.
            return SecretUnprotectResult.Failed(SecretUnprotectStatus.Unreadable);
        }

        int length = payload.Length - overhead;

        byte[] plain = GC.AllocateArray<byte>(length, pinned: true);

        try
        {
            _cipher!.Decrypt(
                payload[..MasterKeyRecord.NonceLength],
                payload.Slice(MasterKeyRecord.NonceLength, length),
                payload[(MasterKeyRecord.NonceLength + length)..],
                plain,
                AssociatedData);

            return SecretUnprotectResult.Success(Secret.FromUtf8(plain));
        }
        catch (CryptographicException)
        {
            // A tag that does not verify means this blob was written under a
            // different document key, or it has been edited. Both come back as
            // Unreadable, which already says the right thing: this password has
            // to be entered again.
            return SecretUnprotectResult.Failed(SecretUnprotectStatus.Unreadable);
        }
        finally
        {
            // Pinned and erased, so the decrypted password does not survive as
            // a second copy that nothing points at (M3-03). Secret.FromUtf8
            // took its own.
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    /// <summary>
    /// Binds the ciphertext to the envelope it goes in, so that a payload
    /// cannot be moved into a differently-labelled envelope and still verify.
    /// Costs nothing and closes a shape of confusion rather than an attack
    /// anybody has.
    /// </summary>
    private static ReadOnlySpan<byte> AssociatedData => "pb1:master"u8;
}
