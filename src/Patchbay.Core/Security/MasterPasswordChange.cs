using System.Globalization;

namespace Patchbay.Core.Security;

/// <summary>
/// What became of a document when its master password was set, changed or
/// removed (M3-07).
///
/// <para>
/// Three successes rather than one, because they are three different sentences
/// and the person doing it is entitled to know which happened. Changing a
/// master password moves no saved passwords at all, and saying "0 passwords
/// re-protected" about that would read like a failure.
/// </para>
/// </summary>
public enum MasterPasswordChangeStatus
{
    /// <summary>A master password was set on a document that had none.</summary>
    Protected = 0,

    /// <summary>
    /// The master password was replaced. Nothing else moved: the document key
    /// is unchanged and only its wrapping was redone, which is the reason the
    /// scheme has two keys in it.
    /// </summary>
    Changed = 1,

    /// <summary>The master password was removed and the saved passwords went back to the machine.</summary>
    Unprotected = 2,

    /// <summary>
    /// Shorter than <see cref="DocumentProtection.MinimumPasswordLength"/>.
    /// Nothing has changed.
    /// </summary>
    PasswordTooShort = 3,

    /// <summary>The current master password was not given correctly, so nothing has changed.</summary>
    WrongPassword = 4,

    /// <summary>The record was written by a newer Patchbay. Nothing has been touched.</summary>
    UnknownKdf = 5,

    /// <summary>The record cannot be read at all, so the document key is beyond reach.</summary>
    Damaged = 6,

    /// <summary>
    /// Removing the master password would leave the saved passwords with
    /// nowhere to go, because this machine has no working data protection
    /// either. Refused rather than done, because the only ways to finish are
    /// losing them or writing them in the clear.
    /// </summary>
    NowhereToPutPasswords = 7,
}

/// <summary>
/// The outcome of setting, changing or removing a master password (M3-07),
/// including how many saved passwords moved with it.
///
/// <para>
/// The counts are not decoration. Setting a master password on a document that
/// holds a password this Windows account cannot read leaves that one exactly
/// where it is — <c>M3-01</c>'s rule, that a blob this account cannot open is
/// very likely one another account can — so the document ends up with some
/// secrets behind the master password and some not. Somebody who has just
/// turned one on believes everything is behind it, and the only honest thing
/// to do is say how many are.
/// </para>
/// </summary>
public sealed record MasterPasswordChange
{
    private MasterPasswordChange(MasterPasswordChangeStatus status, int moved, int leftAlone)
    {
        Status = status;
        Moved = moved;
        LeftAlone = leftAlone;
    }

    /// <summary>What happened.</summary>
    public MasterPasswordChangeStatus Status { get; }

    /// <summary>How many saved passwords were re-protected under the new scheme.</summary>
    public int Moved { get; }

    /// <summary>
    /// How many could not be read here and were left exactly as they were.
    /// Never overwritten: unreadable here is still somebody's password
    /// somewhere.
    /// </summary>
    public int LeftAlone { get; }

    /// <summary>Whether the document changed and needs saving.</summary>
    public bool IsSuccess => Status
        is MasterPasswordChangeStatus.Protected
        or MasterPasswordChangeStatus.Changed
        or MasterPasswordChangeStatus.Unprotected;

    /// <summary>A sentence to show. Never null — something always happened.</summary>
    public string Notice => Status switch
    {
        MasterPasswordChangeStatus.Protected =>
            "This document now has a master password. " + Carried("behind it"),
        MasterPasswordChangeStatus.Changed =>
            "The master password has been changed. The saved passwords did not have to move.",
        MasterPasswordChangeStatus.Unprotected =>
            "The master password has been removed. " + Carried("protected for this Windows account"),
        MasterPasswordChangeStatus.PasswordTooShort =>
            $"A master password needs at least {DocumentProtection.MinimumPasswordLength} "
            + "characters. It is the one password protecting every other one in this document.",
        MasterPasswordChangeStatus.WrongPassword =>
            "That is not the master password for this document, so nothing has changed.",
        MasterPasswordChangeStatus.UnknownKdf =>
            "This document was protected by a newer version of Patchbay. It has been left "
            + "untouched.",
        MasterPasswordChangeStatus.Damaged =>
            "The master key in this document could not be read, so no password will open it. "
            + "Restore the document from a backup — Patchbay keeps five.",
        MasterPasswordChangeStatus.NowhereToPutPasswords =>
            "Windows data protection is not working for this account, so removing the master "
            + "password would leave the saved passwords with nowhere safe to go. They have been "
            + "left where they are.",
        _ => "Nothing happened.",
    };

    internal static MasterPasswordChange Done(
        MasterPasswordChangeStatus status,
        int moved,
        int leftAlone) => new(status, moved, leftAlone);

    internal static MasterPasswordChange Failed(MasterPasswordChangeStatus status)
    {
        if (status is MasterPasswordChangeStatus.Protected
            or MasterPasswordChangeStatus.Changed
            or MasterPasswordChangeStatus.Unprotected)
        {
            throw new ArgumentException(
                "A successful change carries counts; use Done instead.",
                nameof(status));
        }

        return new MasterPasswordChange(status, moved: 0, leftAlone: 0);
    }

    /// <summary>
    /// How many saved passwords came along, and how many did not. The second
    /// half only appears when there is one, because a caveat that is usually
    /// absent is one people read when it is there.
    /// </summary>
    private string Carried(string where)
    {
        string moved = Moved == 0
            ? "There were no saved passwords to move."
            : string.Create(
                CultureInfo.CurrentCulture,
                $"{Moved} saved password{(Moved == 1 ? " is" : "s are")} now {where}.");

        if (LeftAlone == 0)
        {
            return moved;
        }

        return string.Create(
            CultureInfo.CurrentCulture,
            $"{moved} {LeftAlone} could not be read on this Windows account and "
            + $"{(LeftAlone == 1 ? "was" : "were")} left untouched, so "
            + $"{(LeftAlone == 1 ? "it is" : "they are")} not.");
    }
}
