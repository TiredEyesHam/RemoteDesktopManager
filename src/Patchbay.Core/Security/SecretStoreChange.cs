using System.Globalization;

namespace Patchbay.Core.Security;

/// <summary>
/// What became of a document when it was moved between machine stores
/// (M3-04).
/// </summary>
public enum SecretStoreChangeStatus
{
    /// <summary>The document now writes to the store asked for.</summary>
    Moved = 0,

    /// <summary>It already did, so nothing was touched.</summary>
    AlreadyThere = 1,

    /// <summary>
    /// No store on this machine goes by that name. A document that names one
    /// this build has never heard of, or a caller with a typo — the same
    /// answer either way, because there is nothing to be done about it here.
    /// </summary>
    NoSuchStore = 2,

    /// <summary>
    /// The store is there and not working. Refused rather than half done: the
    /// saved passwords would have been read out of somewhere that works and
    /// then had nowhere to go.
    /// </summary>
    Unavailable = 3,

    /// <summary>
    /// The document has a master password and it is the master password that
    /// holds the saved passwords, so which machine store is preferred decides
    /// nothing until it is removed.
    /// </summary>
    Locked = 4,
}

/// <summary>
/// The outcome of choosing where a document keeps its saved passwords
/// (M3-04), and how many came along.
///
/// <para>
/// The counts are here for the same reason they are on
/// <see cref="MasterPasswordChange"/>, and one reason more. A password this
/// Windows account cannot read stays exactly where it is (M3-01), so the
/// document ends up mixed — and mixed across machine stores is worse to be
/// wrong about than mixed across a master password, because both halves look
/// equally saved and only one of them is in the place the person just chose.
/// </para>
/// </summary>
public sealed record SecretStoreChange
{
    private SecretStoreChange(SecretStoreChangeStatus status, int moved, int leftAlone)
    {
        Status = status;
        Moved = moved;
        LeftAlone = leftAlone;
    }

    /// <summary>What happened.</summary>
    public SecretStoreChangeStatus Status { get; }

    /// <summary>How many saved passwords were re-protected into the new store.</summary>
    public int Moved { get; }

    /// <summary>
    /// How many could not be read here and were left exactly as they were.
    /// </summary>
    public int LeftAlone { get; }

    /// <summary>Whether the document is now writing where it was asked to.</summary>
    public bool IsSuccess =>
        Status is SecretStoreChangeStatus.Moved or SecretStoreChangeStatus.AlreadyThere;

    /// <summary>
    /// Whether anything about the document changed, which is a narrower
    /// question than whether it worked and is the one that decides a save.
    /// </summary>
    public bool ChangedTheDocument => Status is SecretStoreChangeStatus.Moved;

    /// <summary>A sentence to show.</summary>
    public string Notice => Status switch
    {
        SecretStoreChangeStatus.Moved => Carried(),
        SecretStoreChangeStatus.AlreadyThere =>
            "This document already keeps its saved passwords there.",
        SecretStoreChangeStatus.NoSuchStore =>
            "This version of Patchbay does not have that password store, so nothing has changed.",
        SecretStoreChangeStatus.Unavailable =>
            "That password store is not working on this machine, so the saved passwords have "
            + "been left where they are.",
        SecretStoreChangeStatus.Locked =>
            "This document has a master password, and that is what protects the passwords saved "
            + "in it. Remove it first to choose a Windows store instead.",
        _ => "Nothing happened.",
    };

    internal static SecretStoreChange Done(int moved, int leftAlone) =>
        new(SecretStoreChangeStatus.Moved, moved, leftAlone);

    internal static SecretStoreChange Failed(SecretStoreChangeStatus status)
    {
        if (status is SecretStoreChangeStatus.Moved)
        {
            throw new ArgumentException(
                "A move carries counts; use Done instead.",
                nameof(status));
        }

        return new SecretStoreChange(status, moved: 0, leftAlone: 0);
    }

    private string Carried()
    {
        string moved = Moved == 0
            ? "Saved passwords will go there from now on. There were none to move."
            : string.Create(
                CultureInfo.CurrentCulture,
                $"{Moved} saved password{(Moved == 1 ? " has" : "s have")} been moved there.");

        if (LeftAlone == 0)
        {
            return moved;
        }

        return string.Create(
            CultureInfo.CurrentCulture,
            $"{moved} {LeftAlone} could not be read on this Windows account and "
            + $"{(LeftAlone == 1 ? "was" : "were")} left where "
            + $"{(LeftAlone == 1 ? "it is" : "they are")}.");
    }
}
