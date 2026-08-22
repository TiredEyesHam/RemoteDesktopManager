namespace Patchbay.Core.Security;

/// <summary>
/// The protector that refuses (M3-02). What <c>Core</c> uses when nobody has
/// given it a real one, and what the shell falls back to when the machine's
/// data protection does not work.
///
/// <para>
/// The obvious alternative is a protector that stores the secret as it is, and
/// it is obviously wrong: it makes the failure invisible. Nothing on screen
/// changes, the password appears to save, and the only difference is a
/// cleartext password sitting in a file that gets copied to a laptop, backed
/// up to a share and attached to a support ticket. Refusing is loud, which is
/// the point — the alternative to a saved password is being asked for it, and
/// that is a working state Patchbay already supports.
/// </para>
///
/// <para>
/// Reading still answers, because a document full of secrets nobody can open
/// is a perfectly ordinary thing to be looking at, and the connections around
/// them are still usable.
/// </para>
/// </summary>
public sealed class UnavailableSecretProtector : ISecretProtector
{
    /// <summary>The one instance. It has no state and never will.</summary>
    public static UnavailableSecretProtector Instance { get; } = new();

    private UnavailableSecretProtector()
    {
    }

    /// <summary>
    /// A name that will never appear in a file, because nothing here writes
    /// one. It exists so that a protector always has one to report.
    /// </summary>
    public string Scheme => "unavailable";

    /// <inheritdoc />
    public bool IsAvailable => false;

    /// <inheritdoc />
    public string Protect(Secret secret)
    {
        ArgumentNullException.ThrowIfNull(secret);

        throw new SecretProtectionException(
            "Patchbay has no way to protect a password on this machine, so it will not save "
            + "one. The connection can still ask for the password each time it connects.");
    }

    /// <inheritdoc />
    public SecretUnprotectResult Unprotect(string? storedText) =>
        SecretUnprotectResult.Failed(
            SecretEnvelope.TryParse(storedText, out _)
                ? SecretUnprotectStatus.Unavailable
                : SecretUnprotectStatus.NotASecret);

    /// <summary>
    /// Nothing to release, because nothing was ever reserved. This protector
    /// refuses to write, so no document holds an envelope it made.
    /// </summary>
    public void Forget(string? storedText)
    {
    }
}
