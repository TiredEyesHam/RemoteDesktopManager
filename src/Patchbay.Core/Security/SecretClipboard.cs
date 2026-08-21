using System.Globalization;

namespace Patchbay.Core.Security;

/// <summary>
/// Puts a sign-in on the clipboard and takes it off again (M3-09).
///
/// <para>
/// The clipboard is readable by every process on the desktop, and Windows
/// syncs it to the person's other machines unless that has been turned off. A
/// password on it is a password published, so the copy is deliberately
/// temporary: thirty seconds, which is long enough to reach a logon box and
/// short enough that walking away does not leave it there.
/// </para>
///
/// <para>
/// Keeping it out of clipboard history is a separate problem and a more
/// important one, because history survives the clear. That is a flag on the
/// data object rather than anything a timer can do, and it belongs to
/// <see cref="ISystemClipboard.SetSecret"/>.
/// </para>
///
/// <para>
/// Nothing here holds the password. It goes to the platform and is not kept,
/// which is why clearing is decided by <see cref="ISystemClipboard.Token"/>
/// rather than by reading the clipboard back and comparing.
/// </para>
/// </summary>
public sealed class SecretClipboard
{
    /// <summary>
    /// How long a password stays. Long enough to paste it, short enough that
    /// leaving the desk does not leave it behind.
    /// </summary>
    public static TimeSpan Window { get; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How often to try again when the clipboard will not be emptied. Giving
    /// up quietly would leave the password sitting there, which is the one
    /// outcome this whole type exists to prevent.
    /// </summary>
    private static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(1);

    private const int MaxAttempts = 5;

    private readonly ISystemClipboard _clipboard;

    private long _token;
    private TimeSpan _remaining;
    private int _attempts;

    /// <summary>
    /// Whether a password of ours is on the clipboard. Separate from
    /// <see cref="_remaining"/> on purpose: the countdown reaching zero is the
    /// moment the clipboard most needs clearing, and reading "still watching"
    /// off a positive remainder would make that the one moment it is not.
    /// </summary>
    private bool _watching;

    public SecretClipboard(ISystemClipboard clipboard)
    {
        ArgumentNullException.ThrowIfNull(clipboard);
        _clipboard = clipboard;
    }

    /// <summary>Whether copying is possible at all.</summary>
    public bool IsAvailable => _clipboard.IsAvailable;

    /// <summary>Whether a password of ours is on the clipboard, waiting to go.</summary>
    public bool IsCountingDown => _watching;

    /// <summary>How long is left, rounded up so that "1 second" is never shown for 0.4.</summary>
    public int SecondsLeft => _remaining > TimeSpan.Zero
        ? (int)Math.Ceiling(_remaining.TotalSeconds)
        : 0;

    /// <summary>
    /// What to tell somebody, or null when there is nothing to say. Replaced
    /// on every copy and every tick, so a status line can simply show it.
    /// </summary>
    public string? Notice { get; private set; }

    /// <summary>
    /// Copies a user name, which is not a secret and gets no countdown.
    ///
    /// <para>
    /// It does end one, though. Putting anything on the clipboard replaces
    /// what was there, so a password copied a moment ago has already gone and
    /// there is nothing left to clear.
    /// </para>
    /// </summary>
    public bool CopyUserName(string userName)
    {
        ArgumentException.ThrowIfNullOrEmpty(userName);

        Stop();

        if (!_clipboard.SetText(userName))
        {
            Notice = Refused;
            return false;
        }

        Notice = "User name copied.";
        return true;
    }

    /// <summary>
    /// Copies a password and starts the countdown.
    ///
    /// <para>
    /// Copying a second time restarts it rather than adding to it: what is on
    /// the clipboard is the second password, and the first one's countdown was
    /// measuring the life of something that is no longer there.
    /// </para>
    /// </summary>
    public bool CopyPassword(Secret password)
    {
        ArgumentNullException.ThrowIfNull(password);

        if (password.IsEmpty)
        {
            Notice = "There is no password on this connection to copy.";
            return false;
        }

        Stop();

        if (!_clipboard.SetSecret(password))
        {
            Notice = Refused;
            return false;
        }

        _token = _clipboard.Token;
        _remaining = Window;
        _attempts = 0;
        _watching = true;
        Notice = Counting();

        return true;
    }

    /// <summary>
    /// Moves the countdown on. Returns whether it is still worth ticking, so
    /// that whatever owns the clock can stop it.
    /// </summary>
    /// <param name="elapsed">
    /// Measured rather than assumed to be the interval, for the same reason as
    /// the reconnect countdown: a busy dispatcher or a machine that has been
    /// asleep delivers ticks late.
    /// </param>
    public bool Tick(TimeSpan elapsed)
    {
        if (!IsCountingDown)
        {
            return false;
        }

        if (HasBeenTakenOver())
        {
            return false;
        }

        _remaining -= elapsed;

        if (_remaining > TimeSpan.Zero)
        {
            Notice = Counting();
            return true;
        }

        return !ClearNow();
    }

    /// <summary>
    /// Empties the clipboard now, if what is on it is still the password this
    /// put there. Called by the countdown when it runs out, and on the way out
    /// of the application, because a password left on the clipboard by a
    /// process that has gone will never be cleared by anything.
    /// </summary>
    /// <returns>Whether the countdown is over, however it ended.</returns>
    public bool ClearNow()
    {
        if (!IsCountingDown)
        {
            return true;
        }

        if (HasBeenTakenOver())
        {
            return true;
        }

        if (_clipboard.Clear())
        {
            Stop();
            Notice = "The password has been cleared from the clipboard.";
            return true;
        }

        _attempts++;

        if (_attempts < MaxAttempts)
        {
            // Another program has the clipboard open. Wait a second and try
            // again rather than give up with a password still on it.
            _remaining = RetryAfter;
            Notice = "Waiting to clear the clipboard.";
            return false;
        }

        Stop();
        Notice =
            "The clipboard could not be cleared, so the password may still be on it. "
            + "Copy something else to be sure.";

        return true;
    }

    /// <summary>
    /// Whether somebody else has copied something since. What is on the
    /// clipboard is then not ours and must be left exactly alone: clearing it
    /// would throw away whatever they had just copied.
    /// </summary>
    private bool HasBeenTakenOver()
    {
        if (_clipboard.Token == _token)
        {
            return false;
        }

        Stop();
        Notice = "Something else was copied, so the password is no longer on the clipboard.";

        return true;
    }

    private void Stop()
    {
        _watching = false;
        _remaining = TimeSpan.Zero;
        _attempts = 0;
        _token = 0;
        Notice = null;
    }

    private string Counting()
    {
        int left = SecondsLeft;

        return string.Create(
            CultureInfo.CurrentCulture,
            $"Password copied. The clipboard clears in {left} second{(left == 1 ? "" : "s")}.");
    }

    private const string Refused =
        "The clipboard would not take it. Another program may be holding it open.";
}
