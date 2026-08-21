namespace Patchbay.Core.Security;

/// <summary>
/// The outcome of reading a stored secret (M3-02) — the secret when there is
/// one, and a reason there is not when there is not.
///
/// A failure to read a saved password is an ordinary condition and not an
/// exceptional one. Documents get copied to a new laptop; people get a new
/// Windows account; a colleague opens the file that was shared with them.
/// Every one of those produces a password that cannot be read here and a
/// connection that is otherwise perfectly good, so the caller gets a result to
/// look at rather than an exception to catch.
/// </summary>
public sealed record SecretUnprotectResult
{
    private SecretUnprotectResult(SecretUnprotectStatus status, Secret? secret)
    {
        Status = status;
        Secret = secret;
    }

    /// <summary>What happened.</summary>
    public SecretUnprotectStatus Status { get; }

    /// <summary>
    /// The secret, or null when there is none.
    ///
    /// <para>
    /// Owned by whoever asked for it, and erasable when they are done with it
    /// (M3-03). It arrives without a <see cref="string"/> ever having been
    /// made for it, which is the whole point of the type: a decoded password
    /// that reached a string once would stay legible in the heap for the rest
    /// of the run.
    /// </para>
    /// </summary>
    public Secret? Secret { get; }

    /// <summary>Whether there is a secret to use.</summary>
    public bool IsSuccess => Status == SecretUnprotectStatus.Unprotected;

    /// <summary>
    /// Whether the stored value must be left alone. A secret that is merely
    /// unreadable <em>here</em> is still somebody's password somewhere, and
    /// overwriting it because this machine could not open it turns a nuisance
    /// into data loss.
    /// </summary>
    public bool ShouldPreserveStoredValue =>
        Status is SecretUnprotectStatus.TooNew
            or SecretUnprotectStatus.WrongScheme
            or SecretUnprotectStatus.Unavailable
            or SecretUnprotectStatus.Unreadable
            or SecretUnprotectStatus.Locked;

    /// <summary>A sentence for the shell to show, or null when nothing needs saying.</summary>
    public string? Notice => Status switch
    {
        SecretUnprotectStatus.Unprotected => null,
        SecretUnprotectStatus.NotASecret => null,
        SecretUnprotectStatus.TooNew =>
            "This password was saved by a newer version of Patchbay and cannot be read here. "
            + "It has been left untouched.",
        SecretUnprotectStatus.WrongScheme =>
            "This password was saved to a different credential store than the one in use. "
            + "It has been left untouched.",
        SecretUnprotectStatus.Unavailable =>
            "Windows data protection is not working for this account, so saved passwords "
            + "cannot be read and new ones cannot be saved.",
        SecretUnprotectStatus.Unreadable =>
            "This password was saved by a different Windows account or on a different machine, "
            + "so Patchbay cannot read it here. Enter it again to save a copy for this account.",
        SecretUnprotectStatus.Locked =>
            "This document is locked. Enter its master password to use the passwords saved in it.",
        _ => null,
    };

    /// <summary>A secret that was read back.</summary>
    public static SecretUnprotectResult Success(Secret secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        return new SecretUnprotectResult(SecretUnprotectStatus.Unprotected, secret);
    }

    /// <summary>A secret that was not.</summary>
    public static SecretUnprotectResult Failed(SecretUnprotectStatus status)
    {
        if (status == SecretUnprotectStatus.Unprotected)
        {
            throw new ArgumentException(
                "A successful read carries a secret; use Success instead.",
                nameof(status));
        }

        return new SecretUnprotectResult(status, secret: null);
    }

    /// <summary>
    /// The status and nothing else.
    ///
    /// Overridden deliberately: a record generates a <c>ToString</c> that
    /// prints every property it has, and one of this record's properties is a
    /// password. Left alone, the first log line or debugger watch that touched
    /// a result would put a cleartext password somewhere permanent — which is
    /// the whole thing M3 exists to prevent. See <c>M3-08</c> for the rest of
    /// the log-scrubbing policy.
    /// </summary>
    public override string ToString() => $"{nameof(SecretUnprotectResult)} {{ Status = {Status} }}";
}
