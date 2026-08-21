using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using Patchbay.Core.Diagnostics;

namespace Patchbay.Core.Security;

/// <summary>
/// A password in memory, in a buffer that can be erased (M3-03).
///
/// <para>
/// A .NET <see cref="string"/> cannot be erased. It is immutable, it may be
/// interned, and a compacting collection can copy it somewhere else and leave
/// the old bytes behind with nothing left pointing at them. So a password that
/// arrives as a string at nine in the morning is still legible in a memory
/// dump at five, and nothing in the process can change that. This type exists
/// so that the copies Patchbay controls are not strings.
/// </para>
///
/// <para>
/// The buffer comes from the pinned object heap. Pinning is not about
/// interop here: the garbage collector moves objects when it compacts, and a
/// buffer that has been moved cannot be erased, because erasing writes over
/// wherever it is now and not wherever it has been. A pinned buffer never
/// moves, so zeroing it on <see cref="Dispose"/> actually erases the only copy
/// there was.
/// </para>
///
/// <para>
/// Bytes are UTF-8, which is what <c>M3-02</c> already protects and stores.
/// Changing that would make every password saved by an earlier version
/// unreadable, which is a worse outcome than any encoding argument is worth.
/// </para>
///
/// <para>
/// What this does not do. It does not stop the page reaching the swap file —
/// that needs <c>VirtualLock</c>, which is Windows-only and unsafe, and
/// belongs to the platform layer rather than here. It does not help against
/// anything that can read the process while it is running, which the threat
/// model already puts out of scope. And it cannot cover the last step: the RDP
/// control takes its password as a BSTR, so <see cref="RevealAsString"/> has
/// to exist and the string it makes cannot be taken back.
/// </para>
/// </summary>
public sealed class Secret : IDisposable, IEquatable<Secret>
{
    /// <summary>
    /// Keys the fingerprint, and is new every time the process starts.
    ///
    /// <para>
    /// The point is not to defend the fingerprint against somebody who has the
    /// process memory — they have this key as well. It is that a fingerprint
    /// written down anywhere outside the process, now or by accident later,
    /// cannot be looked up in a table of hashed common passwords, and cannot
    /// be compared against a fingerprint from a different run.
    /// </para>
    /// </summary>
    private static readonly byte[] FingerprintKey = RandomNumberGenerator.GetBytes(32);

    /// <summary>Longest password encoded on the stack rather than in a rented buffer.</summary>
    private const int StackLimit = 512;

    private readonly byte[] _utf8;
    private readonly byte[] _fingerprint;

    private bool _disposed;

    private Secret(byte[] utf8)
    {
        _utf8 = utf8;
        _fingerprint = HMACSHA256.HashData(FingerprintKey, utf8);
    }

    /// <summary>
    /// No password. Shared, and disposing it does nothing, so it is safe to
    /// hold in a static and to hand out as a default.
    /// </summary>
    public static Secret Empty { get; } = new([]);

    /// <summary>How many bytes of UTF-8, which is not how many characters.</summary>
    public int Length => _utf8.Length;

    /// <summary>Whether there is a password here at all.</summary>
    public bool IsEmpty => _utf8.Length == 0;

    /// <summary>
    /// Whether the plaintext has been erased. Identity survives this; see
    /// <see cref="Dispose"/>.
    /// </summary>
    public bool IsDisposed => _disposed;

    /// <summary>
    /// Copies a typed password into a buffer that can be erased. The
    /// <see cref="string"/> or span it came from is not this type's to clean
    /// up and is very likely a <c>string</c> that never will be — which is why
    /// the aim is to reach one of these early rather than to pretend the
    /// earlier copy did not happen.
    /// </summary>
    public static Secret From(ReadOnlySpan<char> password)
    {
        if (password.IsEmpty)
        {
            return Empty;
        }

        int count = Encoding.UTF8.GetByteCount(password);
        byte[] buffer = Allocate(count);

        Encoding.UTF8.GetBytes(password, buffer);

        return new Secret(buffer);
    }

    /// <summary>
    /// Takes a copy of already-encoded bytes, which is the shape a secret
    /// arrives in from a store. No string is made anywhere on this path.
    /// </summary>
    public static Secret FromUtf8(ReadOnlySpan<byte> utf8)
    {
        if (utf8.IsEmpty)
        {
            return Empty;
        }

        byte[] buffer = Allocate(utf8.Length);

        utf8.CopyTo(buffer);

        return new Secret(buffer);
    }

    /// <summary>
    /// Lends the bytes to <paramref name="use"/> for as long as that call
    /// lasts, and no longer.
    ///
    /// <para>
    /// A span rather than a return value on purpose: a span cannot be stored
    /// on a field or captured in a closure, so the compiler enforces what a
    /// comment would otherwise have to ask for.
    /// </para>
    /// </summary>
    /// <exception cref="ObjectDisposedException">The plaintext has been erased.</exception>
    public void Reveal<TState>(TState state, ReadOnlySpanAction<byte, TState> use)
    {
        ArgumentNullException.ThrowIfNull(use);
        ObjectDisposedException.ThrowIf(_disposed, this);

        use(_utf8, state);
    }

    /// <summary>
    /// The password as a <see cref="string"/>, which is a copy that cannot be
    /// erased afterwards.
    ///
    /// <para>
    /// Deliberately named to be uncomfortable to write. There is exactly one
    /// reason to call it — handing the password to the RDP control, which
    /// takes a BSTR — and it should be called as late as possible and the
    /// result should not be kept.
    /// </para>
    /// </summary>
    /// <exception cref="ObjectDisposedException">The plaintext has been erased.</exception>
    public string RevealAsString()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return Encoding.UTF8.GetString(_utf8);
    }

    /// <summary>
    /// Whether a typed password is the same one, without making a
    /// <see cref="Secret"/> to ask.
    ///
    /// <para>
    /// This is what lets a prompt keep asking "is this the password that was
    /// just refused?" on every keystroke (M3-06) without allocating a pinned
    /// buffer each time, and what lets the refused password itself be erased
    /// while the question is still answerable.
    /// </para>
    /// </summary>
    public bool Matches(ReadOnlySpan<char> candidate)
    {
        if (candidate.IsEmpty)
        {
            return IsEmpty;
        }

        int count = Encoding.UTF8.GetByteCount(candidate);

        byte[]? rented = count > StackLimit ? ArrayPool<byte>.Shared.Rent(count) : null;
        Span<byte> scratch = rented is null ? stackalloc byte[count] : rented.AsSpan(0, count);

        try
        {
            Encoding.UTF8.GetBytes(candidate, scratch);

            Span<byte> theirs = stackalloc byte[HMACSHA256.HashSizeInBytes];
            HMACSHA256.HashData(FingerprintKey, scratch, theirs);

            return CryptographicOperations.FixedTimeEquals(_fingerprint, theirs);
        }
        finally
        {
            // Both of these hold the candidate in the clear. A rented buffer
            // goes back to the pool for somebody else to read, and a stack
            // frame is reused by whatever is called next.
            CryptographicOperations.ZeroMemory(scratch);

            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }

    /// <summary>
    /// Whether two secrets are the same password, by fingerprint rather than
    /// by plaintext.
    ///
    /// <para>
    /// Two consequences worth knowing. Comparing does not need either
    /// plaintext, so it still works after <see cref="Dispose"/> — which is
    /// what makes it safe to erase a refused password and go on refusing it.
    /// And the comparison takes the same time whatever the answer, which is
    /// not defending against a timing attack that anybody could mount here; it
    /// is that the alternative is a byte-by-byte compare that stops early, and
    /// there is no reason to write one of those over a password.
    /// </para>
    /// </summary>
    public bool Equals(Secret? other) =>
        other is not null
        && (ReferenceEquals(this, other)
            || CryptographicOperations.FixedTimeEquals(_fingerprint, other._fingerprint));

    public override bool Equals(object? obj) => Equals(obj as Secret);

    /// <summary>
    /// From the fingerprint, so it stays valid after disposal and never varies
    /// with the plaintext in a way anything outside the process could use.
    /// </summary>
    public override int GetHashCode() => BitConverter.ToInt32(_fingerprint, 0);

    /// <summary>
    /// Erases the plaintext. Reading it afterwards throws rather than
    /// returning an empty password, because a session that silently connects
    /// with nothing is a bug that looks like a wrong password.
    ///
    /// <para>
    /// What survives is the fingerprint, so a disposed secret can still say
    /// whether something equals it. That is deliberate: the identity of a
    /// password is not the password.
    /// </para>
    ///
    /// <para>
    /// Safe to call more than once, and on <see cref="Empty"/>, where it does
    /// nothing at all.
    /// </para>
    /// </summary>
    public void Dispose()
    {
        if (_disposed || _utf8.Length == 0)
        {
            return;
        }

        CryptographicOperations.ZeroMemory(_utf8);
        _disposed = true;
    }

    /// <summary>Fixed width, and never the password.</summary>
    public override string ToString() =>
        IsEmpty ? $"{nameof(Secret)} {{ none }}" : $"{nameof(Secret)} {{ {SecretNames.Mask} }}";

    /// <summary>
    /// On the pinned object heap, so the collector cannot move it and leave a
    /// copy behind that nothing will ever erase.
    /// </summary>
    private static byte[] Allocate(int length) => GC.AllocateArray<byte>(length, pinned: true);
}
