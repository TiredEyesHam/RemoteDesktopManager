namespace Patchbay.Core.Sessions;

/// <summary>
/// What a refused sign-in means for whether asking again is any use (M4-10).
/// </summary>
public enum LogonOutcome
{
    /// <summary>
    /// Winlogon narrating itself. Nothing went wrong and nothing is being
    /// asked — see <see cref="SessionSignalRouter.IsWinlogonNotice"/>.
    /// </summary>
    Notice = 0,

    /// <summary>
    /// The credentials were wrong. A different password may work, so this is
    /// the case a re-prompt exists for.
    /// </summary>
    WrongCredentials = 1,

    /// <summary>
    /// The account cannot be used whatever is typed at it — locked, disabled,
    /// expired, out of hours, or barred from this machine.
    /// </summary>
    AccountUnusable = 2,

    /// <summary>
    /// A code this repo has not pinned down. Asking again is allowed and
    /// nothing is claimed about why it failed.
    /// </summary>
    Unknown = 3,
}

/// <summary>
/// Reads the number that comes with <c>OnLogonError</c> (M4-10).
///
/// The question is whether asking again is any use, not what went wrong. A
/// wrong password is worth a re-prompt; a locked account is not, and offering
/// one is how somebody types their correct password three more times into an
/// account that will not open.
///
/// These are NTSTATUS values from <c>ntstatus.h</c>. What was in doubt is
/// whether the control passes them through unchanged, and
/// <c>STATUS_LOGON_FAILURE</c> arriving as -1073741715 says it does. Anything
/// not listed is <see cref="LogonOutcome.Unknown"/> rather than assumed, and
/// Unknown still permits a re-prompt: a person typing a password once more is
/// not the lockout risk an automatic retry loop is, which is why M4-08 is
/// stricter than this.
///
/// Nothing here retries by itself. Every outcome describes what to offer.
/// </summary>
public static class LogonFailure
{
    /// <summary>Wrong user name or password. <c>STATUS_LOGON_FAILURE</c>, 0xC000006D.</summary>
    public const int StatusLogonFailure = -1073741715;

    /// <summary>
    /// Access denied. The one non-NTSTATUS code in the set, pinned by M4-06
    /// because it is what a refused account actually produces.
    /// </summary>
    public const int AccessDenied = -1;

    /// <summary>The account may not sign in this way. <c>STATUS_ACCOUNT_RESTRICTION</c>, 0xC000006E.</summary>
    public const int AccountRestriction = -1073741714;

    /// <summary>Not at this hour. <c>STATUS_INVALID_LOGON_HOURS</c>, 0xC000006F.</summary>
    public const int InvalidLogonHours = -1073741713;

    /// <summary>Not from this machine. <c>STATUS_INVALID_WORKSTATION</c>, 0xC0000070.</summary>
    public const int InvalidWorkstation = -1073741712;

    /// <summary>The password has expired. <c>STATUS_PASSWORD_EXPIRED</c>, 0xC0000071.</summary>
    public const int PasswordExpired = -1073741711;

    /// <summary>The account is switched off. <c>STATUS_ACCOUNT_DISABLED</c>, 0xC0000072.</summary>
    public const int AccountDisabled = -1073741710;

    /// <summary>The account has expired. <c>STATUS_ACCOUNT_EXPIRED</c>, 0xC0000193.</summary>
    public const int AccountExpired = -1073741421;

    /// <summary>The password must be changed first. <c>STATUS_PASSWORD_MUST_CHANGE</c>, 0xC0000224.</summary>
    public const int PasswordMustChange = -1073741276;

    /// <summary>Too many wrong answers already. <c>STATUS_ACCOUNT_LOCKED_OUT</c>, 0xC0000234.</summary>
    public const int AccountLockedOut = -1073741260;

    /// <summary>
    /// What <paramref name="logonError"/> means for trying again.
    ///
    /// Expired and must-change count as unusable rather than wrong, which is
    /// the judgement call here. The password on file may be correct in both
    /// cases; what is needed is a change, and that happens at the far end.
    /// </summary>
    public static LogonOutcome Classify(int logonError)
    {
        if (SessionSignalRouter.IsWinlogonNotice(logonError))
        {
            return LogonOutcome.Notice;
        }

        return logonError switch
        {
            StatusLogonFailure or AccessDenied => LogonOutcome.WrongCredentials,

            AccountRestriction
                or InvalidLogonHours
                or InvalidWorkstation
                or PasswordExpired
                or AccountDisabled
                or AccountExpired
                or PasswordMustChange
                or AccountLockedOut => LogonOutcome.AccountUnusable,

            _ => LogonOutcome.Unknown,
        };
    }

    /// <summary>
    /// Whether it is worth offering to sign in again with something different.
    /// True for a refusal a different password could fix and for a code nobody
    /// has pinned down; false for an account that will not open however many
    /// times it is asked.
    /// </summary>
    public static bool IsWorthAskingAgain(int logonError)
        => Classify(logonError) is LogonOutcome.WrongCredentials or LogonOutcome.Unknown;
}
