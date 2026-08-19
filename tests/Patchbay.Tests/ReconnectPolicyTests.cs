using Patchbay.Core.Model;
using Patchbay.Core.Sessions;

namespace Patchbay.Tests;

/// <summary>
/// The backoff arithmetic (M4-08).
///
/// Small sums, and every one of them is somewhere a mistake would be invisible
/// in use: a delay that grows the wrong way still reconnects, a spread that is
/// always zero still reconnects, and an overflow only shows up on the fortieth
/// attempt of a session nobody was watching.
/// </summary>
public class ReconnectPolicyTests
{
    // The sample that sits exactly on the base delay, so the arithmetic can be
    // read without the spread on top of it.
    private const double Centre = 0.5;

    // ── The shipped settings ────────────────────────────────────────────

    [Fact]
    public void The_shipped_policy_is_on()
        => Assert.True(ReconnectPolicy.Default.Enabled);

    [Fact]
    public void The_off_policy_is_off()
        => Assert.False(ReconnectPolicy.Off.Enabled);

    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 10)]
    [InlineData(3, 20)]
    [InlineData(4, 40)]
    [InlineData(5, 60)]
    [InlineData(6, 60)]
    [InlineData(10, 60)]
    public void The_wait_doubles_until_it_hits_the_ceiling(int attempt, int expected)
        => Assert.Equal(
            TimeSpan.FromSeconds(expected),
            ReconnectPolicy.Default.Delay(attempt, Centre));

    [Fact]
    public void Ten_attempts_at_the_shipped_delays_is_about_seven_minutes()
    {
        // The number that decides whether a reboot is survived. If somebody
        // changes the defaults, this is the consequence they should have to
        // look at.
        TimeSpan total = TimeSpan.Zero;

        for (int attempt = 1; attempt <= ReconnectPolicy.Default.AttemptLimit; attempt++)
        {
            total += ReconnectPolicy.Default.Delay(attempt, Centre);
        }

        Assert.InRange(total, TimeSpan.FromMinutes(6), TimeSpan.FromMinutes(9));
    }

    // ── The spread ──────────────────────────────────────────────────────

    [Fact]
    public void The_earliest_sample_lands_a_fifth_below_the_base_delay()
        => Assert.Equal(TimeSpan.FromSeconds(4), ReconnectPolicy.Default.Delay(1, sample: 0.0));

    [Fact]
    public void The_latest_sample_lands_a_fifth_above_it()
        => Assert.Equal(TimeSpan.FromSeconds(6), ReconnectPolicy.Default.Delay(1, sample: 1.0));

    [Fact]
    public void The_ceiling_bounds_the_base_delay_and_not_the_spread()
    {
        // Deliberate. Clamping the result at the ceiling would make every
        // session that reached it wait exactly the same time — which is the
        // lockstep the spread exists to prevent, arriving precisely where the
        // most sessions have gathered.
        Assert.Equal(TimeSpan.FromSeconds(72), ReconnectPolicy.Default.Delay(5, sample: 1.0));
    }

    [Fact]
    public void No_spread_gives_the_same_answer_every_time()
    {
        ReconnectPolicy exact = ReconnectPolicy.Default with { Jitter = 0.0 };

        Assert.Equal(exact.Delay(3, 0.0), exact.Delay(3, 1.0));
    }

    [Fact]
    public void A_wait_is_never_negative()
    {
        // The most a spread can subtract is the whole delay, and only then.
        ReconnectPolicy wild = ReconnectPolicy.Default with { Jitter = 1.0 };

        Assert.Equal(TimeSpan.Zero, wild.Delay(1, sample: 0.0));
    }

    // ── The awkward ends ────────────────────────────────────────────────

    [Fact]
    public void A_factor_of_one_is_a_fixed_interval()
    {
        ReconnectPolicy steady = ReconnectPolicy.Default with { Factor = 1.0 };

        Assert.Equal(steady.Delay(1, Centre), steady.Delay(9, Centre));
    }

    [Theory]
    [InlineData(40)]
    [InlineData(1000)]
    [InlineData(int.MaxValue)]
    public void A_very_late_attempt_does_not_overflow(int attempt)
    {
        // Doubling a five-second delay overflows a double to infinity well
        // before this, and TimeSpan.FromSeconds(infinity) throws — turning an
        // unattended session into a crash.
        Assert.Equal(TimeSpan.FromSeconds(60), ReconnectPolicy.Default.Delay(attempt, Centre));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void There_is_no_attempt_before_the_first(int attempt)
        => Assert.Throws<ArgumentOutOfRangeException>(() => ReconnectPolicy.Default.Delay(attempt));

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void A_sample_outside_the_range_is_refused(double sample)
        => Assert.Throws<ArgumentOutOfRangeException>(() => ReconnectPolicy.Default.Delay(1, sample));

    // ── Settings that make no sense ─────────────────────────────────────

    [Fact]
    public void A_first_delay_of_nothing_is_refused()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => ReconnectPolicy.Default with { FirstDelay = TimeSpan.Zero });

    [Fact]
    public void A_ceiling_of_nothing_is_refused()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => ReconnectPolicy.Default with { MaxDelay = TimeSpan.Zero });

    [Fact]
    public void A_factor_that_shrinks_the_wait_is_refused()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => ReconnectPolicy.Default with { Factor = 0.5 });

    [Fact]
    public void A_negative_attempt_limit_is_refused()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => ReconnectPolicy.Default with { AttemptLimit = -1 });

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.5)]
    public void A_spread_outside_the_range_is_refused(double jitter)
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => ReconnectPolicy.Default with { Jitter = jitter });

    [Fact]
    public void No_attempts_at_all_is_a_legitimate_setting()
    {
        // Distinct from being switched off, and reported differently: this one
        // gives up rather than never starting. See ReconnectRulesTests.
        Assert.Equal(0, (ReconnectPolicy.Default with { AttemptLimit = 0 }).AttemptLimit);
    }

    // ── Reading it off a connection ─────────────────────────────────────

    [Fact]
    public void A_policy_needs_settings_to_come_from()
        => Assert.Throws<ArgumentNullException>(() => ReconnectPolicy.For(null!));

    [Fact]
    public void A_connection_with_reconnecting_switched_off_gets_a_policy_that_is_off()
        => Assert.False(ReconnectPolicy.For(new ConnectionSettings { AutoReconnect = false }).Enabled);

    [Fact]
    public void A_connection_with_it_switched_on_gets_one_that_is_on()
        => Assert.True(ReconnectPolicy.For(new ConnectionSettings { AutoReconnect = true }).Enabled);

    [Fact]
    public void An_unresolved_setting_is_read_as_on()
    {
        // Null means inherit, and by the time a request exists the resolver has
        // been through it. A request built by hand has not, and the default in
        // ConnectionSettings.Defaults is on.
        Assert.True(ReconnectPolicy.For(new ConnectionSettings()).Enabled);
        Assert.Equal(true, ConnectionSettings.Defaults.AutoReconnect);
    }

    [Fact]
    public void Only_the_switch_comes_from_the_document()
    {
        // How long to wait and how often is a preference about Patchbay, not a
        // fact about a server. Putting it in the connection file would mean
        // answering it once per machine.
        ReconnectPolicy policy = ReconnectPolicy.For(ConnectionSettings.Defaults);

        Assert.Equal(ReconnectPolicy.Default.FirstDelay, policy.FirstDelay);
        Assert.Equal(ReconnectPolicy.Default.MaxDelay, policy.MaxDelay);
        Assert.Equal(ReconnectPolicy.Default.AttemptLimit, policy.AttemptLimit);
        Assert.Equal(ReconnectPolicy.Default.Factor, policy.Factor);
    }
}
