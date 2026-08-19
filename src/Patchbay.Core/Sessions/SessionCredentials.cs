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
/// <b>The password is a <see cref="string"/> and that is not an oversight.</b>
/// The RDP control takes it as a BSTR, so cleartext exists in managed memory
/// at connect time no matter what shape it is held in beforehand; a
/// <c>SecureString</c> here would buy the appearance of care and one more
/// marshalling step. Shortening the life of that string is M3-03. What this
/// type can do, and does, is refuse to print it.
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

    /// <summary>The password, in the clear. Never printed, never serialised, never logged.</summary>
    public string Password { get; init; } = string.Empty;

    /// <summary>Whether there is a password to hand over.</summary>
    public bool HasPassword => Password.Length > 0;

    /// <summary>Whether there is anything here at all.</summary>
    public bool IsEmpty => UserName.Length == 0 && Domain.Length == 0 && Password.Length == 0;

    /// <summary>
    /// The account as a person would write it, for a prompt or a status line.
    /// Never includes the password.
    /// </summary>
    public string Display => Domain.Length > 0 && UserName.Length > 0
        ? Domain + "\\" + UserName
        : UserName;

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
