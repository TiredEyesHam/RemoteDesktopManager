using Patchbay.Core.Model;
using Patchbay.Core.Sessions;

namespace Patchbay.Tests;

/// <summary>
/// Turning a connection into properties on an RDP control (M4-04).
///
/// Every write goes out late-bound, so none of it is checked by a compiler and
/// all of it is checked here: which settings object a property lives on, which
/// number a gateway mode is, whether a redirection somebody turned off is
/// actually sent. The failures this guards against are all silent ones — a
/// session that connects perfectly well and is not the session that was asked
/// for.
/// </summary>
public class RdpSettingsMapperTests
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

    private static IReadOnlyList<RdpSettingWrite> Plan(Action<ConnectionSettings>? configure = null)
        => RdpSettingsMapper.Plan(RequestFor(configure));

    private static RdpSettingWrite? Find(IReadOnlyList<RdpSettingWrite> plan, string name)
        => plan.FirstOrDefault(w => w.Name == name);

    private static RdpSettingWrite Require(IReadOnlyList<RdpSettingWrite> plan, string name)
        => Find(plan, name) ?? throw new InvalidOperationException(
            $"No write named '{name}'. The plan has: {string.Join(", ", plan.Select(w => w.Name))}");

    // ── The shape of a plan ─────────────────────────────────────────────

    [Fact]
    public void Requires_a_request()
        => Assert.Throws<ArgumentNullException>(() => RdpSettingsMapper.Plan(null!));

    [Fact]
    public void No_property_is_written_twice()
    {
        // Two writes to one property is two answers to one question, and which
        // one wins depends on the order of a list nobody reads.
        IReadOnlyList<RdpSettingWrite> plan = Plan(s =>
        {
            s.UserName = "svc-deploy";
            s.Domain = "CORP";
            s.GatewayHostName = "gw.example.com";
            s.GatewayUsage = GatewayUsage.Always;
        });

        Assert.Equal(
            plan.Select(w => $"{w.Target}.{w.Name}").Distinct().Count(),
            plan.Count);
    }

    [Fact]
    public void Every_write_can_explain_itself()
    {
        foreach (RdpSettingWrite write in Plan(s =>
        {
            s.GatewayHostName = "gw.example.com";
            s.GatewayUsage = GatewayUsage.Always;
        }))
        {
            Assert.False(string.IsNullOrWhiteSpace(write.Name), write.ToString());
            Assert.False(string.IsNullOrWhiteSpace(write.Purpose), write.Name);
            Assert.False(string.IsNullOrWhiteSpace(write.Setting), write.Name);
            Assert.NotNull(write.Value);
        }
    }

    [Fact]
    public void Every_setting_named_in_a_write_is_a_real_setting()
    {
        // A Purpose is prose and can say anything; a Setting has to name
        // something somebody could actually have typed, or a failure notice
        // points at a property that does not exist.
        string[] known =
        [
            .. typeof(ConnectionSettings).GetProperties().Select(p => p.Name),
            .. typeof(ServerNode).GetProperties().Select(p => p.Name),
        ];

        foreach (RdpSettingWrite write in Plan(s =>
        {
            s.UserName = "svc-deploy";
            s.Domain = "CORP";
            s.GatewayHostName = "gw.example.com";
            s.GatewayUsage = GatewayUsage.Always;
        }))
        {
            Assert.Contains(write.Setting, known);
        }
    }

    [Fact]
    public void A_plan_never_carries_a_password()
    {
        // The document does not hold one (M3-02) and this does not invent one.
        // Handing a secret to the control is M4-10, and it will not come
        // through here.
        IReadOnlyList<RdpSettingWrite> plan = Plan(s => s.CredentialMode = CredentialMode.Profile);

        Assert.DoesNotContain(plan, w =>
            w.Name.Contains("Password", StringComparison.OrdinalIgnoreCase)
            || w.Name.Contains("Secret", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Smart_sizing_is_not_in_the_plan()
    {
        // It stopped being a document setting the moment a tab could toggle it
        // (M5-09). Two owners for one property is how a reconnect undoes what
        // somebody just chose.
        Assert.Null(Find(Plan(s => s.UseSmartSizing = false), "SmartSizing"));
    }

    // ── Connection ──────────────────────────────────────────────────────

    [Fact]
    public void The_host_goes_on_the_control_itself()
    {
        RdpSettingWrite server = Require(Plan(), "Server");

        Assert.Equal(RdpSettingTarget.Client, server.Target);
        Assert.Equal("web-01", server.Value);
        Assert.True(server.IsMaterial);
    }

    [Fact]
    public void The_port_goes_on_the_advanced_settings_and_is_not_called_Port()
    {
        RdpSettingWrite port = Require(Plan(), "RDPPort");

        Assert.Equal(RdpSettingTarget.AdvancedSettings, port.Target);
        Assert.Equal(3389, port.Value);
        Assert.True(port.IsMaterial);
    }

    [Fact]
    public void A_port_somebody_changed_is_the_one_that_is_written()
    {
        Assert.Equal(3390, Require(Plan(s => s.Port = 3390), "RDPPort").Value);
    }

    [Fact]
    public void Both_timeouts_are_set_from_the_one_setting()
    {
        // A control given only the per-attempt timeout keeps retrying past the
        // deadline somebody set, which looks like the setting being ignored.
        IReadOnlyList<RdpSettingWrite> plan = Plan(s => s.ConnectTimeoutSeconds = 20);

        Assert.Equal(20, Require(plan, "singleConnectionTimeout").Value);
        Assert.Equal(20, Require(plan, "overallConnectionTimeout").Value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_timeout_of_nothing_is_not_written(int seconds)
    {
        // Zero means "no timeout" to some generations and "give up at once" to
        // others, and neither is what an unset field meant.
        Assert.Null(Find(Plan(s => s.ConnectTimeoutSeconds = seconds), "overallConnectionTimeout"));
    }

    [Fact]
    public void The_console_session_has_an_old_name_to_fall_back_on()
    {
        RdpSettingWrite console = Require(Plan(s => s.ConnectToConsole = true), "ConnectToAdministerServer");

        Assert.Contains("ConnectToServerConsole", console.Alternatives);
        Assert.Equal(true, console.Value);
    }

    [Fact]
    public void Failing_to_reach_the_console_session_matters_and_failing_to_avoid_it_does_not()
    {
        Assert.True(Require(Plan(s => s.ConnectToConsole = true), "ConnectToAdministerServer").IsMaterial);
        Assert.False(Require(Plan(s => s.ConnectToConsole = false), "ConnectToAdministerServer").IsMaterial);
    }

    [Fact]
    public void The_controls_own_reconnect_is_switched_from_the_same_setting()
    {
        // Two layers, one switch (M4-08). Somebody who turns reconnecting off
        // for a machine means both of them, and asking twice would be asking
        // about an implementation detail.
        RdpSettingWrite reconnect = Require(Plan(s => s.AutoReconnect = false), "EnableAutoReconnect");

        Assert.Equal(RdpSettingTarget.AdvancedSettings, reconnect.Target);
        Assert.Equal(false, reconnect.Value);
    }

    [Fact]
    public void Failing_to_stop_the_control_reconnecting_matters_and_failing_to_start_it_does_not()
    {
        // The redirection rule again: a control that reconnects when it was
        // told not to says nothing about it, and the person carries on
        // believing the opposite of what is true. Every control ships with this
        // switched on, so failing to switch it on changes nothing.
        Assert.True(Require(Plan(s => s.AutoReconnect = false), "EnableAutoReconnect").IsMaterial);
        Assert.False(Require(Plan(s => s.AutoReconnect = true), "EnableAutoReconnect").IsMaterial);
    }

    [Fact]
    public void The_attempt_cap_is_left_to_the_control()
    {
        // MaxReconnectAttempts counts transport retries inside a single drop,
        // not reconnects, so pointing Patchbay's attempt limit at it would be
        // two different quantities sharing a number. The control's own default
        // of five is right and no setting in the model means this.
        Assert.Null(Find(Plan(), "MaxReconnectAttempts"));
    }

    // ── Display ─────────────────────────────────────────────────────────

    [Fact]
    public void The_resolution_goes_on_the_control_as_two_writes()
    {
        IReadOnlyList<RdpSettingWrite> plan = Plan();

        Assert.Equal(1920, Require(plan, "DesktopWidth").Value);
        Assert.Equal(1080, Require(plan, "DesktopHeight").Value);
        Assert.Equal(RdpSettingTarget.Client, Require(plan, "DesktopWidth").Target);
    }

    [Fact]
    public void Half_a_resolution_is_not_written_at_all()
    {
        // A control given a width and left with its default height negotiates
        // a resolution nobody chose, which is stranger than not asking.
        IReadOnlyList<RdpSettingWrite> plan = Plan(s => s.DesktopHeight = null);

        Assert.Null(Find(plan, "DesktopWidth"));
        Assert.Null(Find(plan, "DesktopHeight"));
    }

    [Fact]
    public void The_colour_depth_is_spelt_the_way_the_control_spells_it()
    {
        RdpSettingWrite depth = Require(Plan(), "ColorDepth");

        Assert.Equal(32, depth.Value);
        Assert.Equal(nameof(ConnectionSettings.ColourDepth), depth.Setting);
    }

    [Theory]
    [InlineData(ColourDepth.HighColour15, 15)]
    [InlineData(ColourDepth.HighColour16, 16)]
    [InlineData(ColourDepth.TrueColour24, 24)]
    [InlineData(ColourDepth.TrueColour32, 32)]
    public void Every_colour_depth_is_written_as_its_bits(ColourDepth depth, int expected)
    {
        Assert.Equal(expected, Require(Plan(s => s.ColourDepth = depth), "ColorDepth").Value);
    }

    [Fact]
    public void A_resolution_that_did_not_apply_does_not_matter()
    {
        // It announces itself the moment the session draws, which is a better
        // notice than any Patchbay could write.
        Assert.False(Require(Plan(), "DesktopWidth").IsMaterial);
        Assert.False(Require(Plan(), "ColorDepth").IsMaterial);
    }

    // ── Credentials ─────────────────────────────────────────────────────

    [Fact]
    public void A_user_name_and_domain_are_written_when_there_are_any()
    {
        IReadOnlyList<RdpSettingWrite> plan = Plan(s =>
        {
            s.UserName = "svc-deploy";
            s.Domain = "CORP";
        });

        Assert.Equal("svc-deploy", Require(plan, "UserName").Value);
        Assert.Equal("CORP", Require(plan, "Domain").Value);
    }

    [Fact]
    public void An_unset_user_name_is_not_written_as_an_empty_one()
    {
        // Some generations treat "" as an instruction rather than as silence.
        IReadOnlyList<RdpSettingWrite> plan = Plan();

        Assert.Null(Find(plan, "UserName"));
        Assert.Null(Find(plan, "Domain"));
    }

    [Fact]
    public void A_blank_user_name_is_the_same_as_no_user_name()
    {
        Assert.Null(Find(Plan(s => s.UserName = "   "), "UserName"));
    }

    [Fact]
    public void Signing_in_as_the_current_user_turns_credssp_on()
    {
        RdpSettingWrite credssp = Require(
            Plan(s => s.CredentialMode = CredentialMode.CurrentUser), "EnableCredSspSupport");

        Assert.Equal(true, credssp.Value);
        Assert.True(credssp.IsMaterial);
    }

    [Fact]
    public void Signing_in_as_the_current_user_names_no_account()
    {
        // Naming one is how the control is told to use that account instead of
        // the one already signed in, which is the opposite of what was asked.
        IReadOnlyList<RdpSettingWrite> plan = Plan(s =>
        {
            s.CredentialMode = CredentialMode.CurrentUser;
            s.UserName = "svc-deploy";
            s.Domain = "CORP";
        });

        Assert.Null(Find(plan, "UserName"));
        Assert.Null(Find(plan, "Domain"));
    }

    [Fact]
    public void Any_other_credential_mode_leaves_credssp_alone()
    {
        // Whether to demand it in general is M4-09, not this.
        Assert.Null(Find(Plan(s => s.CredentialMode = CredentialMode.Prompt), "EnableCredSspSupport"));
        Assert.Null(Find(Plan(s => s.CredentialMode = CredentialMode.Profile), "EnableCredSspSupport"));
    }

    // ── Gateway ─────────────────────────────────────────────────────────

    [Fact]
    public void No_gateway_produces_no_transport_writes_at_all()
    {
        // Not even an explicit "none". Every control starts there, and writing
        // the default puts a line in the report about an object older controls
        // do not have, describing something nobody asked for.
        Assert.DoesNotContain(Plan(), w => w.Target == RdpSettingTarget.TransportSettings);
    }

    [Fact]
    public void A_gateway_host_with_usage_off_is_still_no_gateway()
    {
        IReadOnlyList<RdpSettingWrite> plan = Plan(s =>
        {
            s.GatewayHostName = "gw.example.com";
            s.GatewayUsage = GatewayUsage.None;
        });

        Assert.DoesNotContain(plan, w => w.Target == RdpSettingTarget.TransportSettings);
    }

    [Fact]
    public void Usage_without_a_host_is_no_gateway_either()
    {
        IReadOnlyList<RdpSettingWrite> plan = Plan(s => s.GatewayUsage = GatewayUsage.Always);

        Assert.DoesNotContain(plan, w => w.Target == RdpSettingTarget.TransportSettings);
    }

    [Fact]
    public void The_gateway_host_is_spelt_with_one_n()
    {
        // GatewayHostName is the obvious spelling and is a silent miss.
        IReadOnlyList<RdpSettingWrite> plan = Plan(s =>
        {
            s.GatewayHostName = "gw.example.com";
            s.GatewayUsage = GatewayUsage.Always;
        });

        Assert.Equal("gw.example.com", Require(plan, "GatewayHostname").Value);
        Assert.Null(Find(plan, "GatewayHostName"));
    }

    [Theory]
    [InlineData(GatewayUsage.Always, 1)]
    [InlineData(GatewayUsage.WhenDirectFails, 2)]
    public void Each_gateway_usage_is_written_as_the_control_number_that_means_it(
        GatewayUsage usage, int expected)
    {
        IReadOnlyList<RdpSettingWrite> plan = Plan(s =>
        {
            s.GatewayHostName = "gw.example.com";
            s.GatewayUsage = usage;
        });

        Assert.Equal(expected, Require(plan, "GatewayUsageMethod").Value);
    }

    [Fact]
    public void The_gateway_named_here_is_the_one_that_is_used()
    {
        // Left at the default, a policy on the machine can send the session
        // through a gateway other than the one on screen.
        IReadOnlyList<RdpSettingWrite> plan = Plan(s =>
        {
            s.GatewayHostName = "gw.example.com";
            s.GatewayUsage = GatewayUsage.Always;
        });

        Assert.Equal(1, Require(plan, "GatewayProfileUsageMethod").Value);
    }

    [Fact]
    public void Every_gateway_write_matters()
    {
        // A gateway that did not apply either fails the connection or quietly
        // goes direct to a machine somebody meant to reach through one.
        IReadOnlyList<RdpSettingWrite> plan = Plan(s =>
        {
            s.GatewayHostName = "gw.example.com";
            s.GatewayUsage = GatewayUsage.Always;
        });

        Assert.All(
            plan.Where(w => w.Target == RdpSettingTarget.TransportSettings),
            w => Assert.True(w.IsMaterial, w.Name));
    }

    // ── Local resources ─────────────────────────────────────────────────

    [Theory]
    [InlineData("RedirectClipboard")]
    [InlineData("RedirectDrives")]
    [InlineData("RedirectPrinters")]
    public void A_redirection_that_was_turned_off_matters(string name)
    {
        // The rule the whole report turns on. A redirection that failed to
        // switch on is noticed the first time somebody tries to use it; one
        // that failed to switch off is never noticed at all.
        IReadOnlyList<RdpSettingWrite> off = Plan(s =>
        {
            s.RedirectClipboard = false;
            s.RedirectDrives = false;
            s.RedirectPrinters = false;
        });

        Assert.True(Require(off, name).IsMaterial);
    }

    [Theory]
    [InlineData("RedirectClipboard")]
    [InlineData("RedirectDrives")]
    [InlineData("RedirectPrinters")]
    public void A_redirection_that_was_turned_on_does_not(string name)
    {
        IReadOnlyList<RdpSettingWrite> on = Plan(s =>
        {
            s.RedirectClipboard = true;
            s.RedirectDrives = true;
            s.RedirectPrinters = true;
        });

        Assert.False(Require(on, name).IsMaterial);
    }

    [Fact]
    public void Redirections_go_on_the_advanced_settings()
    {
        Assert.Equal(
            RdpSettingTarget.AdvancedSettings,
            Require(Plan(), "RedirectClipboard").Target);
    }

    [Theory]
    [InlineData(AudioMode.PlayLocally, 0, false)]
    [InlineData(AudioMode.PlayRemotely, 1, false)]
    [InlineData(AudioMode.DoNotPlay, 2, true)]
    public void Audio_goes_on_the_secured_settings_with_the_mode_as_a_number(
        AudioMode mode, int expected, bool material)
    {
        RdpSettingWrite audio = Require(Plan(s => s.AudioMode = mode), "AudioRedirectionMode");

        Assert.Equal(RdpSettingTarget.SecuredSettings, audio.Target);
        Assert.Equal(expected, audio.Value);
        Assert.Equal(material, audio.IsMaterial);
    }

    // ── Defaults end to end ─────────────────────────────────────────────

    [Fact]
    public void A_connection_with_nothing_configured_still_produces_a_usable_plan()
    {
        // Everything with a default resolves to one, so the plan for an
        // untouched connection is the mstsc-out-of-the-box session.
        string[] names = [.. Plan().Select(w => w.Name)];

        Assert.Equal(
            [
                "Server",
                "RDPPort",
                "singleConnectionTimeout",
                "overallConnectionTimeout",
                "ConnectToAdministerServer",
                "EnableAutoReconnect",
                "DesktopWidth",
                "DesktopHeight",
                "ColorDepth",
                "RedirectClipboard",
                "RedirectDrives",
                "RedirectPrinters",
                "RedirectSmartCards",
                "RedirectPorts",
                "RedirectDevices",
                "RedirectPOSDevices",
                "AudioRedirectionMode",
                "AudioCaptureRedirectionMode",
                "AudioQualityMode",
                "PerformanceFlags",
                "BitmapPersistence",
                "BandwidthDetection",
                "AuthenticationLevel",
                "keepAliveInterval",
                "MinutesToIdleTimeout",
            ],
            names);
    }

    [Fact]
    public void The_names_written_are_the_controls_and_not_the_models()
    {
        // The model spells every one of these differently. Case is not what
        // makes that matter — dispatch lookup ignores it, measured against a
        // real control — but the plan is matched to the type library anyway so
        // that this file and the IDL can be read side by side.
        IReadOnlyList<RdpSettingWrite> plan = Plan(s =>
        {
            s.GatewayHostName = "gw.example.com";
            s.GatewayUsage = GatewayUsage.Always;
            s.GatewayUseSameCredentials = false;
            s.GatewayUserName = "gw-svc";
        });

        // One "n", beside a UserName on the client that has two.
        Assert.NotNull(Find(plan, "GatewayHostname"));
        Assert.NotNull(Find(plan, "GatewayUsername"));

        // Three capitals in the middle.
        Assert.NotNull(Find(plan, "RedirectPOSDevices"));

        // Lower-case k, like the two connection timeouts.
        Assert.NotNull(Find(plan, "keepAliveInterval"));

        // This pair is the one that would genuinely go wrong. They differ by a
        // letter rather than by case, so they are two members rather than one
        // reached two ways, and both are still present on a current control.
        // Picking the older, misspelt one would be a silent miss.
        Assert.NotNull(Find(plan, "BitmapPersistence"));
        Assert.Null(Find(plan, "BitmapPeristence"));
    }

    [Fact]
    public void The_plan_reads_in_the_order_the_settings_are_grouped()
    {
        // Connection, display, credentials, gateway, local resources — the same
        // order as ConnectionSettings itself, so the two can be read side by
        // side when one of them gains a property.
        IReadOnlyList<RdpSettingWrite> plan = Plan(s =>
        {
            s.UserName = "svc-deploy";
            s.GatewayHostName = "gw.example.com";
            s.GatewayUsage = GatewayUsage.Always;
        });

        int user = plan.ToList().FindIndex(w => w.Name == "UserName");
        int gateway = plan.ToList().FindIndex(w => w.Name == "GatewayHostname");
        int clipboard = plan.ToList().FindIndex(w => w.Name == "RedirectClipboard");

        Assert.True(user < gateway, "credentials come before the gateway");
        Assert.True(gateway < clipboard, "the gateway comes before local resources");
    }

    // ── Redirections beyond the first three (M4-13) ─────────────────────

    [Theory]
    [InlineData("RedirectSmartCards")]
    [InlineData("RedirectPorts")]
    [InlineData("RedirectDevices")]
    [InlineData("RedirectPOSDevices")]
    public void Every_redirection_follows_the_same_materiality_rule(string name)
    {
        // Off is material and on is not, throughout. A redirection that failed
        // to switch on is noticed the first time somebody tries to use it; one
        // that failed to switch off is never noticed at all.
        Assert.True(Require(PlanWithRedirections(false), name).IsMaterial);
        Assert.False(Require(PlanWithRedirections(true), name).IsMaterial);
    }

    [Theory]
    [InlineData("RedirectSmartCards")]
    [InlineData("RedirectPorts")]
    [InlineData("RedirectDevices")]
    [InlineData("RedirectPOSDevices")]
    public void Every_redirection_goes_on_the_advanced_settings(string name)
        => Assert.Equal(
            RdpSettingTarget.AdvancedSettings,
            Require(PlanWithRedirections(true), name).Target);

    private static IReadOnlyList<RdpSettingWrite> PlanWithRedirections(bool wanted) => Plan(s =>
    {
        s.RedirectSmartCards = wanted;
        s.RedirectPorts = wanted;
        s.RedirectDevices = wanted;
        s.RedirectPointOfSaleDevices = wanted;
    });

    [Fact]
    public void The_microphone_is_a_boolean_however_much_its_name_says_mode()
    {
        // The control declares AudioCaptureRedirectionMode as a VARIANT_BOOL,
        // beside an AudioRedirectionMode that really is a number. Writing the
        // 0 or 1 the word "mode" invites happens to work and happens to mean
        // the opposite of what the number reads as.
        Assert.Equal(true, Require(Plan(s => s.RedirectMicrophone = true), "AudioCaptureRedirectionMode").Value);
        Assert.Equal(false, Require(Plan(s => s.RedirectMicrophone = false), "AudioCaptureRedirectionMode").Value);
    }

    [Fact]
    public void Losing_the_microphone_is_loud_and_keeping_it_is_quiet()
        => Assert.True(Require(Plan(s => s.RedirectMicrophone = false), "AudioCaptureRedirectionMode").IsMaterial);

    [Theory]
    [InlineData(AudioQuality.Dynamic, 0)]
    [InlineData(AudioQuality.Medium, 1)]
    [InlineData(AudioQuality.High, 2)]
    public void Audio_quality_goes_out_as_the_controls_number(AudioQuality quality, int expected)
        => Assert.Equal(expected, Require(Plan(s => s.AudioQuality = quality), "AudioQualityMode").Value);

    // ── Experience (M4-14) ──────────────────────────────────────────────

    [Fact]
    public void The_experience_checkboxes_arrive_as_one_number()
    {
        // Eight settings, one property. The number is the interesting part and
        // it is worked out in RdpPerformanceFlags, which has the inversions in
        // it and its own tests.
        RdpSettingWrite flags = Require(Plan(), "PerformanceFlags");

        Assert.Equal(RdpSettingTarget.AdvancedSettings, flags.Target);
        Assert.False(flags.IsMaterial);
    }

    [Fact]
    public void Nothing_about_how_it_looks_is_material()
    {
        // All of it is visible the instant the session draws, which is the
        // definition of a failure the report stays quiet about.
        foreach (string name in new[] { "PerformanceFlags", "BitmapPersistence", "BandwidthDetection", "NetworkConnectionType" })
        {
            RdpSettingWrite? write = Find(Plan(s => s.ConnectionQuality = ConnectionQuality.Lan), name);

            if (write is not null)
            {
                Assert.False(write.IsMaterial, name);
            }
        }
    }

    [Fact]
    public void Detecting_the_link_is_written_as_detection_and_not_as_a_speed()
    {
        // Naming a speed and asking the control to measure at the same time is
        // two answers to one question.
        IReadOnlyList<RdpSettingWrite> plan = Plan(s => s.ConnectionQuality = ConnectionQuality.Detect);

        Assert.Equal(true, Require(plan, "BandwidthDetection").Value);
        Assert.Null(Find(plan, "NetworkConnectionType"));
    }

    [Theory]
    [InlineData(ConnectionQuality.Modem, 1)]
    [InlineData(ConnectionQuality.LowSpeedBroadband, 2)]
    [InlineData(ConnectionQuality.Satellite, 3)]
    [InlineData(ConnectionQuality.HighSpeedBroadband, 4)]
    [InlineData(ConnectionQuality.Wan, 5)]
    [InlineData(ConnectionQuality.Lan, 6)]
    public void A_named_link_switches_detection_off_and_names_the_speed(
        ConnectionQuality quality,
        int expected)
    {
        IReadOnlyList<RdpSettingWrite> plan = Plan(s => s.ConnectionQuality = quality);

        Assert.Equal(false, Require(plan, "BandwidthDetection").Value);
        Assert.Equal(expected, Require(plan, "NetworkConnectionType").Value);
    }

    [Fact]
    public void The_bitmap_cache_is_a_number_and_not_a_boolean()
    {
        Assert.Equal(1, Require(Plan(s => s.PersistentBitmapCache = true), "BitmapPersistence").Value);
        Assert.Equal(0, Require(Plan(s => s.PersistentBitmapCache = false), "BitmapPersistence").Value);
    }

    // ── Server authentication (M4-09) ───────────────────────────────────

    [Theory]
    [InlineData(ServerAuthentication.Connect, 0)]
    [InlineData(ServerAuthentication.Require, 1)]
    [InlineData(ServerAuthentication.Warn, 2)]
    public void The_authentication_level_does_not_rise_with_strictness(
        ServerAuthentication authentication,
        int expected)
    {
        // 1 is the strict one and 2 is the lenient one. A cast off an enum
        // ordered the sensible way round would swap exactly the two answers
        // that matter.
        Assert.Equal(
            expected,
            Require(Plan(s => s.ServerAuthentication = authentication), "AuthenticationLevel").Value);
    }

    [Fact]
    public void Asking_for_any_authentication_at_all_is_material()
    {
        // The clearest case in the table: a session to a server nobody proved
        // is pixel-for-pixel a session to one that was, and the difference only
        // shows up after somebody has typed a password into it.
        Assert.True(Require(Plan(s => s.ServerAuthentication = ServerAuthentication.Require), "AuthenticationLevel").IsMaterial);
        Assert.True(Require(Plan(s => s.ServerAuthentication = ServerAuthentication.Warn), "AuthenticationLevel").IsMaterial);
        Assert.False(Require(Plan(s => s.ServerAuthentication = ServerAuthentication.Connect), "AuthenticationLevel").IsMaterial);
    }

    // ── Keep-alive and idle (M4-15) ─────────────────────────────────────

    [Fact]
    public void The_keep_alive_goes_out_in_milliseconds()
    {
        // The control takes milliseconds. The .rdp file setting of the same
        // name takes minutes, which is the sixty-thousand-fold mistake waiting
        // for anybody who reads one and writes the other.
        Assert.Equal(60_000, Require(Plan(s => s.KeepAliveIntervalSeconds = 60), "keepAliveInterval").Value);
    }

    [Fact]
    public void Switching_the_keep_alive_off_is_still_written()
    {
        // Zero is the control's own resting value, so writing it changes
        // nothing — but a group that turns it on and a child that turns it off
        // again is a real arrangement, and skipping the write would leave the
        // child inheriting the parent's interval through the control instead.
        Assert.Equal(0, Require(Plan(s => s.KeepAliveIntervalSeconds = 0), "keepAliveInterval").Value);
    }

    [Fact]
    public void A_negative_keep_alive_is_not_written()
        => Assert.Null(Find(Plan(s => s.KeepAliveIntervalSeconds = -1), "keepAliveInterval"));

    [Fact]
    public void An_idle_timeout_somebody_asked_for_is_material_and_no_timeout_is_not()
    {
        // A session that was supposed to close itself and did not is one left
        // open on an unattended machine, and nothing on screen says so. A
        // session that was never going to close cannot fail to.
        Assert.True(Require(Plan(s => s.IdleTimeoutMinutes = 30), "MinutesToIdleTimeout").IsMaterial);
        Assert.False(Require(Plan(s => s.IdleTimeoutMinutes = 0), "MinutesToIdleTimeout").IsMaterial);
    }

    // ── The gateway account (M4-11) ─────────────────────────────────────

    private static IReadOnlyList<RdpSettingWrite> GatewayPlan(Action<ConnectionSettings>? configure = null)
        => Plan(s =>
        {
            s.GatewayHostName = "gw.example.com";
            s.GatewayUsage = GatewayUsage.Always;
            configure?.Invoke(s);
        });

    [Fact]
    public void A_direct_session_says_nothing_about_a_gateway_account()
    {
        // Not even the defaults. A TransportSettings object an older control
        // does not have would put lines in the report about something nobody
        // asked for.
        IReadOnlyList<RdpSettingWrite> plan = Plan(s => s.GatewayUserName = "gw-svc");

        Assert.Null(Find(plan, "GatewayCredsSource"));
        Assert.Null(Find(plan, "GatewayCredSharing"));
        Assert.Null(Find(plan, "GatewayUsername"));
    }

    [Theory]
    [InlineData(GatewayCredentialSource.Password, 0)]
    [InlineData(GatewayCredentialSource.SmartCard, 1)]
    [InlineData(GatewayCredentialSource.Any, 2 + 2)]
    public void The_gateway_credential_sources_are_not_contiguous(
        GatewayCredentialSource source,
        int expected)
    {
        // "Any" is 4, not 2. A cast would configure a gateway for something
        // nobody chose, and the gateway would refuse it.
        Assert.Equal(
            expected,
            Require(GatewayPlan(s => s.GatewayCredentialSource = source), "GatewayCredsSource").Value);
    }

    [Fact]
    public void Credential_sharing_is_a_number_however_much_it_reads_as_a_switch()
    {
        // Declared UI4 by the control, with booleans on both sides of it in
        // the same interface.
        Assert.Equal(1, Require(GatewayPlan(s => s.GatewayUseSameCredentials = true), "GatewayCredSharing").Value);
        Assert.Equal(0, Require(GatewayPlan(s => s.GatewayUseSameCredentials = false), "GatewayCredSharing").Value);
    }

    [Fact]
    public void A_gateway_account_is_not_written_while_the_credentials_are_shared()
    {
        // Two contradictory instructions, with the control picking. Sharing is
        // on by default, so this is the ordinary case rather than the corner.
        IReadOnlyList<RdpSettingWrite> plan = GatewayPlan(s =>
        {
            s.GatewayUseSameCredentials = true;
            s.GatewayUserName = "gw-svc";
            s.GatewayDomain = "DMZ";
        });

        Assert.Null(Find(plan, "GatewayUsername"));
        Assert.Null(Find(plan, "GatewayDomain"));
    }

    [Fact]
    public void A_gateway_account_is_written_once_the_credentials_are_not_shared()
    {
        IReadOnlyList<RdpSettingWrite> plan = GatewayPlan(s =>
        {
            s.GatewayUseSameCredentials = false;
            s.GatewayUserName = "gw-svc";
            s.GatewayDomain = "DMZ";
        });

        Assert.Equal("gw-svc", Require(plan, "GatewayUsername").Value);
        Assert.Equal("DMZ", Require(plan, "GatewayDomain").Value);
    }

    [Fact]
    public void Every_gateway_write_is_material()
    {
        // A gateway that did not apply either fails the connection or quietly
        // goes direct to a machine somebody meant to reach through one.
        IReadOnlyList<RdpSettingWrite> plan = GatewayPlan(s =>
        {
            s.GatewayUseSameCredentials = false;
            s.GatewayUserName = "gw-svc";
            s.GatewayDomain = "DMZ";
        });

        foreach (RdpSettingWrite write in plan.Where(w => w.Target is RdpSettingTarget.TransportSettings))
        {
            Assert.True(write.IsMaterial, write.Name);
        }
    }

    [Fact]
    public void The_gateway_account_lives_on_the_transport_settings_like_the_rest_of_it()
    {
        IReadOnlyList<RdpSettingWrite> plan = GatewayPlan(s =>
        {
            s.GatewayUseSameCredentials = false;
            s.GatewayUserName = "gw-svc";
        });

        Assert.Equal(RdpSettingTarget.TransportSettings, Require(plan, "GatewayUsername").Target);
        Assert.Equal(RdpSettingTarget.TransportSettings, Require(plan, "GatewayCredsSource").Target);
    }
}
