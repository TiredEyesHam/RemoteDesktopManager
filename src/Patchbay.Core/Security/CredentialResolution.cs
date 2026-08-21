using Patchbay.Core.Model;
using Patchbay.Core.Sessions;

namespace Patchbay.Core.Security;

/// <summary>
/// What came of looking up the sign-in for a connection (M3-01).
/// </summary>
public enum CredentialResolutionStatus
{
    /// <summary>
    /// A profile was found. Whether it carried a password is
    /// <see cref="SessionCredentials.HasPassword"/>, not a separate status: a
    /// profile with no saved password is configured correctly and simply needs
    /// asking, which is M3-05's job.
    /// </summary>
    Resolved = 0,

    /// <summary>
    /// Windows supplies the sign-in and nothing should ever be asked. The
    /// signed-in ticket goes over CredSSP, and naming an account would be
    /// asking for a logon prompt rather than avoiding one.
    /// </summary>
    SingleSignOn = 1,

    /// <summary>
    /// A profile was asked for and is not in this document — deleted while
    /// nodes still named it, or imported from a tree that had it. The
    /// connection can still be made by asking, so this is worth saying rather
    /// than worth refusing over.
    /// </summary>
    ProfileMissing = 2,

    /// <summary>
    /// The profile is there and its saved password would not open. Usually a
    /// different Windows account than the one that saved it, since M3-02
    /// protects to the current user. The stored value must be left alone.
    /// </summary>
    PasswordUnreadable = 3,

    /// <summary>
    /// The connection is set to ask every time and nothing has been typed yet
    /// (M3-05).
    ///
    /// Separate from <see cref="SingleSignOn"/>, which it used to share a
    /// status with, because they are opposite instructions: one means never
    /// ask and the other means always. Conflating them is how a connection set
    /// to prompt quietly connects with nothing and lands on the control's own
    /// logon screen — or, with network level authentication required,
    /// fails outright, because there is no logon screen to land on.
    /// </summary>
    AskEveryTime = 4,
}

/// <summary>
/// The sign-in for a connection, and what happened while finding it (M3-01).
///
/// Every status carries usable <see cref="Credentials"/>, including the two
/// that went wrong. A missing profile or an unreadable password is a reason to
/// ask somebody, not a reason to refuse to connect, and the connection still
/// needs whatever account name is known.
/// </summary>
public sealed record CredentialResolution
{
    public required CredentialResolutionStatus Status { get; init; }

    /// <summary>What to connect with. <see cref="SessionCredentials.None"/> when nothing is known.</summary>
    public required SessionCredentials Credentials { get; init; }

    /// <summary>The profile this came from, or null when there was none to find.</summary>
    public CredentialProfile? Profile { get; init; }

    /// <summary>Whether the connection can go ahead without asking anybody anything.</summary>
    public bool IsComplete =>
        Status is CredentialResolutionStatus.SingleSignOn
        || (Status is CredentialResolutionStatus.Resolved && Credentials.HasPassword);

    /// <summary>
    /// Whether a panel should be docked before connecting (M3-05).
    ///
    /// True for everything that is not already complete, including a profile
    /// found with no password saved against it. False for single sign-on,
    /// where asking would be asking a question Windows has already answered.
    /// </summary>
    public bool NeedsPrompt => !IsComplete;

    /// <summary>
    /// What the panel should say it is asking about, or null when nothing
    /// should be asked.
    /// </summary>
    public CredentialPromptReason? PromptReason => Status switch
    {
        CredentialResolutionStatus.SingleSignOn => null,
        CredentialResolutionStatus.ProfileMissing => CredentialPromptReason.ProfileMissing,
        CredentialResolutionStatus.PasswordUnreadable => CredentialPromptReason.Unreadable,
        _ when IsComplete => null,
        _ => CredentialPromptReason.Required,
    };

    /// <summary>
    /// Whether the stored password must be left exactly as it is. True for
    /// <see cref="CredentialResolutionStatus.PasswordUnreadable"/>: a blob
    /// this account cannot open is very likely one another account can, and
    /// overwriting it loses somebody else's password to save a round trip.
    /// </summary>
    public bool ShouldPreserveStoredPassword => Status is CredentialResolutionStatus.PasswordUnreadable;

    /// <summary>A sentence for a prompt or a notice bar, or null when nothing needs saying.</summary>
    public string? Notice => Status switch
    {
        CredentialResolutionStatus.ProfileMissing =>
            "The saved sign-in this connection uses is no longer in this file. "
            + "Choose another, or sign in for this session only.",

        CredentialResolutionStatus.PasswordUnreadable =>
            "The password saved for this sign-in cannot be read on this Windows account. "
            + "It was saved by a different account, or on a different computer.",

        _ => null,
    };

    public override string ToString() => $"{nameof(CredentialResolution)} {{ {Status}, {Credentials} }}";
}
