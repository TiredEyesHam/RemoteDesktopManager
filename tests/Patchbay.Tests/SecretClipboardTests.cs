using Patchbay.Core.Security;

namespace Patchbay.Tests;

/// <summary>
/// Copying a sign-in, and taking it back off again (M3-09).
///
/// <para>
/// Two rules carry the whole thing. A password must not be left on the
/// clipboard, and whatever somebody else copied must not be thrown away. They
/// pull against each other, which is why the decision is not "clear after
/// thirty seconds" but "clear after thirty seconds if it is still ours".
/// </para>
/// </summary>
public class SecretClipboardTests
{
    private const string Password = "hunter2-correct-horse";

    /// <summary>
    /// A clipboard that can be watched and made to misbehave. The real one is
    /// a shared resource another process can hold open, so refusing is an
    /// ordinary thing for it to do and has to be tested.
    /// </summary>
    private sealed class FakeClipboard : ISystemClipboard
    {
        public bool IsAvailable { get; init; } = true;

        public bool RefusesToWrite { get; set; }

        public bool RefusesToClear { get; set; }

        public string? Contents { get; private set; }

        /// <summary>Whether what is on it went on with the history exclusions.</summary>
        public bool ContentsAreMarkedSecret { get; private set; }

        public long Token { get; private set; }

        public int Clears { get; private set; }

        public bool SetSecret(Secret secret) => Put(secret.RevealAsString(), secret: true);

        public bool SetText(string text) => Put(text, secret: false);

        public bool Clear()
        {
            if (RefusesToClear)
            {
                return false;
            }

            Clears++;
            Contents = null;
            ContentsAreMarkedSecret = false;
            Token++;

            return true;
        }

        /// <summary>Another program, or the person, copying something of their own.</summary>
        public void SomebodyElseCopies(string what)
        {
            Contents = what;
            ContentsAreMarkedSecret = false;
            Token++;
        }

        private bool Put(string text, bool secret)
        {
            if (RefusesToWrite)
            {
                return false;
            }

            Contents = text;
            ContentsAreMarkedSecret = secret;
            Token++;

            return true;
        }
    }

    private static (SecretClipboard Clipboard, FakeClipboard Fake) New(
        Action<FakeClipboard>? configure = null)
    {
        FakeClipboard fake = new();
        configure?.Invoke(fake);

        return (new SecretClipboard(fake), fake);
    }

    private static Secret APassword() => Secret.From(Password);

    // ── Copying ─────────────────────────────────────────────────────────

    [Fact]
    public void Copying_a_password_puts_it_on_and_starts_the_countdown()
    {
        (SecretClipboard clipboard, FakeClipboard fake) = New();
        using Secret password = APassword();

        Assert.True(clipboard.CopyPassword(password));

        Assert.Equal(Password, fake.Contents);
        Assert.True(clipboard.IsCountingDown);
        Assert.Equal(30, clipboard.SecondsLeft);
    }

    [Fact]
    public void A_password_goes_on_marked_to_stay_out_of_clipboard_history()
    {
        // The countdown is no defence against history, which survives the
        // clear and is readable from Win+V for the rest of the session.
        (SecretClipboard clipboard, FakeClipboard fake) = New();
        using Secret password = APassword();

        clipboard.CopyPassword(password);

        Assert.True(fake.ContentsAreMarkedSecret);
    }

    [Fact]
    public void A_user_name_goes_on_unmarked_and_starts_nothing()
    {
        // Not a secret. Keeping it out of history would cost somebody a
        // feature and buy nothing, since the same name is in the document.
        (SecretClipboard clipboard, FakeClipboard fake) = New();

        Assert.True(clipboard.CopyUserName("CORP\\ada"));

        Assert.Equal("CORP\\ada", fake.Contents);
        Assert.False(fake.ContentsAreMarkedSecret);
        Assert.False(clipboard.IsCountingDown);
    }

    [Fact]
    public void There_is_nothing_to_copy_when_there_is_no_password()
    {
        (SecretClipboard clipboard, FakeClipboard fake) = New();

        Assert.False(clipboard.CopyPassword(Secret.Empty));

        Assert.Null(fake.Contents);
        Assert.False(clipboard.IsCountingDown);
        Assert.NotNull(clipboard.Notice);
    }

    [Fact]
    public void A_clipboard_that_will_not_take_it_starts_no_countdown()
    {
        // Otherwise a countdown would run against a password that never went
        // on, and end by clearing whatever somebody else had copied.
        (SecretClipboard clipboard, FakeClipboard _) = New(f => f.RefusesToWrite = true);
        using Secret password = APassword();

        Assert.False(clipboard.CopyPassword(password));
        Assert.False(clipboard.IsCountingDown);
        Assert.NotNull(clipboard.Notice);
    }

    // ── Clearing ────────────────────────────────────────────────────────

    [Fact]
    public void The_clipboard_is_not_cleared_early()
    {
        (SecretClipboard clipboard, FakeClipboard fake) = New();
        using Secret password = APassword();

        clipboard.CopyPassword(password);

        Assert.True(clipboard.Tick(TimeSpan.FromSeconds(29)));

        Assert.Equal(Password, fake.Contents);
        Assert.Equal(1, clipboard.SecondsLeft);
    }

    [Fact]
    public void The_clipboard_is_cleared_when_the_countdown_runs_out()
    {
        (SecretClipboard clipboard, FakeClipboard fake) = New();
        using Secret password = APassword();

        clipboard.CopyPassword(password);

        Assert.False(clipboard.Tick(SecretClipboard.Window));

        Assert.Null(fake.Contents);
        Assert.Equal(1, fake.Clears);
        Assert.False(clipboard.IsCountingDown);
    }

    [Fact]
    public void A_late_tick_still_clears_rather_than_missing_the_moment()
    {
        // A busy dispatcher, or a machine that has been asleep, delivers a
        // tick well after it was due. Elapsed time is measured, so overshoot
        // clears rather than leaving a countdown stuck below zero.
        (SecretClipboard clipboard, FakeClipboard fake) = New();
        using Secret password = APassword();

        clipboard.CopyPassword(password);
        clipboard.Tick(TimeSpan.FromMinutes(20));

        Assert.Equal(1, fake.Clears);
    }

    [Fact]
    public void Something_else_copied_in_the_meantime_is_left_exactly_alone()
    {
        // The rule that makes this safe to use. Clearing here would throw away
        // whatever the person had just copied, and they would have no idea
        // what did it.
        (SecretClipboard clipboard, FakeClipboard fake) = New();
        using Secret password = APassword();

        clipboard.CopyPassword(password);
        fake.SomebodyElseCopies("a paragraph of notes");

        Assert.False(clipboard.Tick(SecretClipboard.Window));

        Assert.Equal("a paragraph of notes", fake.Contents);
        Assert.Equal(0, fake.Clears);
        Assert.False(clipboard.IsCountingDown);
    }

    [Fact]
    public void Copying_a_user_name_ends_a_running_countdown_without_clearing()
    {
        // The password has already gone: putting anything on the clipboard
        // replaces what was there. There is nothing left to clear, and
        // clearing would take the user name with it.
        (SecretClipboard clipboard, FakeClipboard fake) = New();
        using Secret password = APassword();

        clipboard.CopyPassword(password);
        clipboard.CopyUserName("ada");

        Assert.False(clipboard.IsCountingDown);

        clipboard.Tick(SecretClipboard.Window);

        Assert.Equal("ada", fake.Contents);
        Assert.Equal(0, fake.Clears);
    }

    [Fact]
    public void Copying_a_second_password_restarts_the_countdown()
    {
        // Rather than adding to it. The first countdown was measuring the life
        // of something no longer on the clipboard.
        (SecretClipboard clipboard, FakeClipboard _) = New();
        using Secret first = APassword();
        using Secret second = Secret.From("a-different-one");

        clipboard.CopyPassword(first);
        clipboard.Tick(TimeSpan.FromSeconds(25));
        clipboard.CopyPassword(second);

        Assert.Equal(30, clipboard.SecondsLeft);
    }

    [Fact]
    public void A_password_erased_after_copying_still_gets_cleared()
    {
        // Nothing here holds the password, which is why the countdown does not
        // care that it has gone. If it compared contents instead of asking
        // Windows whether the clipboard had changed, this would fail.
        (SecretClipboard clipboard, FakeClipboard fake) = New();

        Secret password = APassword();
        clipboard.CopyPassword(password);
        password.Dispose();

        clipboard.Tick(SecretClipboard.Window);

        Assert.Equal(1, fake.Clears);
    }

    // ── When the clipboard will not co-operate ──────────────────────────

    [Fact]
    public void A_clear_that_fails_is_tried_again()
    {
        // Another program holding the clipboard open is an ordinary thing.
        // Giving up would leave the password on it.
        (SecretClipboard clipboard, FakeClipboard fake) = New(f => f.RefusesToClear = true);
        using Secret password = APassword();

        clipboard.CopyPassword(password);

        Assert.True(clipboard.Tick(SecretClipboard.Window));
        Assert.True(clipboard.IsCountingDown);

        fake.RefusesToClear = false;

        Assert.False(clipboard.Tick(TimeSpan.FromSeconds(1)));
        Assert.Equal(1, fake.Clears);
    }

    [Fact]
    public void A_clear_that_keeps_failing_says_so_rather_than_going_quiet()
    {
        // Silence here would be a password left on the clipboard and nobody
        // told. The notice says what to do about it instead.
        (SecretClipboard clipboard, FakeClipboard fake) = New(f => f.RefusesToClear = true);
        using Secret password = APassword();

        clipboard.CopyPassword(password);
        clipboard.Tick(SecretClipboard.Window);

        for (int i = 0; i < 10 && clipboard.IsCountingDown; i++)
        {
            clipboard.Tick(TimeSpan.FromSeconds(1));
        }

        Assert.False(clipboard.IsCountingDown);
        Assert.Equal(0, fake.Clears);
        Assert.Contains("may still be on it", clipboard.Notice, StringComparison.Ordinal);
    }

    // ── On the way out ──────────────────────────────────────────────────

    [Fact]
    public void Clearing_on_the_way_out_empties_it()
    {
        // A password left behind by a process that has gone will never be
        // cleared by anything.
        (SecretClipboard clipboard, FakeClipboard fake) = New();
        using Secret password = APassword();

        clipboard.CopyPassword(password);

        Assert.True(clipboard.ClearNow());
        Assert.Equal(1, fake.Clears);
    }

    [Fact]
    public void Clearing_on_the_way_out_leaves_somebody_elses_copy_alone()
    {
        (SecretClipboard clipboard, FakeClipboard fake) = New();
        using Secret password = APassword();

        clipboard.CopyPassword(password);
        fake.SomebodyElseCopies("a paragraph of notes");

        clipboard.ClearNow();

        Assert.Equal("a paragraph of notes", fake.Contents);
        Assert.Equal(0, fake.Clears);
    }

    [Fact]
    public void Clearing_when_nothing_was_copied_does_nothing()
    {
        (SecretClipboard clipboard, FakeClipboard fake) = New();
        fake.SomebodyElseCopies("a paragraph of notes");

        clipboard.ClearNow();

        Assert.Equal("a paragraph of notes", fake.Contents);
        Assert.Equal(0, fake.Clears);
    }

    [Fact]
    public void Ticking_when_nothing_is_running_asks_to_be_left_alone()
    {
        (SecretClipboard clipboard, FakeClipboard _) = New();

        Assert.False(clipboard.Tick(TimeSpan.FromSeconds(1)));
    }

    // ── What it says ────────────────────────────────────────────────────

    [Fact]
    public void The_countdown_counts_in_whole_seconds_and_gets_the_grammar_right()
    {
        (SecretClipboard clipboard, FakeClipboard _) = New();
        using Secret password = APassword();

        clipboard.CopyPassword(password);
        Assert.Contains("30 seconds", clipboard.Notice, StringComparison.Ordinal);

        clipboard.Tick(TimeSpan.FromSeconds(29));
        Assert.Contains("1 second.", clipboard.Notice, StringComparison.Ordinal);
    }

    [Fact]
    public void Nothing_it_says_contains_the_password()
    {
        (SecretClipboard clipboard, FakeClipboard fake) = New();
        using Secret password = APassword();

        List<string> said = [];

        clipboard.CopyPassword(password);
        said.Add(clipboard.Notice!);

        clipboard.Tick(TimeSpan.FromSeconds(29));
        said.Add(clipboard.Notice!);

        clipboard.Tick(TimeSpan.FromSeconds(1));
        said.Add(clipboard.Notice!);

        fake.SomebodyElseCopies("x");

        Assert.All(said, s => Assert.DoesNotContain(Password, s, StringComparison.Ordinal));
    }
}
