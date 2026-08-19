using System.Globalization;

namespace Patchbay.Core.Sessions;

/// <summary>
/// Turns the numbers the RDP control reports into something worth reading
/// (M4-07). Plugs straight into <see cref="SessionSignalRouter"/>'s
/// <c>describe</c> seam.
///
/// <para>
/// <b>Most of this is not a table, and that is the finding.</b> The obvious
/// shape for this task is a big switch from disconnect reason to sentence, and
/// writing one by hand produces confident wrong text. The reasons are
/// composed, not enumerated — 260, 516, 772, 1028, 1288 and 1540 all describe
/// the same "cannot find that computer" family, because the low byte is the
/// class and the high byte is the detail — so a hand-written table is either
/// enormous or wrong, and the way to find out which is to ask the control.
/// <c>GetErrorDescription(disconnectReason, extendedDisconnectReason)</c> is
/// Microsoft's own text for its own codes, and it arrives already translated
/// into the language Windows is running in. Patchbay cannot do either of those
/// things, so it does not try.
/// </para>
///
/// <para>
/// <b>What Patchbay does say for itself is the ordinary ending</b>, because
/// there the control is actively wrong. Asked about reason 1 — a disconnect
/// this computer asked for — it answers "An internal error has occurred", and
/// so it does for reason 2, someone signing out. Those are the two commonest
/// ways a session ends. Handing that sentence to somebody who just closed a
/// tab is worse than saying nothing, so the ordinary endings never reach the
/// control at all.
/// </para>
///
/// <para>
/// <b>And where nothing has verified a meaning, the number stands.</b> The
/// fatal-error codes have no table here for that reason: a plausible sentence
/// about a code nobody has checked is a worse outcome than a number somebody
/// can search for, because only one of the two is obviously incomplete.
/// </para>
/// </summary>
public sealed class SessionReasons
{
    /// <summary>The control did not say why. Not the same as nothing going wrong.</summary>
    public const int DisconnectNoInformation = 0;

    /// <summary>This computer asked for the disconnect.</summary>
    public const int DisconnectLocal = 1;

    /// <summary>Someone signed out or disconnected at the far end.</summary>
    public const int DisconnectRemoteByUser = 2;

    /// <summary>The server closed the session.</summary>
    public const int DisconnectByServer = 3;

    /// <summary>Access denied — the account was refused before any password was judged.</summary>
    public const int LogonAccessDenied = -1;

    /// <summary>
    /// <c>STATUS_LOGON_FAILURE</c>, 0xC000006D as a signed integer. The one
    /// people actually hit, and the reason the sign of a logon code is not a
    /// safe test for whether it is a problem (see
    /// <see cref="SessionSignalRouter.IsWinlogonNotice"/>).
    /// </summary>
    public const int LogonBadCredentials = -1073741715;

    private readonly Func<int, string?>? _fromControl;

    /// <param name="fromControl">
    /// The control's own description of a disconnect reason, or null when
    /// there is no control to ask — a fake session, or a test. Supplied by
    /// <c>Patchbay.Rdp</c>, which is the only layer that knows the reason has
    /// a second half to be read with it.
    /// </param>
    public SessionReasons(Func<int, string?>? fromControl = null)
    {
        _fromControl = fromControl;
    }

    /// <summary>
    /// The describer to hand <see cref="SessionSignalRouter"/>.
    /// </summary>
    public Func<SessionSignal, int, string> Describer => Describe;

    /// <summary>
    /// What to say about one announcement and its code.
    /// </summary>
    /// <remarks>
    /// The register is deliberately mixed. Patchbay's own answers are short
    /// sentences; the control's are Microsoft's paragraphs, passed through
    /// whole. Rewriting the latter into house style would mean rewriting them
    /// in every language Windows ships, which is not a thing Patchbay can do.
    /// </remarks>
    public string Describe(SessionSignal signal, int code) => signal switch
    {
        SessionSignal.Disconnected => Disconnect(code),
        SessionSignal.LogonError => LogonError(code),
        _ => ByNumber(code),
    };

    /// <summary>
    /// Collapses the control's whitespace. Its strings carry embedded newlines
    /// and doubled spaces from a time when they were laid out for a message
    /// box, and dropped into a status bar unedited they arrive with holes in
    /// them.
    /// </summary>
    public static string? Tidy(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return string.Join(' ', text.Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    /// <summary>
    /// Patchbay's own words for an ending that is not a fault, or null when
    /// this is not one of them. Public because the same three codes decide
    /// <see cref="SessionSignalRouter.IsOrdinaryDisconnect"/>, and the two
    /// must not come to different conclusions.
    /// </summary>
    public static string? OrdinaryEnding(int reason) => reason switch
    {
        DisconnectLocal => "The session was ended from this computer.",
        DisconnectRemoteByUser => "You were signed out of the remote computer.",
        DisconnectByServer => "The remote computer ended the session.",
        DisconnectNoInformation => "The remote computer did not say why.",
        _ => null,
    };

    private string Disconnect(int reason)
    {
        // Never asked of the control. It answers "An internal error has
        // occurred" for a session somebody deliberately ended, and that
        // sentence in front of someone who just closed a tab is worse than
        // silence.
        if (OrdinaryEnding(reason) is { } plain)
        {
            return plain;
        }

        return Tidy(_fromControl?.Invoke(reason)) ?? ByNumber(reason);
    }

    /// <summary>
    /// The two logon codes that have been pinned down, and numbers for the
    /// rest. More of them will be earned by <c>M4-10</c>, which has to tell a
    /// wrong password apart from a locked account to know whether re-prompting
    /// is any use.
    /// </summary>
    private static string LogonError(int code)
    {
        if (SessionSignalRouter.IsWinlogonNotice(code))
        {
            return "The remote computer is showing a sign-in prompt.";
        }

        return code switch
        {
            LogonBadCredentials => "The user name or password is not correct.",
            LogonAccessDenied => "The remote computer refused the account.",
            _ => ByNumber(code),
        };
    }

    private static string ByNumber(int code)
        => string.Create(CultureInfo.InvariantCulture, $"error code {code}");
}
