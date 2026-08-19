using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Patchbay.Core.Security;

/// <summary>
/// The shape a protected secret takes once it is text (M3-02).
///
/// A protected secret has to survive a round trip through a JSON string in the
/// connection document, and it has to arrive at the other end saying three
/// things about itself:
///
/// <list type="bullet">
///   <item><b>That it is one.</b> A field holding <c>hunter2</c> and a field
///   holding a DPAPI blob are both strings. Without a marker, the only way to
///   tell them apart is to try to decrypt one, and a password that happens to
///   look like base64 would be quietly destroyed.</item>
///   <item><b>Who protected it.</b> DPAPI is not the only store Patchbay will
///   have — Windows Credential Manager is <c>M3-04</c> and a document master
///   password is <c>M3-07</c> — and a document may well contain blobs from
///   more than one of them at once, because they arrive one secret at a time.
///   The scheme says which key opens which.</item>
///   <item><b>What format it is in.</b> So that a file written by a later
///   Patchbay can be recognised as such and refused politely, rather than
///   reported as a corrupt password.</item>
/// </list>
///
/// <para>
/// The text form is <c>pb1:dpapi:BASE64</c>. Colons, because base64 has none:
/// the payload cannot smuggle a separator into the middle of the string. Not
/// JSON, because this already lives inside a JSON string and nesting one in
/// the other only produces escaping.
/// </para>
///
/// <para>
/// The envelope is not a security boundary and holds nothing secret. Anyone
/// reading the file can see the scheme and the ciphertext; what they cannot do
/// is read the plaintext, and that is the protector's job, not this one's.
/// </para>
/// </summary>
public sealed class SecretEnvelope
{
    /// <summary>
    /// The format version Patchbay writes. Bumped only if the envelope itself
    /// changes shape — a new protection scheme is a new
    /// <see cref="Scheme"/>, not a new version.
    /// </summary>
    public const int CurrentVersion = 1;

    /// <summary>Longest scheme name accepted. Nothing honest comes close.</summary>
    public const int MaxSchemeLength = 32;

    private const string Prefix = "pb";
    private const char Separator = ':';

    private readonly byte[] _payload;

    private SecretEnvelope(int version, string scheme, byte[] payload)
    {
        Version = version;
        Scheme = scheme;
        _payload = payload;
    }

    /// <summary>The envelope format this blob was written in.</summary>
    public int Version { get; }

    /// <summary>
    /// Which protector made the payload, lowercase. Compared with
    /// <see cref="StringComparison.Ordinal"/>, which is safe precisely because
    /// the case is normalised on the way in and on the way out.
    /// </summary>
    public string Scheme { get; }

    /// <summary>The protected bytes, meaningless to anyone but the named scheme.</summary>
    public ReadOnlyMemory<byte> Payload => _payload;

    /// <summary>
    /// Wraps a freshly protected payload at <see cref="CurrentVersion"/>.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The scheme is empty, too long, or contains anything but lowercase
    /// letters, digits and hyphens; or the payload is empty.
    /// </exception>
    public static SecretEnvelope Create(string scheme, ReadOnlySpan<byte> payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheme);

        string normalised = scheme.ToLowerInvariant();

        if (!IsUsableScheme(normalised))
        {
            throw new ArgumentException(
                $"'{scheme}' is not a usable protection scheme name. Names are up to "
                + $"{MaxSchemeLength} characters of letters, digits and hyphens.",
                nameof(scheme));
        }

        if (payload.IsEmpty)
        {
            throw new ArgumentException(
                "A protected secret with no payload is not a protected secret.",
                nameof(payload));
        }

        return new SecretEnvelope(CurrentVersion, normalised, payload.ToArray());
    }

    /// <summary>
    /// Reads an envelope back out of text, without trusting any of it. Returns
    /// false for everything that is not one — including a plain password, an
    /// empty field, and a blob somebody has edited by hand.
    ///
    /// <para>
    /// A version this Patchbay does not know is <em>parsed</em>, not rejected:
    /// telling someone their password was saved by a newer version is a
    /// different sentence from telling them it is corrupt, and only the caller
    /// knows which it can act on.
    /// </para>
    /// </summary>
    public static bool TryParse(string? text, [NotNullWhen(true)] out SecretEnvelope? envelope)
    {
        envelope = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string[] parts = text.Split(Separator);

        if (parts.Length != 3)
        {
            return false;
        }

        if (!TryReadVersion(parts[0], out int version))
        {
            return false;
        }

        string scheme = parts[1].ToLowerInvariant();

        if (!IsUsableScheme(scheme))
        {
            return false;
        }

        if (!TryReadPayload(parts[2], out byte[]? payload))
        {
            return false;
        }

        envelope = new SecretEnvelope(version, scheme, payload);
        return true;
    }

    /// <summary>
    /// The text form, which is what goes in the file. Safe to log: it is the
    /// protected side of the secret, and it is the only representation of a
    /// secret in Patchbay that is.
    /// </summary>
    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture,
        $"{Prefix}{Version}{Separator}{Scheme}{Separator}{Convert.ToBase64String(_payload)}");

    private static bool TryReadVersion(string part, out int version)
    {
        version = 0;

        if (!part.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        ReadOnlySpan<char> digits = part.AsSpan(Prefix.Length);

        return digits.Length is > 0 and <= 4
            && int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out version)
            && version > 0;
    }

    private static bool TryReadPayload(string part, [NotNullWhen(true)] out byte[]? payload)
    {
        payload = null;

        if (part.Length == 0)
        {
            return false;
        }

        // Sized from the base64 length rather than decoded into a growing
        // buffer: the length is known before anything is trusted, so a
        // hostile field cannot ask for an allocation bigger than itself.
        byte[] buffer = new byte[(part.Length / 4 * 3) + 3];

        if (!Convert.TryFromBase64String(part, buffer, out int written) || written == 0)
        {
            return false;
        }

        payload = buffer.AsSpan(0, written).ToArray();
        return true;
    }

    private static bool IsUsableScheme(string scheme)
    {
        if (scheme.Length is 0 or > MaxSchemeLength)
        {
            return false;
        }

        foreach (char c in scheme)
        {
            if (!char.IsAsciiLetterLower(c) && !char.IsAsciiDigit(c) && c != '-')
            {
                return false;
            }
        }

        return true;
    }
}
