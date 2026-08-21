namespace Patchbay.Core.Security;

/// <summary>
/// What happened when a master password was tried (M3-07).
///
/// <para>
/// Separate values because they lead to different sentences and different next
/// steps, and because collapsing them is how somebody gets told their document
/// is damaged when in fact they mistyped. The distinction that matters most is
/// between <see cref="WrongPassword"/>, which the person can fix by typing
/// again, and everything else, which they cannot.
/// </para>
/// </summary>
public enum MasterKeyStatus
{
    /// <summary>The password was right and the document key is available.</summary>
    Unlocked = 0,

    /// <summary>
    /// This document has no master password, so there is nothing to unlock.
    /// Not a failure: it is the ordinary state of a document nobody has
    /// protected.
    /// </summary>
    NotProtected = 1,

    /// <summary>
    /// The key would not unwrap. Under AES-GCM that means the tag did not
    /// verify, and the only sentence worth saying is that this is not the
    /// password — how nearly it was is not something to hint at.
    /// </summary>
    WrongPassword = 2,

    /// <summary>
    /// The key was derived by a function this build does not have, which means
    /// the document was protected by a newer Patchbay. The same distinction as
    /// <see cref="SecretUnprotectStatus.TooNew"/>: intact, not corrupt, and
    /// nothing here may write to it.
    /// </summary>
    UnknownKdf = 3,

    /// <summary>
    /// The record is not a usable one — a truncated blob, a salt of the wrong
    /// length, an iteration count outside anything sane. Either the file was
    /// hand-edited or it was damaged in transit, and no password will open it.
    /// </summary>
    Damaged = 4,
}
