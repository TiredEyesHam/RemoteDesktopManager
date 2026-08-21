using Patchbay.Core.Security;

namespace Patchbay.Core.Sessions;

/// <summary>
/// Why a sign-in is being asked for, which decides what the panel says
/// (M3-06).
/// </summary>
public enum CredentialPromptReason
{
    /// <summary>Nothing saved, and the connection is set to ask each time.</summary>
    Required = 0,

    /// <summary>The far end refused what was sent, and the session is still up (M4-10).</summary>
    Refused = 1,

    /// <summary>A saved password exists and this Windows account cannot read it (M3-01).</summary>
    Unreadable = 2,

    /// <summary>The profile a connection names is not in this document (M3-01).</summary>
    ProfileMissing = 3,
}

/// <summary>
/// What a docked credential panel is asking, and the rules about the answer
/// (M3-06).
///
/// Lives in <c>Core</c> with no notion of a panel, because everything worth
/// getting wrong here is a rule rather than a control: whether an answer may
/// be submitted at all, whether it is the sign-in that was just refused, and
/// whether saving it is even possible. The shell wraps this in something
/// bindable; the tests reach it directly.
///
/// It carries a typed password because a panel has to hold one somewhere
/// between the keystroke and the connect. <see cref="Forget"/> exists so that
/// window is as short as the caller cares to make it, and nothing here prints
/// it (M3-03 is the rest of that problem).
/// </summary>
public sealed class CredentialPrompt
{
    /// <summary>
    /// Builds a prompt for one attempt.
    /// </summary>
    /// <param name="endpoint">Which machine is asking, for the panel's heading.</param>
    /// <param name="reason">Why it is being asked.</param>
    /// <param name="known">
    /// What is already known, used to fill the boxes in. The password is never
    /// carried over even when it is there: pre-filling the one that was just
    /// refused invites somebody to press Connect again without reading.
    /// </param>
    /// <param name="canSave">
    /// Whether saving is possible at all, from
    /// <c>CredentialVault.CanSavePasswords</c>. When false the panel must not
    /// offer it rather than offering it and failing (M3-02).
    /// </param>
    public CredentialPrompt(
        string endpoint,
        CredentialPromptReason reason,
        SessionCredentials? known = null,
        bool canSave = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);

        Endpoint = endpoint;
        Reason = reason;
        CanOfferToSave = canSave;
        Refused = reason is CredentialPromptReason.Refused ? known : null;

        UserName = known?.UserName ?? string.Empty;
        Domain = known?.Domain ?? string.Empty;
    }

    /// <summary>Which machine is asking.</summary>
    public string Endpoint { get; }

    /// <summary>Why.</summary>
    public CredentialPromptReason Reason { get; }

    /// <summary>Whether the panel may offer to save what is typed.</summary>
    public bool CanOfferToSave { get; }

    /// <summary>
    /// The sign-in the far end refused, when there was one. Kept so that
    /// pressing Connect on an unchanged answer can be refused rather than sent
    /// (M4-10): resubmitting is not a retry, and enough of them lock the
    /// account.
    /// </summary>
    public SessionCredentials? Refused { get; }

    /// <summary>The account being offered. Bound to a text box.</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>The domain, or empty for a local account.</summary>
    public string Domain { get; set; } = string.Empty;

    /// <summary>What was typed. Never pre-filled, never printed.</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Whether to keep this password for next time. Only meaningful when
    /// <see cref="CanOfferToSave"/>, and forced false otherwise so that a
    /// panel which forgets to hide the box cannot promise something that will
    /// not happen.
    /// </summary>
    public bool SavePassword
    {
        get;
        set => field = value && CanOfferToSave;
    }

    /// <summary>What the panel should say about why it is here.</summary>
    public string Title => Reason switch
    {
        CredentialPromptReason.Refused => $"{Endpoint} did not accept that sign-in",
        CredentialPromptReason.Unreadable => $"The saved password for {Endpoint} cannot be read",
        CredentialPromptReason.ProfileMissing => $"{Endpoint} has no saved sign-in any more",
        _ => $"Sign in to {Endpoint}",
    };

    /// <summary>
    /// Whether this panel went up before the session was connected (M3-05), as
    /// opposed to over one the far end has already refused (M4-10).
    ///
    /// It decides what the second button means, and the two meanings are not
    /// close. Before connecting there is a way past: the server has its own
    /// logon screen, and somebody who does not want to type into Patchbay can
    /// go and use it. After a refusal there is nothing to go past — the
    /// screen they would land on is the one that just said no.
    /// </summary>
    public bool IsBeforeConnecting => Reason is not CredentialPromptReason.Refused;

    /// <summary>
    /// What the second button says. Never "Cancel": on a panel raised before
    /// connecting, dismissing it connects anyway, and a button labelled Cancel
    /// that starts a connection is the worst kind of surprise.
    /// </summary>
    public string DismissLabel => IsBeforeConnecting ? "Connect without one" : "Not now";

    /// <summary>The line under the title, or null when the title says enough.</summary>
    public string? Detail => Reason switch
    {
        CredentialPromptReason.Refused =>
            "The session is still open. Try a different password, or a different account.",

        CredentialPromptReason.Unreadable =>
            "It was saved by a different Windows account, or on a different computer. "
            + "The saved password has been left alone.",

        CredentialPromptReason.ProfileMissing =>
            "The saved sign-in it used has been deleted. Sign in for this session, "
            + "or pick another in the connection's settings.",

        _ => null,
    };

    /// <summary>
    /// The answer as something to connect with.
    ///
    /// The password is taken as typed and the account is trimmed, because a
    /// name pasted out of a spreadsheet arrives with a space on the end and a
    /// password legitimately may.
    ///
    /// <para>
    /// This is where the typed password stops being a <see cref="string"/> and
    /// becomes a <see cref="Secret"/> (M3-03). Once, at the point of
    /// answering, rather than on every keystroke: the box hands over a fresh
    /// string each time it is read, and turning each of those into a pinned
    /// buffer would be a great deal of churn to shorten the life of a copy
    /// that WPF made and Patchbay cannot reach.
    /// </para>
    /// </summary>
    public SessionCredentials ToCredentials() => new()
    {
        UserName = UserName.Trim(),
        Domain = Domain.Trim(),
        Password = Secret.From(Password),
    };

    /// <summary>
    /// Whether this is word for word what the far end just refused. True is a
    /// reason to stop, not a reason to warn and continue.
    ///
    /// <para>
    /// Asked without building the answer, because it is asked on every
    /// keystroke. It also goes on being answerable after the refused password
    /// has been erased, since what is compared is the fingerprint and not the
    /// plaintext.
    /// </para>
    /// </summary>
    public bool IsUnchanged =>
        Refused is { } refused && refused.Matches(UserName.Trim(), Domain.Trim(), Password);

    /// <summary>
    /// Whether pressing Connect should do anything. False for an empty answer,
    /// and false for one that repeats a refusal.
    /// </summary>
    public bool CanSubmit => (UserName.Trim().Length > 0 || Password.Length > 0) && !IsUnchanged;

    /// <summary>
    /// Whether the panel should say why Connect is disabled. Only for the
    /// repeat: an empty box explains itself, and a sentence about it would be
    /// pointing at something the person is already looking at.
    /// </summary>
    public string? Obstacle => IsUnchanged
        ? "That is the sign-in that was just refused. Change something before trying again."
        : null;

    /// <summary>
    /// Drops the typed password. Called once it has been handed to a
    /// connection attempt, so the panel is not still holding it while the
    /// session runs.
    /// </summary>
    public void Forget() => Password = string.Empty;

    /// <summary>Redacted, like everything else that could carry a password into a log.</summary>
    public override string ToString()
        => $"{nameof(CredentialPrompt)} {{ {Endpoint}, {Reason}, {ToCredentials().Display} }}";
}
