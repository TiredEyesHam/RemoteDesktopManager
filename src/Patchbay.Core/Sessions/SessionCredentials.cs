using Patchbay.Core.Security;



namespace Patchbay.Core.Sessions;

/// <summary>
/// The sign-in for one connection attempt (M4-10).
///
/// <para>
/// <b>Deliberately not part of the document, and not part of a node.</b>
/// <see cref="Patchbay.Core.Model.ConnectionSettings"/> holds a user name, a
/// domain and the id of a credential profile, and it holds no password —
/// M3-02 exists so that a stored secret is a protected blob somewhere else.
/// This type is the other end of that: what an attempt was actually given,
/// assembled at the moment of connecting from a profile, a prompt, or a
/// re-prompt after the far end said no, and thrown away with the attempt.
/// </para>
///
/// <para>
/// It is a record, so two attempts with the same sign-in compare equal — which
/// is the question the re-prompt has to answer before it offers to try again.
/// Reconnecting with the credentials that were just refused is not a retry, it
/// is the same failure a second time, and enough of those locks the account
/// (M4-08 has the same rule for the automatic case).
/// </para>
///
/// <para>
/// The password is a <see cref="Secret"/> rather than a <see cref="string"/>,
/// which is M3-03's half of this. A string cannot be erased and a session's
/// sign-in is kept for as long as the tab is open, so that copy was the
/// longest-lived one in the application. Cleartext still exists at connect
/// time, because the RDP control takes a BSTR and nothing at this layer can
/// change that; what changes is that the copy Patchbay holds can be wiped when
/// the session ends instead of waiting for a collection that may never come.
/// </para>
/// </summary>
public sealed record SessionCredentials
{
    /// <summary>Nothing supplied — the control asks, or Windows answers for it.</summary>
    public static SessionCredentials None { get; } = new();

    /// <summary>The account to sign in as. Empty when nothing was supplied.</summary>
    public string UserName { get; init; } = string.Empty;

    /// <summary>The domain that goes with it, or empty for a local account.</summary>
    public string Domain { get; init; } = string.Empty;

    /// <summary>
    /// The password. Never printed, never serialised, never logged.
    ///
    /// <para>
    /// Owned by the session this belongs to, which erases it when it ends.
    /// Copies made with <c>with</c> share the same buffer, which is safe
    /// because erasing destroys the plaintext and not the identity: a disposed
    /// secret can still say whether something equals it, which is the only
    /// thing a copy is kept for.
    /// </para>
    /// </summary>
    public Secret Password { get; init; } = Secret.Empty;

    /// <summary>Whether there is a password to hand over.</summary>
    public bool HasPassword => !Password.IsEmpty;

    /// <summary>Whether there is anything here at all.</summary>
    public bool IsEmpty => UserName.Length == 0 && Domain.Length == 0 && Password.IsEmpty;

    /// <summary>
    /// The account as a person would write it, for a prompt or a status line.
    /// Never includes the password.
    /// </summary>
    public string Display => Domain.Length > 0 && UserName.Length > 0
        ? Domain + "\\" + UserName
        : UserName;

    /// <summary>
    /// Whether a sign-in somebody is typing is the one already held, asked
    /// without building a <see cref="Secret"/> to ask with.
    ///
    /// <para>
    /// This is what a re-prompt calls on every keystroke to decide whether
    /// Connect should be enabled (M3-06). Going through
    /// <see cref="Secret.Matches"/> means the refused password can be erased
    /// while the question stays answerable, and means no pinned buffer is
    /// allocated for a comparison that is thrown away immediately.
    /// </para>
    /// </summary>
    public bool Matches(string userName, string domain, ReadOnlySpan<char> password) =>
        string.Equals(UserName, userName, StringComparison.Ordinal)
        && string.Equals(Domain, domain, StringComparison.Ordinal)
        && Password.Matches(password);

    /// <summary>
    /// Erases the password (M3-03).
    ///
    /// <para>
    /// Called by whatever owned this sign-in when it is finished with it: a
    /// session that is ending, or a session being handed a different sign-in
    /// after this one was refused. What is destroyed is the plaintext and not
    /// the identity, so a prompt that kept this to compare against goes on
    /// being able to say "that is the one that was just refused".
    /// </para>
    ///
    /// <para>
    /// Safe on <see cref="None"/> and safe to call twice. A sign-in that never
    /// reached a session is not erased and waits for a collection, the same as
    /// it did before this existed.
    /// </para>
    /// </summary>
    public void Forget() => Password.Dispose();

    /// <summary>
    /// Redacted, and overridden precisely because the default for a record is
    /// to print every property. A record's generated <c>ToString</c> is the
    /// most likely way a password reaches a log file, and it would arrive
    /// there through a line of code nobody wrote.
    /// </summary>
    public override string ToString() => IsEmpty
        ? $"{nameof(SessionCredentials)} {{ none }}"
        : $"{nameof(SessionCredentials)} {{ {Display}, password {(HasPassword ? "supplied" : "none")} }}";
}
