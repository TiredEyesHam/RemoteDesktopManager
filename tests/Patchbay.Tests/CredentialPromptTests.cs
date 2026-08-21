using Patchbay.Core.Sessions;

namespace Patchbay.Tests;

/// <summary>
/// What a docked credential panel asks, and what it will not accept (M3-06).
///
/// The panel itself is XAML and is not tested here. What is tested is the part
/// that would be wrong in a way nobody notices: offering to save on a machine
/// that cannot, pre-filling a password that was just refused, and letting
/// somebody press Connect on an unchanged answer until the account locks.
/// </summary>
public class CredentialPromptTests
{
    private const string Endpoint = "web-01:3389";

    private static SessionCredentials Refused => new()
    {
        UserName = "svc-deploy",
        Domain = "CORP",
        Password = "the-wrong-one",
    };

    private static CredentialPrompt AfterRefusal(bool canSave = false)
        => new(Endpoint, CredentialPromptReason.Refused, Refused, canSave);

    // ── What it asks ────────────────────────────────────────────────────

    [Fact]
    public void A_prompt_needs_to_know_who_is_asking()
        => Assert.Throws<ArgumentException>(
            () => new CredentialPrompt(" ", CredentialPromptReason.Required));

    [Theory]
    [InlineData(CredentialPromptReason.Required)]
    [InlineData(CredentialPromptReason.Refused)]
    [InlineData(CredentialPromptReason.Unreadable)]
    [InlineData(CredentialPromptReason.ProfileMissing)]
    public void Every_reason_names_the_machine(CredentialPromptReason reason)
        => Assert.Contains(Endpoint, new CredentialPrompt(Endpoint, reason).Title, StringComparison.Ordinal);

    [Fact]
    public void An_ordinary_prompt_needs_no_explanation()
        => Assert.Null(new CredentialPrompt(Endpoint, CredentialPromptReason.Required).Detail);

    [Fact]
    public void A_refusal_says_the_session_is_still_there()
    {
        // The whole point of M4-10. Somebody who thinks the tab has died will
        // close it rather than answer the panel.
        Assert.Contains("still open", AfterRefusal().Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unreadable_password_says_it_has_been_left_alone()
        => Assert.Contains(
            "left alone",
            new CredentialPrompt(Endpoint, CredentialPromptReason.Unreadable).Detail,
            StringComparison.Ordinal);

    // ── What it starts with ─────────────────────────────────────────────

    [Fact]
    public void The_account_is_filled_in_and_the_password_never_is()
    {
        // Pre-filling the refused password invites Connect without reading.
        CredentialPrompt prompt = AfterRefusal();

        Assert.Equal("svc-deploy", prompt.UserName);
        Assert.Equal("CORP", prompt.Domain);
        Assert.Equal(string.Empty, prompt.Password);
    }

    [Fact]
    public void A_prompt_with_nothing_known_starts_empty()
    {
        CredentialPrompt prompt = new(Endpoint, CredentialPromptReason.Required);

        Assert.Equal(string.Empty, prompt.UserName);
        Assert.Equal(string.Empty, prompt.Domain);
    }

    [Fact]
    public void Only_a_refusal_remembers_what_was_refused()
    {
        // A missing profile has no refused sign-in to compare against, so the
        // repeat rule below must not fire on one.
        Assert.Null(new CredentialPrompt(
            Endpoint,
            CredentialPromptReason.ProfileMissing,
            Refused).Refused);
    }

    // ── Saving ──────────────────────────────────────────────────────────

    [Fact]
    public void Saving_is_not_offered_where_it_cannot_work()
        => Assert.False(AfterRefusal(canSave: false).CanOfferToSave);

    [Fact]
    public void Asking_to_save_where_saving_is_impossible_is_refused_rather_than_promised()
    {
        // A panel that forgets to hide the box must not be able to promise
        // something that will not happen (M3-02).
        CredentialPrompt prompt = AfterRefusal(canSave: false);

        prompt.SavePassword = true;

        Assert.False(prompt.SavePassword);
    }

    [Fact]
    public void Asking_to_save_where_it_works_is_kept()
    {
        CredentialPrompt prompt = AfterRefusal(canSave: true);

        prompt.SavePassword = true;

        Assert.True(prompt.SavePassword);
    }

    // ── What it will not send ───────────────────────────────────────────

    [Fact]
    public void An_empty_answer_cannot_be_submitted()
        => Assert.False(new CredentialPrompt(Endpoint, CredentialPromptReason.Required).CanSubmit);

    [Fact]
    public void A_password_with_no_account_can_be_submitted()
    {
        // The account may be coming from the document. Only the secret was
        // typed, which is a real case (M4-10).
        CredentialPrompt prompt = new(Endpoint, CredentialPromptReason.Required)
        {
            Password = "hunter2",
        };

        Assert.True(prompt.CanSubmit);
    }

    [Fact]
    public void The_sign_in_that_was_just_refused_cannot_be_sent_again()
    {
        // Not a warning. Resubmitting is not a retry, and enough of them lock
        // the account.
        CredentialPrompt prompt = AfterRefusal();
        prompt.Password = "the-wrong-one";

        Assert.True(prompt.IsUnchanged);
        Assert.False(prompt.CanSubmit);
        Assert.NotNull(prompt.Obstacle);
    }

    [Fact]
    public void A_different_password_may_be_sent()
    {
        CredentialPrompt prompt = AfterRefusal();
        prompt.Password = "a-different-one";

        Assert.False(prompt.IsUnchanged);
        Assert.True(prompt.CanSubmit);
        Assert.Null(prompt.Obstacle);
    }

    [Fact]
    public void A_different_account_may_be_sent_with_the_same_password()
    {
        CredentialPrompt prompt = AfterRefusal();
        prompt.UserName = "svc-other";
        prompt.Password = "the-wrong-one";

        Assert.True(prompt.CanSubmit);
    }

    [Fact]
    public void An_empty_box_explains_itself_and_gets_no_sentence()
        => Assert.Null(new CredentialPrompt(Endpoint, CredentialPromptReason.Required).Obstacle);

    // ── The answer ──────────────────────────────────────────────────────

    [Fact]
    public void The_account_is_trimmed_and_the_password_is_not()
    {
        // A name pasted out of a spreadsheet arrives with a space on it. A
        // password is allowed to end in one.
        CredentialPrompt prompt = new(Endpoint, CredentialPromptReason.Required)
        {
            UserName = "  svc-deploy  ",
            Domain = " CORP ",
            Password = "trailing ",
        };

        SessionCredentials answer = prompt.ToCredentials();

        Assert.Equal("svc-deploy", answer.UserName);
        Assert.Equal("CORP", answer.Domain);
        Assert.Equal("trailing ", answer.Password);
    }

    [Fact]
    public void Whitespace_alone_is_not_an_account()
        => Assert.False(new CredentialPrompt(Endpoint, CredentialPromptReason.Required)
        {
            UserName = "   ",
        }.CanSubmit);

    [Fact]
    public void Forgetting_drops_the_password_and_keeps_the_account()
    {
        CredentialPrompt prompt = AfterRefusal();
        prompt.Password = "a-different-one";

        prompt.Forget();

        Assert.Equal(string.Empty, prompt.Password);
        Assert.Equal("svc-deploy", prompt.UserName);
    }

    [Fact]
    public void A_prompt_does_not_print_the_password()
    {
        CredentialPrompt prompt = AfterRefusal();
        prompt.Password = "hunter2-correct-horse";

        Assert.DoesNotContain("hunter2", prompt.ToString(), StringComparison.Ordinal);
        Assert.Contains("CORP\\svc-deploy", prompt.ToString(), StringComparison.Ordinal);
    }

    // ── The way past (M3-05) ────────────────────────────────────────────

    [Theory]
    [InlineData(CredentialPromptReason.Required)]
    [InlineData(CredentialPromptReason.Unreadable)]
    [InlineData(CredentialPromptReason.ProfileMissing)]
    public void A_panel_raised_before_connecting_offers_a_way_past(CredentialPromptReason reason)
    {
        // The server has its own logon screen, and somebody who does not want
        // to type into Patchbay is entitled to go and use it.
        CredentialPrompt prompt = new(Endpoint, reason);

        Assert.True(prompt.IsBeforeConnecting);
        Assert.Equal("Connect without one", prompt.DismissLabel);
    }

    [Fact]
    public void A_panel_over_a_refusal_has_nowhere_past_to_go()
    {
        // The screen they would land on is the one that just said no.
        CredentialPrompt prompt = AfterRefusal();

        Assert.False(prompt.IsBeforeConnecting);
        Assert.Equal("Not now", prompt.DismissLabel);
    }

    [Fact]
    public void The_second_button_never_says_cancel()
    {
        // On a pre-connect panel it starts a connection, and a button labelled
        // Cancel that connects is the worst kind of surprise.
        foreach (CredentialPromptReason reason in Enum.GetValues<CredentialPromptReason>())
        {
            Assert.DoesNotContain(
                "Cancel",
                new CredentialPrompt(Endpoint, reason).DismissLabel,
                StringComparison.OrdinalIgnoreCase);
        }
    }
}
