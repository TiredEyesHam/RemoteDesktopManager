namespace Patchbay.Core.Security;

/// <summary>
/// Why a stored secret could not be read (M3-02).
///
/// These are separate values because they lead to different sentences and
/// different next steps, and collapsing them is how someone ends up being told
/// their connection file is corrupt when in fact they are simply signed in as
/// a different Windows account than the one that saved the password.
/// </summary>
public enum SecretUnprotectStatus
{
    /// <summary>Read back successfully.</summary>
    Unprotected = 0,

    /// <summary>
    /// The text is not a protected secret at all. An empty field, or one
    /// somebody typed into by hand. Not an error on its own — most fields are
    /// not secrets.
    /// </summary>
    NotASecret = 1,

    /// <summary>
    /// A protected secret in an envelope format this version does not know,
    /// which means the file has been opened by a newer Patchbay. The secret is
    /// intact and must be left exactly as it is.
    /// </summary>
    TooNew = 2,

    /// <summary>
    /// Protected by a different scheme than the one asked — a Credential
    /// Manager entry (M3-04) handed to the DPAPI protector, say. Somebody else
    /// can read it; this protector cannot.
    /// </summary>
    WrongScheme = 3,

    /// <summary>
    /// There is no working protection on this machine or account, so nothing
    /// can be read and, more to the point, nothing may be written.
    /// </summary>
    Unavailable = 4,

    /// <summary>
    /// The right scheme refused the payload. Under DPAPI this is one of three
    /// things and they are indistinguishable from here: a different Windows
    /// account, a different machine, or an altered blob. All three mean the
    /// same thing to the person — this password has to be entered again.
    /// </summary>
    Unreadable = 5,
}
