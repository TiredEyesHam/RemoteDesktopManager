using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Patchbay.Core.Security;

/// <summary>
/// The document key, wrapped under a master password (M3-07). This is the one
/// part of the scheme that gets written to the file.
///
/// <para>
/// <b>Two keys, not one.</b> The master password derives a key-encryption key,
/// and that key encrypts a separate random document key, which is what every
/// saved password is actually encrypted with. The indirection buys three
/// things, and each of them is a bug avoided rather than a nicety:
/// </para>
///
/// <list type="bullet">
///   <item><b>One derivation per unlock.</b> Deriving per secret would mean
///   six hundred thousand iterations of PBKDF2 for every password in the
///   document, every time it opens.</item>
///   <item><b>Changing the master password rewraps one key.</b> The
///   alternative re-encrypts every saved password, and a crash halfway through
///   that leaves a document with some secrets under the old password and some
///   under the new, which is a file nobody can fully open again.</item>
///   <item><b>A wrong password is detected once, at unlock.</b> Not by trying
///   to decrypt a password and seeing what happens.</item>
/// </list>
///
/// <para>
/// <b>The wrapped key is its own verifier.</b> AES-GCM authenticates, so a
/// wrong password fails the tag check and there is nothing else to store. A
/// separate "check value" would be an extra thing to get wrong and an extra
/// thing for an attacker to test against; the wrapped key already is one.
/// </para>
///
/// <para>
/// <b>Nothing here is secret.</b> The salt, the iteration count and the
/// wrapped key are all readable by anybody holding the file, and are meant to
/// be. What they do not contain is the master password or the document key.
/// </para>
/// </summary>
public sealed class MasterKeyRecord
{
    /// <summary>
    /// The only key derivation function this build has, named in the file so
    /// that the next one can be added without stranding a single document.
    ///
    /// <para>
    /// PBKDF2-HMAC-SHA256 rather than Argon2id, which is the better function
    /// and is not in the framework. Argon2id would mean a third-party
    /// cryptographic implementation in the path that protects every password
    /// Patchbay saves, which is a supply-chain surface bought for a
    /// memory-hardness property that matters against a dedicated attacker with
    /// custom hardware. Naming the function in the record is what makes that a
    /// decision to revisit rather than one to live with: a document written
    /// today says <c>pbkdf2-sha256</c>, and a build that grows an Argon2id
    /// option will still open it.
    /// </para>
    /// </summary>
    public const string Pbkdf2Sha256 = "pbkdf2-sha256";

    /// <summary>
    /// OWASP's current figure for PBKDF2-HMAC-SHA256, and measured at about
    /// 93 ms on the machine this was written on — imperceptible once per
    /// document, and the whole cost of the scheme, since encrypting a password
    /// afterwards takes about three microseconds.
    ///
    /// <para>
    /// Stored in the file rather than assumed, so that raising it in a later
    /// build costs nothing: old documents keep opening at the count they were
    /// written with, and get the new one the next time the master password is
    /// changed.
    /// </para>
    /// </summary>
    public const int DefaultIterations = 600_000;

    /// <summary>
    /// A floor, not a compatibility line. Nothing Patchbay has ever written is
    /// below this; a file that says less than it has been edited by hand, and
    /// the edit cannot have been an improvement.
    /// </summary>
    public const int MinimumIterations = 100_000;

    /// <summary>
    /// A ceiling, because the iteration count comes out of a file somebody
    /// else may have written. Ten million takes about two seconds here; the
    /// numbers above it are how a document turns into a hang.
    /// </summary>
    public const int MaximumIterations = 10_000_000;

    /// <summary>Sixteen bytes, so two documents never share a derived key.</summary>
    public const int SaltLength = 16;

    /// <summary>AES-256.</summary>
    public const int KeyLength = 32;

    /// <summary>
    /// The only nonce length AES-GCM accepts in .NET, and the one the
    /// construction is specified for.
    /// </summary>
    public const int NonceLength = 12;

    /// <summary>Full-length tag. A truncated one buys nothing here.</summary>
    public const int TagLength = 16;

    private const string Domain = "patchbay-master-key";

    /// <summary>Which key derivation function, lowercase. See <see cref="Pbkdf2Sha256"/>.</summary>
    public string Kdf { get; set; } = Pbkdf2Sha256;

    /// <summary>How many iterations it was derived with.</summary>
    public int Iterations { get; set; } = DefaultIterations;

    /// <summary>The salt, base64. Public by design.</summary>
    public string Salt { get; set; } = string.Empty;

    /// <summary>
    /// Nonce, ciphertext and tag, concatenated and base64. One field rather
    /// than three because they are never useful apart, and a document with two
    /// of the three in it is a shape nothing should have to handle.
    /// </summary>
    public string WrappedKey { get; set; } = string.Empty;

    /// <summary>
    /// Makes a record protecting <paramref name="documentKey"/> with
    /// <paramref name="password"/>, under a fresh salt and the current
    /// iteration count.
    /// </summary>
    public static MasterKeyRecord Wrap(ReadOnlySpan<char> password, ReadOnlySpan<byte> documentKey)
    {
        if (password.IsEmpty)
        {
            throw new ArgumentException(
                "A master password of nothing protects nothing.",
                nameof(password));
        }

        if (documentKey.Length != KeyLength)
        {
            throw new ArgumentException(
                $"A document key is {KeyLength} bytes.",
                nameof(documentKey));
        }

        byte[] salt = RandomNumberGenerator.GetBytes(SaltLength);

        MasterKeyRecord record = new()
        {
            Kdf = Pbkdf2Sha256,
            Iterations = DefaultIterations,
            Salt = Convert.ToBase64String(salt),
        };

        byte[] wrapped = new byte[NonceLength + KeyLength + TagLength];
        RandomNumberGenerator.Fill(wrapped.AsSpan(0, NonceLength));

        Span<byte> kek = stackalloc byte[KeyLength];

        try
        {
            Rfc2898DeriveBytes.Pbkdf2(password, salt, kek, DefaultIterations, HashAlgorithmName.SHA256);

            using AesGcm cipher = new(kek, TagLength);

            cipher.Encrypt(
                wrapped.AsSpan(0, NonceLength),
                documentKey,
                wrapped.AsSpan(NonceLength, KeyLength),
                wrapped.AsSpan(NonceLength + KeyLength, TagLength),
                record.AuthenticatedParameters());
        }
        finally
        {
            // The key-encryption key exists only for this call. Leaving it in
            // a stack frame that the next call reuses is the sort of copy this
            // codebase spent M3-03 getting rid of.
            CryptographicOperations.ZeroMemory(kek);
        }

        record.WrappedKey = Convert.ToBase64String(wrapped);

        return record;
    }

    /// <summary>
    /// Tries to recover the document key. Never throws for a bad password or a
    /// bad record: both are ordinary contents of a file that has been moved
    /// around.
    /// </summary>
    public MasterKeyResult Unwrap(ReadOnlySpan<char> password)
    {
        // Order matters, the same way it does in SecretProtector: a function
        // this build does not have makes everything after it unanswerable, so
        // asking about it first is what keeps "written by a newer Patchbay"
        // from being reported as damage.
        if (!string.Equals(Kdf, Pbkdf2Sha256, StringComparison.Ordinal))
        {
            return MasterKeyResult.Failed(MasterKeyStatus.UnknownKdf);
        }

        if (Iterations is < MinimumIterations or > MaximumIterations)
        {
            return MasterKeyResult.Failed(MasterKeyStatus.Damaged);
        }

        if (!TryDecode(Salt, SaltLength, out byte[]? salt)
            || !TryDecode(WrappedKey, NonceLength + KeyLength + TagLength, out byte[]? wrapped))
        {
            return MasterKeyResult.Failed(MasterKeyStatus.Damaged);
        }

        if (password.IsEmpty)
        {
            // Nothing ever wrapped a key with one, so this cannot be right,
            // and saying so costs no derivation.
            return MasterKeyResult.Failed(MasterKeyStatus.WrongPassword);
        }

        Span<byte> kek = stackalloc byte[KeyLength];
        Span<byte> documentKey = stackalloc byte[KeyLength];

        try
        {
            Rfc2898DeriveBytes.Pbkdf2(password, salt, kek, Iterations, HashAlgorithmName.SHA256);

            using AesGcm cipher = new(kek, TagLength);

            cipher.Decrypt(
                wrapped.AsSpan(0, NonceLength),
                wrapped.AsSpan(NonceLength, KeyLength),
                wrapped.AsSpan(NonceLength + KeyLength, TagLength),
                documentKey,
                AuthenticatedParameters());

            return MasterKeyResult.Unlocked(Secret.FromUtf8(documentKey));
        }
        catch (AuthenticationTagMismatchException)
        {
            // The one outcome the person can do something about. It is also
            // the only thing this catch can mean: the parameters were checked
            // above and the lengths are exact.
            return MasterKeyResult.Failed(MasterKeyStatus.WrongPassword);
        }
        catch (CryptographicException)
        {
            return MasterKeyResult.Failed(MasterKeyStatus.Damaged);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(kek);
            CryptographicOperations.ZeroMemory(documentKey);
        }
    }

    /// <summary>
    /// The parameters, authenticated alongside the wrapped key rather than
    /// merely stored beside it.
    ///
    /// <para>
    /// Editing the iteration count in the file cannot weaken anything on its
    /// own — a key derived at the wrong count simply will not unwrap — but
    /// authenticating the parameters means a tampered record fails as a
    /// tampered record rather than as a wrong password, and it means the
    /// fields that a future Argon2id would add are covered from the day they
    /// exist rather than the day somebody remembers.
    /// </para>
    /// </summary>
    private byte[] AuthenticatedParameters() => Encoding.UTF8.GetBytes(
        string.Create(CultureInfo.InvariantCulture, $"{Domain}/{Kdf}/{Iterations}"));

    /// <summary>
    /// Base64 of an exactly known length. Exact rather than "at most",
    /// because every field here has one fixed size and anything else is a
    /// record to refuse rather than a record to interpret.
    /// </summary>
    private static bool TryDecode(string? text, int expectedLength, out byte[]? bytes)
    {
        bytes = null;

        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        byte[] buffer = new byte[expectedLength];

        if (!Convert.TryFromBase64String(text, buffer, out int written) || written != expectedLength)
        {
            return false;
        }

        bytes = buffer;
        return true;
    }
}
