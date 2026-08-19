using Patchbay.Core.Model;
using Patchbay.Core.Sessions;

namespace Patchbay.Tests;

/// <summary>
/// What the status bar says about a session (M5-17).
///
/// The rule under test throughout is the same one: the engine when it has
/// spoken, the configuration when it has not, and muted whenever it is the
/// second. Every case here is one where showing the configured value as though
/// it were the negotiated one would tell somebody something untrue about a
/// machine they are typing into.
/// </summary>
public class SessionStatusLineTests
{
    private static SessionRequest RequestFor(Action<ConnectionSettings>? configure = null)
    {
        ConnectionSettings settings = ConnectionSettings.Defaults;
        configure?.Invoke(settings);

        return new SessionRequest
        {
            HostName = "web-01",
            Settings = settings,
            DisplayName = "WEB-PRD-01",
        };
    }

    private static IReadOnlyList<SessionStatusField> Build(
        SessionVitals vitals = default,
        SessionState state = SessionState.Idle,
        SessionPlacement? placement = null,
        Action<ConnectionSettings>? configure = null)
        => SessionStatusLine.Build(
            RequestFor(configure),
            state,
            vitals,
            placement ?? SessionPlacement.Nowhere);

    private static SessionStatusField Field(IReadOnlyList<SessionStatusField> fields, string label)
        => fields.Single(f => f.Label == label);

    private static SessionVitals Live => new()
    {
        Resolution = new PixelSize(1920, 1080),
        Security = SessionSecurity.NetworkLevel,
    };

    // ── The shape of the line ───────────────────────────────────────────

    [Fact]
    public void The_five_fields_are_always_there_in_the_same_order()
    {
        // A status bar whose fields come and go is harder to read than one
        // with dashes in it: the eye learns where a value lives and then has
        // to hunt for it again.
        string[] labels = [.. Build().Select(f => f.Label)];

        Assert.Equal(["Host", "Resolution", "Security", "Gateway", "Latency"], labels);
    }

    [Fact]
    public void Nothing_is_ever_blank_even_with_nothing_known()
    {
        foreach (SessionStatusField field in Build())
        {
            Assert.False(string.IsNullOrWhiteSpace(field.Value), field.Label);
        }
    }

    [Fact]
    public void A_field_that_is_only_a_configured_intention_says_so_by_being_muted()
    {
        IReadOnlyList<SessionStatusField> fields = Build(configure: s =>
        {
            s.GatewayHostName = "gw.example.com";
            s.GatewayUsage = GatewayUsage.Always;
        });

        Assert.Equal(SessionStatusTone.Muted, Field(fields, "Resolution").Tone);
        Assert.Equal(SessionStatusTone.Muted, Field(fields, "Gateway").Tone);
    }

    [Fact]
    public void The_same_fields_become_facts_once_the_engine_has_spoken()
    {
        SessionVitals vitals = Live with { GatewayHostName = "gw.example.com" };

        IReadOnlyList<SessionStatusField> fields = Build(vitals, SessionState.Connected);

        Assert.Equal(SessionStatusTone.Normal, Field(fields, "Resolution").Tone);
        Assert.Equal(SessionStatusTone.Normal, Field(fields, "Gateway").Tone);
    }

    [Fact]
    public void Requires_a_request()
        => Assert.Throws<ArgumentNullException>(() => SessionStatusLine.Build(
            null!, SessionState.Idle, SessionVitals.Unknown, SessionPlacement.Nowhere));

    // ── Host ────────────────────────────────────────────────────────────

    [Fact]
    public void The_default_port_is_not_worth_the_width()
    {
        Assert.Equal("web-01", Field(Build(), "Host").Value);
    }

    [Fact]
    public void A_port_someone_changed_is_shown()
    {
        IReadOnlyList<SessionStatusField> fields = Build(configure: s => s.Port = 3390);

        Assert.Equal("web-01:3390", Field(fields, "Host").Value);
    }

    [Fact]
    public void The_name_on_the_tab_and_the_address_are_paired_in_the_tooltip()
    {
        // The tab carries the node name, so this is the only place the two
        // appear together — which is what someone checks when they want to be
        // sure which machine they are about to type into.
        SessionStatusField host = Field(Build(), "Host");

        Assert.Contains("WEB-PRD-01", host.Detail);
        Assert.Contains("web-01:3389", host.Detail);
    }

    [Fact]
    public void A_connection_named_after_its_address_needs_no_explaining()
    {
        SessionRequest request = new()
        {
            HostName = "web-01",
            Settings = ConnectionSettings.Defaults,
            DisplayName = "web-01",
        };

        IReadOnlyList<SessionStatusField> fields = SessionStatusLine.Build(
            request, SessionState.Idle, SessionVitals.Unknown, SessionPlacement.Nowhere);

        Assert.Null(Field(fields, "Host").Detail);
    }

    // ── Resolution ──────────────────────────────────────────────────────

    [Fact]
    public void Before_connecting_the_resolution_shown_is_the_one_being_asked_for()
    {
        SessionStatusField resolution = Field(Build(), "Resolution");

        Assert.Equal("1920 × 1080", resolution.Value);
        Assert.Equal(SessionStatusTone.Muted, resolution.Tone);
        Assert.Contains("ask for", resolution.Detail);
    }

    [Fact]
    public void What_the_far_end_agreed_to_wins_over_what_was_asked_for()
    {
        // A server with a session-size policy hands back a resolution nobody
        // asked for, and the status bar is where that is noticed.
        SessionVitals vitals = new() { Resolution = new PixelSize(1280, 1024) };

        IReadOnlyList<SessionStatusField> fields = Build(vitals, SessionState.Connected);

        Assert.Equal("1280 × 1024", Field(fields, "Resolution").Value);
        Assert.Equal(SessionStatusTone.Normal, Field(fields, "Resolution").Tone);
    }

    [Fact]
    public void A_scaled_picture_says_what_it_is_scaled_by()
    {
        SessionPlacement placement = SessionScaling.Place(
            new PixelSize(1920, 1080), new PixelSize(1152, 648), smartSizing: true);

        IReadOnlyList<SessionStatusField> fields = Build(Live, SessionState.Connected, placement);

        Assert.Equal("1920 × 1080 at 60%", Field(fields, "Resolution").Value);
    }

    [Fact]
    public void A_picture_at_its_own_size_does_not_say_at_one_hundred_per_cent()
    {
        // It would be a longer way of writing nothing, and it would be on
        // screen almost always.
        SessionPlacement placement = SessionScaling.Place(
            new PixelSize(1920, 1080), new PixelSize(1920, 1080), smartSizing: true);

        IReadOnlyList<SessionStatusField> fields = Build(Live, SessionState.Connected, placement);

        Assert.Equal("1920 × 1080", Field(fields, "Resolution").Value);
    }

    [Fact]
    public void A_resolution_nobody_has_asked_for_or_agreed_to_is_a_dash()
    {
        IReadOnlyList<SessionStatusField> fields = Build(configure: s =>
        {
            s.DesktopWidth = null;
            s.DesktopHeight = null;
        });

        Assert.Equal(SessionStatusLine.Unknown, Field(fields, "Resolution").Value);
    }

    [Fact]
    public void A_percentage_is_not_claimed_for_a_resolution_that_is_only_a_request()
    {
        // Nothing has been drawn, so there is nothing being scaled. Attaching
        // a percentage to a resolution the far end has not agreed to would be
        // two guesses stacked on each other.
        SessionPlacement placement = SessionScaling.Place(
            new PixelSize(1920, 1080), new PixelSize(960, 540), smartSizing: true);

        IReadOnlyList<SessionStatusField> fields = Build(placement: placement);

        Assert.Equal("1920 × 1080", Field(fields, "Resolution").Value);
    }

    [Fact]
    public void A_session_larger_than_its_tab_says_that_the_tab_scrolls()
    {
        SessionPlacement placement = SessionScaling.Place(
            new PixelSize(1920, 1080), new PixelSize(960, 540), smartSizing: false);

        IReadOnlyList<SessionStatusField> fields = Build(Live, SessionState.Connected, placement);

        Assert.Contains("scrolls", Field(fields, "Resolution").Detail);
    }

    // ── Security ────────────────────────────────────────────────────────

    [Fact]
    public void An_unconnected_session_has_no_security_layer_to_report()
    {
        // Not the configured one. This is the single field where a muted value
        // could be read as an assurance, so there is nothing there to read.
        SessionStatusField security = Field(Build(), "Security");

        Assert.Equal(SessionStatusLine.Unknown, security.Value);
        Assert.Equal(SessionStatusTone.Muted, security.Tone);
        Assert.Contains("negotiated", security.Detail);
    }

    [Fact]
    public void A_connected_session_the_engine_said_nothing_about_says_that_instead()
    {
        SessionStatusField security = Field(Build(state: SessionState.Connected), "Security");

        Assert.Equal(SessionStatusLine.Unknown, security.Value);
        Assert.Contains("did not report", security.Detail);
    }

    [Theory]
    [InlineData(SessionSecurity.NetworkLevel, "TLS + NLA", SessionStatusTone.Normal)]
    [InlineData(SessionSecurity.Tls, "TLS", SessionStatusTone.Warn)]
    [InlineData(SessionSecurity.RdpLegacy, "RDP (legacy)", SessionStatusTone.Bad)]
    public void Each_security_layer_is_named_and_weighed(
        SessionSecurity layer, string expected, SessionStatusTone tone)
    {
        SessionVitals vitals = new() { Security = layer };

        SessionStatusField security = Field(Build(vitals, SessionState.Connected), "Security");

        Assert.Equal(expected, security.Value);
        Assert.Equal(tone, security.Tone);
    }

    [Fact]
    public void Legacy_rdp_security_says_what_is_actually_missing()
    {
        // "Encrypted" is what the far end will happily call it. The thing worth
        // knowing is that nothing has checked who the far end is.
        SessionVitals vitals = new() { Security = SessionSecurity.RdpLegacy };

        Assert.Contains(
            "who is at the other end",
            Field(Build(vitals, SessionState.Connected), "Security").Detail);
    }

    [Fact]
    public void A_connection_asking_for_network_level_that_did_not_get_it_still_shows_what_it_got()
    {
        // The gap between what was asked for and what was agreed to is the
        // whole reason this field is on screen.
        SessionVitals vitals = new() { Security = SessionSecurity.Tls };

        Assert.Equal("TLS", Field(Build(vitals, SessionState.Connected), "Security").Value);
    }

    // ── Gateway ─────────────────────────────────────────────────────────

    [Fact]
    public void No_gateway_configured_is_a_fact_and_not_an_absence()
    {
        SessionStatusField gateway = Field(Build(), "Gateway");

        Assert.Equal("Direct", gateway.Value);
        Assert.Equal(SessionStatusTone.Muted, gateway.Tone);
    }

    [Fact]
    public void A_gateway_that_is_only_configured_is_shown_as_configured()
    {
        IReadOnlyList<SessionStatusField> fields = Build(configure: s =>
        {
            s.GatewayHostName = "gw.example.com";
            s.GatewayUsage = GatewayUsage.Always;
        });

        SessionStatusField gateway = Field(fields, "Gateway");

        Assert.Equal("gw.example.com", gateway.Value);
        Assert.Equal(SessionStatusTone.Muted, gateway.Tone);
        Assert.Contains("Configured", gateway.Detail);
    }

    [Fact]
    public void A_gateway_used_only_when_direct_fails_admits_it_may_not_have_been_used()
    {
        IReadOnlyList<SessionStatusField> fields = Build(configure: s =>
        {
            s.GatewayHostName = "gw.example.com";
            s.GatewayUsage = GatewayUsage.WhenDirectFails;
        });

        Assert.Contains("not known here", Field(fields, "Gateway").Detail);
    }

    [Fact]
    public void A_gateway_the_session_is_really_going_through_stops_being_a_guess()
    {
        SessionVitals vitals = Live with { GatewayHostName = "gw.example.com" };

        IReadOnlyList<SessionStatusField> fields = Build(
            vitals,
            SessionState.Connected,
            configure: s =>
            {
                s.GatewayHostName = "gw.example.com";
                s.GatewayUsage = GatewayUsage.WhenDirectFails;
            });

        SessionStatusField gateway = Field(fields, "Gateway");

        Assert.Equal("gw.example.com", gateway.Value);
        Assert.Equal(SessionStatusTone.Normal, gateway.Tone);
    }

    [Fact]
    public void A_gateway_host_with_usage_set_to_none_is_not_a_gateway()
    {
        // Someone typed a gateway in and then turned it off. Showing it would
        // suggest the session is going somewhere it is not.
        IReadOnlyList<SessionStatusField> fields = Build(configure: s =>
        {
            s.GatewayHostName = "gw.example.com";
            s.GatewayUsage = GatewayUsage.None;
        });

        Assert.Equal("Direct", Field(fields, "Gateway").Value);
    }

    // ── Latency ─────────────────────────────────────────────────────────

    [Fact]
    public void An_unmeasured_round_trip_is_a_dash_and_not_a_zero()
    {
        SessionStatusField latency = Field(Build(Live, SessionState.Connected), "Latency");

        Assert.Equal(SessionStatusLine.Unknown, latency.Value);
        Assert.Equal(SessionStatusTone.Muted, latency.Tone);
    }

    [Theory]
    [InlineData(0.2, "<1 ms")]
    [InlineData(0.4, "<1 ms")]
    [InlineData(1.4, "1 ms")]
    [InlineData(24, "24 ms")]
    [InlineData(249.6, "250 ms")]
    public void A_round_trip_is_reported_in_whole_milliseconds(double milliseconds, string expected)
    {
        // A round trip that rounds to nothing is a loopback or a very short
        // cable, and "0 ms" reads as a broken measurement rather than a fast one.
        SessionVitals vitals = Live with { Latency = TimeSpan.FromMilliseconds(milliseconds) };

        Assert.Equal(expected, Field(Build(vitals, SessionState.Connected), "Latency").Value);
    }

    [Theory]
    [InlineData(24, SessionStatusTone.Normal)]
    [InlineData(150, SessionStatusTone.Normal)]
    [InlineData(151, SessionStatusTone.Warn)]
    [InlineData(300, SessionStatusTone.Warn)]
    [InlineData(301, SessionStatusTone.Bad)]
    public void A_slow_link_is_weighed_by_how_it_feels_to_use(
        double milliseconds, SessionStatusTone expected)
    {
        SessionVitals vitals = Live with { Latency = TimeSpan.FromMilliseconds(milliseconds) };

        Assert.Equal(expected, Field(Build(vitals, SessionState.Connected), "Latency").Tone);
    }

    [Fact]
    public void A_nonsense_round_trip_is_treated_as_no_measurement()
    {
        SessionVitals vitals = Live with { Latency = TimeSpan.FromMilliseconds(-5) };

        Assert.Equal(
            SessionStatusLine.Unknown,
            Field(Build(vitals, SessionState.Connected), "Latency").Value);
    }

    // ── Vitals themselves ───────────────────────────────────────────────

    [Fact]
    public void Unknown_vitals_know_nothing()
    {
        Assert.True(SessionVitals.Unknown.IsUnknown);
        Assert.True(SessionVitals.Unknown.Resolution.IsEmpty);
        Assert.Equal(SessionSecurity.Unknown, SessionVitals.Unknown.Security);
        Assert.Null(SessionVitals.Unknown.GatewayHostName);
        Assert.Null(SessionVitals.Unknown.Latency);
    }

    [Fact]
    public void Any_one_reading_is_enough_to_stop_being_unknown()
    {
        Assert.False((SessionVitals.Unknown with { Latency = TimeSpan.Zero }).IsUnknown);
        Assert.False((SessionVitals.Unknown with { Security = SessionSecurity.Tls }).IsUnknown);
    }

    [Fact]
    public void A_field_prints_as_the_pair_it_is()
    {
        Assert.Equal("Host: web-01", Field(Build(), "Host").ToString());
    }
}
