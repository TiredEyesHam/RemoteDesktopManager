using Patchbay.Core.Security;

using Patchbay.Core.Model;
using Patchbay.Core.Sessions;

namespace Patchbay.Tests;

/// <summary>
/// The sign-in for one attempt, and the one property that matters about it
/// (M4-10): a password must not appear in anything that prints.
///
/// <para>
/// Most of this file asserts a negative, which is unusual and deliberate. A
/// password reaching a log file is not a bug anybody writes on purpose; it is
/// what happens when a value travels through a diagnostic object and every
/// diagnostic object does what it was built to do. So each route a password
/// can take out of the process is walked here and asserted to be closed — the
/// credentials themselves, the request that carries them, the plan entry, the
/// report line and the notice bar.
/// </para>
/// </summary>
public class SessionCredentialsTests
{
    private const string Plaintext = "hunter2-correct-horse";

    private static SessionCredentials Full => new()
    {
        UserName = "svc-deploy",
        Domain = "CORP",
        Password = Secret.From(Plaintext),
    };

    private static SessionRequest RequestFor(
        SessionCredentials credentials,
        Action<ConnectionSettings>? configure = null)
    {
        ConnectionSettings settings = ConnectionSettings.Defaults;
        configure?.Invoke(settings);

        return new SessionRequest
        {
            HostName = "web-01",
            Settings = settings,
            DisplayName = "WEB-PRD-01",
            Credentials = credentials,
        };
    }

    // ── What is in there ────────────────────────────────────────────────

    [Fact]
    public void Nothing_supplied_is_the_default()
    {
        SessionRequest request = new()
        {
            HostName = "web-01",
            Settings = ConnectionSettings.Defaults,
        };

        Assert.True(SessionCredentials.None.IsEmpty);
        Assert.False(SessionCredentials.None.HasPassword);
        Assert.Equal(SessionCredentials.None, request.Credentials);
    }

    [Fact]
    public void A_user_name_alone_is_not_empty()
        => Assert.False(new SessionCredentials { UserName = "svc-deploy" }.IsEmpty);

    [Fact]
    public void A_password_alone_is_not_empty()
    {
        // The control is entitled to a password with no user name: the name
        // may be coming from the document while only the secret was typed.
        SessionCredentials credentials = new() { Password = Secret.From(Plaintext) };

        Assert.False(credentials.IsEmpty);
        Assert.True(credentials.HasPassword);
    }

    [Fact]
    public void An_account_reads_the_way_a_person_writes_it()
        => Assert.Equal("CORP\\svc-deploy", Full.Display);

    [Fact]
    public void A_local_account_is_shown_without_a_domain()
        => Assert.Equal("admin", new SessionCredentials { UserName = "admin" }.Display);

    [Fact]
    public void A_domain_with_no_account_shows_nothing()
    {
        // Half a sign-in is not a sign-in. Showing the realm and a backslash
        // would look like a name that had been lost rather than one never
        // given.
        Assert.Equal(string.Empty, new SessionCredentials { Domain = "CORP" }.Display);
    }

    // ── The same sign-in twice ──────────────────────────────────────────

    [Fact]
    public void Two_identical_sign_ins_compare_equal()
    {
        // The question a re-prompt has to answer before it offers to try
        // again. Reconnecting with what was just refused is not a retry.
        Assert.Equal(Full, Full with { });
    }

    [Fact]
    public void A_different_password_is_a_different_sign_in()
        => Assert.NotEqual(Full, Full with { Password = Secret.From("something-else") });

    [Fact]
    public void A_different_account_is_a_different_sign_in()
        => Assert.NotEqual(Full, Full with { UserName = "svc-other" });

    // ── The password does not print ─────────────────────────────────────

    [Fact]
    public void The_credentials_do_not_print_the_password()
    {
        string printed = Full.ToString();

        Assert.DoesNotContain(Plaintext, printed, StringComparison.Ordinal);
        Assert.Contains("CORP\\svc-deploy", printed, StringComparison.Ordinal);
        Assert.Contains("supplied", printed, StringComparison.Ordinal);
    }

    [Fact]
    public void The_credentials_say_when_there_is_no_password()
    {
        Assert.Contains(
            "password none",
            new SessionCredentials { UserName = "admin" }.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_request_does_not_print_the_password()
    {
        // A record prints every property it has, so this is the route the
        // password takes without anybody writing a line of code.
        Assert.DoesNotContain(Plaintext, RequestFor(Full).ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_plan_does_not_print_the_password()
    {
        IReadOnlyList<RdpSettingWrite> plan = RdpSettingsMapper.Plan(RequestFor(Full));

        Assert.DoesNotContain(
            Plaintext,
            string.Join(Environment.NewLine, plan.Select(w => w.ToString())),
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_redacted_write_still_says_what_it_is()
    {
        string printed = Password(RdpSettingsMapper.Plan(RequestFor(Full))).ToString();

        Assert.Contains("ClearTextPassword", printed, StringComparison.Ordinal);
        Assert.DoesNotContain(Plaintext, printed, StringComparison.Ordinal);
    }

    [Fact]
    public void Redaction_does_not_leak_the_length()
    {
        // Two passwords of different lengths must print identically, or the
        // redaction has told anyone reading how long to make their guess.
        string longer = string.Concat(Enumerable.Repeat("x", 64));

        RdpSettingWrite one = Password(RdpSettingsMapper.Plan(
            RequestFor(new SessionCredentials { UserName = "a", Password = Secret.From("x") })));

        RdpSettingWrite other = Password(RdpSettingsMapper.Plan(
            RequestFor(new SessionCredentials { UserName = "a", Password = Secret.From(longer) })));

        Assert.Equal(one.ToString(), other.ToString());
    }

    [Fact]
    public void The_report_does_not_print_the_password()
    {
        RdpSettingsReport report = new()
        {
            Entries =
            [
                new RdpSettingReport
                {
                    Write = Password(RdpSettingsMapper.Plan(RequestFor(Full))),
                    Outcome = RdpSettingOutcome.Rejected,
                    Message = "The control would not take it.",
                },
            ],
        };

        Assert.DoesNotContain(Plaintext, report.Entries[0].ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Plaintext, report.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Plaintext, report.Notice ?? string.Empty, StringComparison.Ordinal);
    }

    // ── Where the password goes ─────────────────────────────────────────

    [Fact]
    public void The_password_is_written_to_the_advanced_settings()
    {
        RdpSettingWrite password = Password(RdpSettingsMapper.Plan(RequestFor(Full)));

        Assert.Equal(RdpSettingTarget.AdvancedSettings, password.Target);
        Assert.Equal("ClearTextPassword", password.Name);
        Assert.Equal(Plaintext, ((Secret)password.Value).RevealAsString());
        Assert.True(password.IsSecret);
    }

    [Fact]
    public void A_password_that_does_not_apply_is_not_material()
    {
        // It produces a logon prompt: visible, immediate, and fixable by the
        // person looking at it. Nothing is left less protected than was asked
        // for, which is what material means here.
        Assert.False(Password(RdpSettingsMapper.Plan(RequestFor(Full))).IsMaterial);
    }

    [Fact]
    public void No_password_means_no_write_at_all()
    {
        // Not an empty string. Writing one is telling the control the password
        // is blank, which is a different claim from not having been given one.
        Assert.Null(Find(RdpSettingsMapper.Plan(RequestFor(SessionCredentials.None))));
    }

    [Fact]
    public void Single_sign_on_never_carries_a_password()
    {
        // CredSSP hands over the ticket that is already there. A secret sent
        // alongside it is a secret handed over for no reason.
        IReadOnlyList<RdpSettingWrite> plan = RdpSettingsMapper.Plan(
            RequestFor(Full, s => s.CredentialMode = CredentialMode.CurrentUser));

        Assert.Null(Find(plan));
        Assert.DoesNotContain(plan, w => w.Name == "UserName");
    }

    // ── Which account gets named ────────────────────────────────────────

    [Fact]
    public void The_document_names_the_account_when_the_attempt_does_not()
    {
        IReadOnlyList<RdpSettingWrite> plan = RdpSettingsMapper.Plan(
            RequestFor(SessionCredentials.None, s =>
            {
                s.UserName = "stored-user";
                s.Domain = "STORED";
            }));

        Assert.Equal("stored-user", Named(plan, "UserName"));
        Assert.Equal("STORED", Named(plan, "Domain"));
    }

    [Fact]
    public void The_attempt_overrides_the_account_the_document_remembers()
    {
        // The situation this exists for: refused, asked again, and a different
        // account typed. Sending the stored one back would look like listening
        // and be the same failure.
        IReadOnlyList<RdpSettingWrite> plan = RdpSettingsMapper.Plan(
            RequestFor(Full, s =>
            {
                s.UserName = "stored-user";
                s.Domain = "STORED";
            }));

        Assert.Equal("svc-deploy", Named(plan, "UserName"));
        Assert.Equal("CORP", Named(plan, "Domain"));
    }

    [Fact]
    public void A_typed_local_account_does_not_inherit_the_stored_domain()
    {
        // An empty domain is how a local account is expressed. Attaching the
        // realm from the document to it fails in a way that looks like a bad
        // password, which is the worst shape a failure can take here.
        IReadOnlyList<RdpSettingWrite> plan = RdpSettingsMapper.Plan(
            RequestFor(
                new SessionCredentials { UserName = "admin", Password = Secret.From(Plaintext) },
                s =>
                {
                    s.UserName = "stored-user";
                    s.Domain = "STORED";
                }));

        Assert.Equal("admin", Named(plan, "UserName"));
        Assert.DoesNotContain(plan, w => w.Name == "Domain");
    }

    [Fact]
    public void A_password_alone_leaves_the_document_to_name_the_account()
    {
        // Somebody typed only the secret. The name they were shown is the
        // stored one, so the stored one is what should be sent.
        IReadOnlyList<RdpSettingWrite> plan = RdpSettingsMapper.Plan(
            RequestFor(
                new SessionCredentials { Password = Secret.From(Plaintext) },
                s => s.UserName = "stored-user"));

        Assert.Equal("stored-user", Named(plan, "UserName"));
        Assert.NotNull(Find(plan));
    }

    private static RdpSettingWrite? Find(IReadOnlyList<RdpSettingWrite> plan)
        => plan.FirstOrDefault(w => w.Name == "ClearTextPassword");

    private static RdpSettingWrite Password(IReadOnlyList<RdpSettingWrite> plan)
        => Find(plan) ?? throw new InvalidOperationException("The plan has no password in it.");

    private static string? Named(IReadOnlyList<RdpSettingWrite> plan, string name)
        => plan.FirstOrDefault(w => w.Name == name)?.Value as string;
}
