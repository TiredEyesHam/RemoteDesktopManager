namespace Patchbay.Core.Security;

/// <summary>
/// Turns a secret into something safe to write down, and back again (M3-02).
///
/// The interface lives in <c>Core</c> and the working implementation does not,
/// because every real one is platform code: DPAPI is Windows, Credential
/// Manager is Windows (M3-04), and a master password (M3-07) is the only one
/// that could live here. Anything in <c>Core</c> that has to store a secret
/// takes one of these and never knows which.
///
/// <para>
/// <b>What a protector is and is not for.</b> DPAPI's <c>CurrentUser</c> scope
/// stops another account on the machine reading the file, and stops the file
/// being useful if it is copied off the machine. It does not stop code running
/// as the signed-in user, and nothing at this layer can: that code can ask
/// Windows to unprotect the blob and Windows will, because that is precisely
/// what the scope means. Protecting the file against its own owner needs a
/// secret the machine does not hold, which is what M3-07 is.
/// </para>
///
/// <para>
/// <b>Refusing is a valid answer.</b> A protector that cannot protect must
/// throw rather than hand back the secret in the clear — see
/// <see cref="UnavailableSecretProtector"/>. Writing a plaintext password into
/// the connection document because the machine's cryptography was unavailable
/// is worse than not saving it at all, and it is silent, which makes it worse
/// again.
/// </para>
/// </summary>
public interface ISecretProtector
{
    /// <summary>
    /// The name this protector stamps on what it writes, and the only name it
    /// will read back. See <see cref="SecretEnvelope"/>.
    /// </summary>
    string Scheme { get; }

    /// <summary>
    /// Whether protection actually works here. Checked before offering to save
    /// a password, not after failing to.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Protects a secret and returns the text to store — a
    /// <see cref="SecretEnvelope"/>, so that whoever reads it back knows what
    /// it is.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="secret"/> is empty.</exception>
    /// <exception cref="SecretProtectionException">
    /// Protection is unavailable or failed. The caller must not store anything.
    /// </exception>
    string Protect(Secret secret);

    /// <summary>
    /// Reads a stored secret back. Never throws for input it does not like:
    /// unreadable and not-a-secret are both ordinary outcomes and both come
    /// back as a <see cref="SecretUnprotectResult"/>.
    /// </summary>
    SecretUnprotectResult Unprotect(string? storedText);
}
