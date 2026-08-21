namespace Patchbay.Core.Security;

/// <summary>
/// The outcome of trying a master password (M3-07) — the document key when it
/// was the right one, and a reason when it was not.
///
/// <para>
/// A result rather than an exception, for the same reason as
/// <see cref="SecretUnprotectResult"/>: mistyping a password is the most
/// ordinary thing that can happen here, and it is not exceptional.
/// </para>
/// </summary>
public sealed record MasterKeyResult
{
    private MasterKeyResult(MasterKeyStatus status, Secret? key)
    {
        Status = status;
        Key = key;
    }

    /// <summary>What happened.</summary>
    public MasterKeyStatus Status { get; }

    /// <summary>
    /// The document key, or null when there is none.
    ///
    /// <para>
    /// Thirty-two random bytes rather than anything typed, held in a
    /// <see cref="Secret"/> because that is the erasable pinned buffer this
    /// codebase already has (M3-03). The name says password and the contents
    /// are a key; what the type actually provides — a buffer that can be
    /// written over, and that never prints itself — is what a key needs too.
    /// </para>
    /// </summary>
    public Secret? Key { get; }

    /// <summary>Whether there is a key to use.</summary>
    public bool IsSuccess => Status == MasterKeyStatus.Unlocked;

    /// <summary>
    /// Whether trying a different password could possibly help. False for a
    /// damaged record and for one this build cannot read, where asking again
    /// is only cruelty.
    /// </summary>
    public bool IsWorthRetrying => Status == MasterKeyStatus.WrongPassword;

    /// <summary>A sentence to show, or null when nothing needs saying.</summary>
    public string? Notice => NoticeFor(Status);

    /// <summary>
    /// The sentence for a status on its own.
    ///
    /// <para>
    /// Static because <see cref="DocumentProtection.Unlock"/> hands back a
    /// status and not a result: it has taken ownership of the key, and a
    /// result carrying a key that somebody else now owns is a double-free
    /// waiting to be written.
    /// </para>
    /// </summary>
    public static string? NoticeFor(MasterKeyStatus status) => status switch
    {
        MasterKeyStatus.Unlocked => null,
        MasterKeyStatus.NotProtected => null,
        MasterKeyStatus.WrongPassword => "That is not the master password for this document.",
        MasterKeyStatus.UnknownKdf =>
            "This document was protected by a newer version of Patchbay and cannot be opened "
            + "here. It has been left untouched.",
        MasterKeyStatus.Damaged =>
            "The master key in this document could not be read, so no password will open it. "
            + "Restore the document from a backup — Patchbay keeps five.",
        _ => null,
    };

    /// <summary>The right password.</summary>
    public static MasterKeyResult Unlocked(Secret key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return new MasterKeyResult(MasterKeyStatus.Unlocked, key);
    }

    /// <summary>Anything else.</summary>
    public static MasterKeyResult Failed(MasterKeyStatus status)
    {
        if (status == MasterKeyStatus.Unlocked)
        {
            throw new ArgumentException(
                "A successful unlock carries a key; use Unlocked instead.",
                nameof(status));
        }

        return new MasterKeyResult(status, key: null);
    }

    /// <summary>
    /// The status and nothing else. Overridden for the same reason as
    /// <see cref="SecretUnprotectResult.ToString"/>: a record prints every
    /// property it has, and one of these is the key to every password in the
    /// document.
    /// </summary>
    public override string ToString() => $"{nameof(MasterKeyResult)} {{ Status = {Status} }}";
}
