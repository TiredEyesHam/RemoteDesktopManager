using System.Globalization;
using Patchbay.Core.Diagnostics;
using Patchbay.Core.Sessions;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Patchbay.Tests;

/// <summary>
/// A password must not reach a log file (M3-08).
///
/// <see cref="ArchitectureTests.Anything_holding_a_secret_overrides_ToString"/>
/// covers the plain route, where a value is rendered with <c>ToString</c>.
/// These cover the two that go round it: <c>{@Object}</c>, which reflects over
/// properties and never calls <c>ToString</c>, and <c>{Password}</c>, where
/// there is no object at all and only the name of the hole to go on.
/// </summary>
public class LogRedactionTests
{
    // ── Harness ─────────────────────────────────────────────────────────

    /// <summary>
    /// Keeps every event, and can flatten one into the message and its
    /// properties together. Assertions run against that whole string, because
    /// "the password is not in the rendered line" is a weaker claim than "the
    /// password is nowhere in the event", and it is the weaker one that lets a
    /// JSON sink leak.
    /// </summary>
    private sealed class Capture : ILogEventSink
    {
        private readonly List<LogEvent> _events = [];

        public IReadOnlyList<LogEvent> Events => _events;

        public void Emit(LogEvent logEvent) => _events.Add(logEvent);

        public string Everything => string.Join(
            "\n",
            _events.Select(e => e.RenderMessage(CultureInfo.InvariantCulture)
                + " | "
                + string.Join(", ", e.Properties.Select(p => $"{p.Key}={p.Value}"))));
    }

    private const string Password = "correct-horse-battery-staple";

    private static Capture Log(Action<ILogger> write)
    {
        Capture capture = new();

        using Logger logger = PatchbayLog.Create(c => c.WriteTo.Sink(capture));

        write(logger);

        return capture;
    }

    private static readonly SessionCredentials SignIn = new()
    {
        UserName = "ada",
        Domain = "CONTOSO",
        Password = Password,
    };

    /// <summary>A record with a secret and no override, which is the hole this closes.</summary>
    private sealed record Forgetful
    {
        public string Server { get; init; } = "vm-07";

        public string Password { get; init; } = LogRedactionTests.Password;
    }

    /// <summary>Nothing secret anywhere, so nothing should change.</summary>
    private sealed class Ordinary
    {
        public string Server { get; init; } = "vm-07";

        public int Port { get; init; } = 3389;
    }

    // ── Objects, through {@} ────────────────────────────────────────────

    [Fact]
    public void Destructuring_a_sign_in_does_not_print_the_password()
    {
        Capture capture = Log(l => l.Information("Connecting with {@SignIn}", SignIn));

        Assert.DoesNotContain(Password, capture.Everything, StringComparison.Ordinal);
    }

    [Fact]
    public void Destructuring_a_sign_in_still_says_who_it_was_for()
    {
        // Over-redaction has a cost. The account a refused logon was tried
        // with is the first thing anybody reads the log for, and a line that
        // masks that too is a line nobody can act on.
        Capture capture = Log(l => l.Information("Connecting with {@SignIn}", SignIn));

        Assert.Contains("ada", capture.Everything, StringComparison.Ordinal);
        Assert.Contains("CONTOSO", capture.Everything, StringComparison.Ordinal);
    }

    [Fact]
    public void A_setting_write_carrying_a_secret_prints_the_way_it_asked_to()
    {
        // The name test cannot save this one: the secret is in a property
        // called Value. What saves it is the write's own ToString, which the
        // policy uses instead of taking the record apart.
        RdpSettingWrite write = new()
        {
            Target = RdpSettingTarget.AdvancedSettings,
            Name = "ClearTextPassword",
            Value = Password,
            Setting = "Password",
            Purpose = "The password",
            IsSecret = true,
        };

        Capture capture = Log(l => l.Information("Applying {@Write}", write));

        Assert.DoesNotContain(Password, capture.Everything, StringComparison.Ordinal);
        Assert.Contains(SecretNames.Mask, capture.Everything, StringComparison.Ordinal);
    }

    [Fact]
    public void A_record_that_never_overrode_ToString_is_taken_apart_rather_than_trusted()
    {
        // A record gets a ToString synthesised onto it, and that one prints
        // every property. Trusting "the type declares a ToString" would hand
        // the password straight to the sink.
        Capture capture = Log(l => l.Information("Opening {@Connection}", new Forgetful()));

        Assert.DoesNotContain(Password, capture.Everything, StringComparison.Ordinal);
        Assert.Contains("vm-07", capture.Everything, StringComparison.Ordinal);
    }

    [Fact]
    public void An_object_with_nothing_secret_in_it_is_left_as_it_was()
    {
        Capture capture = Log(l => l.Information("Opening {@Connection}", new Ordinary()));

        Assert.Contains("vm-07", capture.Everything, StringComparison.Ordinal);
        Assert.Contains("3389", capture.Everything, StringComparison.Ordinal);
        Assert.DoesNotContain(SecretNames.Mask, capture.Everything, StringComparison.Ordinal);
    }

    [Fact]
    public void A_type_from_outside_patchbay_is_left_to_serilog()
    {
        // Redacting other objects by reflection breaks diagnostics that were
        // not ours to break, and the secrets are all in types this repository
        // owns.
        Capture capture = Log(l => l.Information("Version {@Version}", new Version(1, 2, 3)));

        Assert.Contains("1", capture.Everything, StringComparison.Ordinal);
        Assert.DoesNotContain(SecretNames.Mask, capture.Everything, StringComparison.Ordinal);
    }

    // ── Names, through the enricher ─────────────────────────────────────

    [Fact]
    public void A_hole_named_like_a_secret_is_masked()
    {
        // No object to have an opinion about, just a string and the name of
        // the hole it went into.
        Capture capture = Log(l => l.Information("Signing in as {UserName} with {Password}", "ada", Password));

        Assert.DoesNotContain(Password, capture.Everything, StringComparison.Ordinal);
        Assert.Contains("ada", capture.Everything, StringComparison.Ordinal);
    }

    [Fact]
    public void Whether_there_is_a_password_is_not_itself_a_password()
    {
        // HasPassword and a count of imported passwords read as secrets by
        // name and are facts about one. They are also the facts somebody
        // chasing a refused logon actually wants.
        Capture capture = Log(l => l.Information("Attempt {HasPassword} {PasswordCount}", true, 3));

        Assert.Contains("True", capture.Everything, StringComparison.Ordinal);
        Assert.Contains("3", capture.Everything, StringComparison.Ordinal);
        Assert.DoesNotContain(SecretNames.Mask, capture.Everything, StringComparison.Ordinal);
    }

    [Fact]
    public void A_secret_under_a_key_in_a_dictionary_is_masked()
    {
        Dictionary<string, string> parsed = new(StringComparer.Ordinal)
        {
            ["Server"] = "vm-07",
            ["Password"] = Password,
        };

        Capture capture = Log(l => l.Information("Imported {@Fields}", parsed));

        Assert.DoesNotContain(Password, capture.Everything, StringComparison.Ordinal);
        Assert.Contains("vm-07", capture.Everything, StringComparison.Ordinal);
    }

    [Fact]
    public void A_secret_inside_a_list_is_masked()
    {
        Forgetful[] connections = [new Forgetful { Server = "vm-07" }, new Forgetful { Server = "vm-08" }];

        Capture capture = Log(l => l.Information("Imported {@Connections}", connections));

        Assert.DoesNotContain(Password, capture.Everything, StringComparison.Ordinal);
        Assert.Contains("vm-08", capture.Everything, StringComparison.Ordinal);
    }

    [Fact]
    public void A_password_pushed_through_the_log_context_is_masked()
    {
        // FromLogContext runs before the redactor for exactly this reason. A
        // property added on the way past is still a property.
        Capture capture = Log(l =>
        {
            using (Serilog.Context.LogContext.PushProperty("Password", Password))
            {
                l.Information("Connecting");
            }
        });

        Assert.DoesNotContain(Password, capture.Everything, StringComparison.Ordinal);
    }

    // ── Level (M0-07) ───────────────────────────────────────────────────

    [Fact]
    public void The_level_switch_changes_what_is_written_without_a_new_logger()
    {
        LogEventLevel original = PatchbayLog.Level.MinimumLevel;

        try
        {
            Capture capture = new();

            using Logger logger = PatchbayLog.Create(c => c.WriteTo.Sink(capture));

            logger.Debug("first");
            Assert.Empty(capture.Events);

            PatchbayLog.Level.MinimumLevel = LogEventLevel.Debug;
            logger.Debug("second");

            Assert.Single(capture.Events);
        }
        finally
        {
            PatchbayLog.Level.MinimumLevel = original;
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("chatty")]
    public void A_level_nobody_recognises_leaves_the_default_alone(string? value)
    {
        LogEventLevel original = PatchbayLog.Level.MinimumLevel;

        try
        {
            Assert.Equal(original, PatchbayLog.ApplyEnvironmentLevel(value));
        }
        finally
        {
            PatchbayLog.Level.MinimumLevel = original;
        }
    }

    [Fact]
    public void A_level_named_on_the_environment_is_applied()
    {
        LogEventLevel original = PatchbayLog.Level.MinimumLevel;

        try
        {
            Assert.Equal(LogEventLevel.Verbose, PatchbayLog.ApplyEnvironmentLevel("verbose"));
        }
        finally
        {
            PatchbayLog.Level.MinimumLevel = original;
        }
    }
}
