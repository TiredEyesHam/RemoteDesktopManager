using Patchbay.Core.Model;

namespace Patchbay.Core.Sessions;

/// <summary>
/// Turns a resolved <see cref="SessionRequest"/> into the list of properties
/// to write on an RDP control (M4-04).
///
/// A plan rather than a sequence of calls. Every write goes out late-bound by
/// name, so the compiler checks none of it; a list can be checked, and built
/// and tested in a project with no COM and no control to talk to. That puts
/// the decisions worth getting wrong somewhere they can be asserted: which
/// settings object a property lives on, which number a gateway mode is,
/// whether a redirection that was turned off actually got sent.
///
/// Nothing a document holds is skipped, and nothing it does not hold is
/// invented. The password comes from the <see cref="SessionRequest"/> and
/// never from <see cref="ConnectionSettings"/> (M3-02, M4-10), so saving a
/// connection file cannot write a secret into it. Smart sizing is not here at
/// all — M5-09 owns it once a tab can toggle it.
///
/// Names are copied from the type library. A control given a name it does not
/// have reports <c>Unsupported</c> and carries on, so a wrong name fails
/// silently and produces a session nobody configured.
///
/// Case does not matter: dispatch lookup is case-insensitive, and
/// <c>redirectdrives</c>, <c>RedirectPosDevices</c> and <c>GatewayUserName</c>
/// were all measured resolving against the real control. Letters do matter.
/// <c>BitmapPersistence</c> sits beside <c>BitmapPeristence</c>, missing its
/// second <c>s</c>; both are real and both still present, so they are two
/// words rather than one word twice. An undeclared name such as
/// <c>BitmapPersistance</c> is rejected with <c>DISP_E_UNKNOWNNAME</c>.
///
/// A setting left null after resolution has no default and nothing in the
/// ancestry, and produces no write rather than a write of an empty string.
/// The control treats "" as an instruction.
/// </summary>
public static class RdpSettingsMapper
{
    /// <summary>
    /// Builds the plan.
    /// </summary>
    /// <param name="request">A request whose settings have already been resolved.</param>
    public static IReadOnlyList<RdpSettingWrite> Plan(SessionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        ConnectionSettings settings = request.Settings;
        List<RdpSettingWrite> plan = [];

        AddConnection(plan, request, settings);
        AddDisplay(plan, settings);
        AddCredentials(plan, request, settings);
        AddGateway(plan, settings);
        AddLocalResources(plan, settings);
        AddExperience(plan, settings);
        AddSecurity(plan, settings);
        AddAdvanced(plan, settings);

        return plan;
    }

    // ── Connection ──────────────────────────────────────────────────────

    private static void AddConnection(
        List<RdpSettingWrite> plan,
        SessionRequest request,
        ConnectionSettings settings)
    {
        plan.Add(new RdpSettingWrite
        {
            Target = RdpSettingTarget.Client,
            Name = "Server",
            Value = request.HostName,
            Setting = nameof(ServerNode.HostName),
            Purpose = "The machine to connect to",
            IsMaterial = true,
        });

        plan.Add(new RdpSettingWrite
        {
            Target = RdpSettingTarget.AdvancedSettings,
            Name = "RDPPort",
            Value = request.Port,
            Setting = nameof(ConnectionSettings.Port),
            Purpose = "The port",
            IsMaterial = true,
        });

        if (settings.ConnectTimeoutSeconds is { } timeout and > 0)
        {
            // Two properties, both lower-cased at the front, both in seconds.
            // The single one bounds one attempt and the overall one bounds the
            // lot, and a control given only the first will keep retrying past
            // the deadline someone set.
            plan.Add(Timeout("singleConnectionTimeout", timeout));
            plan.Add(Timeout("overallConnectionTimeout", timeout));
        }

        if (settings.ConnectToConsole is { } console)
        {
            plan.Add(new RdpSettingWrite
            {
                Target = RdpSettingTarget.AdvancedSettings,
                Name = "ConnectToAdministerServer",
                Alternatives = ["ConnectToServerConsole"],
                Value = console,
                Setting = nameof(ConnectionSettings.ConnectToConsole),
                Purpose = "Connecting to the administrative session",

                // Only when it was asked for. Failing to switch it on lands
                // somebody in an ordinary session when they wanted the console;
                // failing to switch it off changes nothing, because off is
                // where every control starts.
                IsMaterial = console,
            });
        }

        if (settings.AutoReconnect is { } reconnect)
        {
            // The control's own reconnect, which is not Patchbay's (M4-08) and
            // is the better of the two where it applies: it holds an
            // auto-reconnect cookie and rejoins the *same* session, desktop and
            // open windows intact, where a fresh connect gets a new one. It
            // only covers the transport going away for a moment, which is why
            // there is a second layer above it for the cases it cannot reach —
            // a reboot, a gateway restart, a laptop closed for an hour.
            //
            // MaxReconnectAttempts is deliberately left alone. It counts
            // transport retries inside a single drop, not reconnects, so
            // pointing Patchbay's attempt limit at it would be two different
            // quantities sharing a number; the control's own default of five is
            // right and there is no setting in the model that means this.
            plan.Add(new RdpSettingWrite
            {
                Target = RdpSettingTarget.AdvancedSettings,
                Name = "EnableAutoReconnect",
                Value = reconnect,
                Setting = nameof(ConnectionSettings.AutoReconnect),
                Purpose = "Letting the control rejoin a session it briefly lost",

                // Only when it was turned off, by the same rule as the
                // redirections: a control that reconnects when it was told not
                // to says nothing about it, and the person is left believing
                // the opposite of what is true. Every control starts with this
                // switched on, so failing to switch it on changes nothing.
                IsMaterial = !reconnect,
            });
        }
    }

    private static RdpSettingWrite Timeout(string name, int seconds) => new()
    {
        Target = RdpSettingTarget.AdvancedSettings,
        Name = name,
        Value = seconds,
        Setting = nameof(ConnectionSettings.ConnectTimeoutSeconds),
        Purpose = "The connection timeout",
    };

    // ── Display ─────────────────────────────────────────────────────────

    private static void AddDisplay(List<RdpSettingWrite> plan, ConnectionSettings settings)
    {
        // Both or neither. A control given a width and left with the default
        // height negotiates a resolution nobody chose, which is a stranger
        // outcome than not asking at all.
        if (settings.DesktopWidth is { } width and > 0 && settings.DesktopHeight is { } height and > 0)
        {
            plan.Add(Desktop("DesktopWidth", width, nameof(ConnectionSettings.DesktopWidth)));
            plan.Add(Desktop("DesktopHeight", height, nameof(ConnectionSettings.DesktopHeight)));
        }

        if (settings.ColourDepth is { } depth)
        {
            plan.Add(new RdpSettingWrite
            {
                Target = RdpSettingTarget.Client,

                // American spelling, because it is the control's name and not
                // Patchbay's. The model spells it the other way and the two
                // meet here, which is the whole job of a mapper.
                Name = "ColorDepth",
                Value = (int)depth,
                Setting = nameof(ConnectionSettings.ColourDepth),
                Purpose = "The colour depth",
            });
        }
    }

    private static RdpSettingWrite Desktop(string name, int value, string setting) => new()
    {
        Target = RdpSettingTarget.Client,
        Name = name,
        Value = value,
        Setting = setting,
        Purpose = "The resolution",
    };

    // ── Credentials ─────────────────────────────────────────────────────

    /// <summary>
    /// Names the account to sign in as (M4-04), preferring the sign-in the
    /// attempt was given over the one the document remembers (M4-10).
    ///
    /// The two are the same on an ordinary connect. They differ when somebody
    /// has been refused, asked again, and typed a different account, and
    /// sending the stored name back there would retry what was just turned
    /// down.
    ///
    /// The domain travels with the user name and not on its own. An empty
    /// domain is how a local account is expressed, so falling back to the
    /// document's realm would attach it to a freshly typed local account and
    /// fail in a way that looks like a bad password.
    /// </summary>
    private static void AddCredentials(
        List<RdpSettingWrite> plan,
        SessionRequest request,
        ConnectionSettings settings)
    {
        CredentialMode mode = settings.CredentialMode ?? Model.CredentialMode.Prompt;

        if (mode is Model.CredentialMode.CurrentUser)
        {
            // Single sign-on is CredSSP handing over the signed-in ticket, so
            // asking for one without the other is asking for a logon prompt.
            // The general authentication policy — whether to demand server
            // authentication, what to do about a certificate nobody trusts —
            // is M4-09, and is deliberately not decided here.
            plan.Add(new RdpSettingWrite
            {
                Target = RdpSettingTarget.AdvancedSettings,
                Name = "EnableCredSspSupport",
                Value = true,
                Setting = nameof(ConnectionSettings.CredentialMode),
                Purpose = "Signing in as the current Windows user",
                IsMaterial = true,
            });

            // No user name, no domain and no password: naming an account is
            // how the control is told to use that one instead of the one
            // already signed in, and a password sent alongside single sign-on
            // is a secret handed over for no reason.
            return;
        }

        bool fromAttempt = request.Credentials.UserName.Length > 0;

        string userName = fromAttempt ? request.Credentials.UserName : settings.UserName ?? string.Empty;
        string domain = fromAttempt ? request.Credentials.Domain : settings.Domain ?? string.Empty;
        string source = fromAttempt
            ? nameof(SessionRequest.Credentials)
            : nameof(ConnectionSettings.UserName);

        if (!string.IsNullOrWhiteSpace(userName))
        {
            plan.Add(new RdpSettingWrite
            {
                Target = RdpSettingTarget.Client,
                Name = "UserName",
                Value = userName,
                Setting = source,
                Purpose = "The user name",
            });
        }

        if (!string.IsNullOrWhiteSpace(domain))
        {
            plan.Add(new RdpSettingWrite
            {
                Target = RdpSettingTarget.Client,
                Name = "Domain",
                Value = domain,
                Setting = fromAttempt ? source : nameof(ConnectionSettings.Domain),
                Purpose = "The domain",
            });
        }

        AddPassword(plan, request);
    }

    /// <summary>
    /// Hands the control a password, when the attempt was given one (M4-10).
    ///
    /// It comes from the request and never from the settings: a document holds
    /// a user name, a domain and a profile id, never a secret, so writing a
    /// connection file cannot write a password into it.
    ///
    /// <c>ClearTextPassword</c> is write-only and sits on the advanced
    /// settings at DISPID 186, present since the first RDP-branded control.
    /// Worth saying because the obvious place to look is
    /// <c>IMsTscNonScriptable</c>, where it also exists and where reaching it
    /// would mean transcribing a vtable by hand.
    ///
    /// Not material. A password that did not reach the control produces a
    /// logon screen, which is visible and fixable by the person looking at it,
    /// and nothing is left less protected than was asked for.
    /// </summary>
    private static void AddPassword(List<RdpSettingWrite> plan, SessionRequest request)
    {
        if (!request.Credentials.HasPassword)
        {
            return;
        }

        plan.Add(new RdpSettingWrite
        {
            Target = RdpSettingTarget.AdvancedSettings,
            Name = "ClearTextPassword",
            Value = request.Credentials.Password,
            Setting = nameof(SessionRequest.Credentials),
            Purpose = "The password",
            IsSecret = true,
        });
    }

    // ── Gateway ─────────────────────────────────────────────────────────

    private static void AddGateway(List<RdpSettingWrite> plan, ConnectionSettings settings)
    {
        GatewayUsage usage = settings.GatewayUsage ?? Model.GatewayUsage.None;

        // Nothing at all rather than an explicit "no gateway". Every control
        // starts with none, and writing the default would put a line in the
        // report about a TransportSettings object that older generations do
        // not have, describing a thing nobody asked for.
        if (usage is Model.GatewayUsage.None || string.IsNullOrWhiteSpace(settings.GatewayHostName))
        {
            return;
        }

        plan.Add(new RdpSettingWrite
        {
            Target = RdpSettingTarget.TransportSettings,

            // One "n", as the type library spells it. Dispatch lookup ignores
            // case, so GatewayHostName would have reached the same property;
            // this matches the IDL so the two read alike.
            Name = "GatewayHostname",
            Value = settings.GatewayHostName,
            Setting = nameof(ConnectionSettings.GatewayHostName),
            Purpose = "The gateway",
            IsMaterial = true,
        });

        plan.Add(new RdpSettingWrite
        {
            Target = RdpSettingTarget.TransportSettings,
            Name = "GatewayUsageMethod",
            Value = ProxyMode(usage),
            Setting = nameof(ConnectionSettings.GatewayUsage),
            Purpose = "How the gateway is used",
            IsMaterial = true,
        });

        plan.Add(new RdpSettingWrite
        {
            Target = RdpSettingTarget.TransportSettings,
            Name = "GatewayProfileUsageMethod",

            // 1 is "explicit": use the gateway named above rather than
            // whatever a policy on this machine would have chosen. Left at the
            // default, a domain policy can redirect the session through a
            // gateway other than the one on screen.
            Value = 1,
            Setting = nameof(ConnectionSettings.GatewayHostName),
            Purpose = "Using the gateway that is configured here",
            IsMaterial = true,
        });

        AddGatewayCredentials(plan, settings);
    }

    /// <summary>
    /// Who the gateway is told is connecting (M4-11). Reached only when there
    /// is a gateway, so nothing here is written for a direct session.
    ///
    /// Every write is material: a gateway asked for one account and given
    /// another either refuses the session, which is loud and fine, or accepts
    /// it as somebody else, which is not.
    /// </summary>
    private static void AddGatewayCredentials(List<RdpSettingWrite> plan, ConnectionSettings settings)
    {
        if (settings.GatewayCredentialSource is { } source)
        {
            plan.Add(new RdpSettingWrite
            {
                Target = RdpSettingTarget.TransportSettings,
                Name = "GatewayCredsSource",
                Value = CredentialSource(source),
                Setting = nameof(ConnectionSettings.GatewayCredentialSource),
                Purpose = "What the gateway is offered as proof",
                IsMaterial = true,
            });
        }

        if (settings.GatewayUseSameCredentials is { } sharing)
        {
            plan.Add(new RdpSettingWrite
            {
                Target = RdpSettingTarget.TransportSettings,
                Name = "GatewayCredSharing",

                // A number, not a boolean, whatever the name suggests: the
                // control declares it UI4 and the two neighbours either side of
                // it are booleans, which is exactly the sort of neighbourhood
                // that produces a confident wrong guess.
                Value = sharing ? 1 : 0,
                Setting = nameof(ConnectionSettings.GatewayUseSameCredentials),
                Purpose = "Offering the gateway the same credentials as the server",
                IsMaterial = true,
            });
        }

        // Only when they were configured, and only when they are not going to
        // be ignored. Naming a gateway account while credential sharing is on
        // writes two contradictory instructions and lets the control pick.
        if (settings.GatewayUseSameCredentials is true)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(settings.GatewayUserName))
        {
            plan.Add(new RdpSettingWrite
            {
                Target = RdpSettingTarget.TransportSettings,

                // One capital, beside GatewayHostname, which also has one, and
                // beside UserName on the client, which has two. The control
                // does not care which; the type library does, and so does
                // anyone reading the two side by side.
                Name = "GatewayUsername",
                Value = settings.GatewayUserName,
                Setting = nameof(ConnectionSettings.GatewayUserName),
                Purpose = "The gateway user name",
                IsMaterial = true,
            });
        }

        if (!string.IsNullOrWhiteSpace(settings.GatewayDomain))
        {
            plan.Add(new RdpSettingWrite
            {
                Target = RdpSettingTarget.TransportSettings,
                Name = "GatewayDomain",
                Value = settings.GatewayDomain,
                Setting = nameof(ConnectionSettings.GatewayDomain),
                Purpose = "The gateway domain",
                IsMaterial = true,
            });
        }
    }

    /// <summary>
    /// The control's number for a credential source. Written out because the
    /// set is not contiguous — "any" is 4, not 2 — and a cast would produce a
    /// gateway configured for something nobody chose.
    /// </summary>
    private static int CredentialSource(GatewayCredentialSource source) => source switch
    {
        Model.GatewayCredentialSource.SmartCard => 1,
        Model.GatewayCredentialSource.Any => 4,
        _ => 0,
    };

    /// <summary>
    /// The control's proxy mode for a usage. Written out rather than cast: the
    /// numbers happen to line up with <see cref="GatewayUsage"/> today, and a
    /// coincidence is not a contract — 3 and 4 exist in the control's set and
    /// have no counterpart here.
    /// </summary>
    private static int ProxyMode(GatewayUsage usage) => usage switch
    {
        Model.GatewayUsage.Always => 1,
        Model.GatewayUsage.WhenDirectFails => 2,
        _ => 0,
    };

    // ── Local resources ─────────────────────────────────────────────────

    private static void AddLocalResources(List<RdpSettingWrite> plan, ConnectionSettings settings)
    {
        Redirect(plan, "RedirectClipboard", settings.RedirectClipboard, nameof(ConnectionSettings.RedirectClipboard), "Clipboard redirection");
        Redirect(plan, "RedirectDrives", settings.RedirectDrives, nameof(ConnectionSettings.RedirectDrives), "Drive redirection");
        Redirect(plan, "RedirectPrinters", settings.RedirectPrinters, nameof(ConnectionSettings.RedirectPrinters), "Printer redirection");
        Redirect(plan, "RedirectSmartCards", settings.RedirectSmartCards, nameof(ConnectionSettings.RedirectSmartCards), "Smart card redirection");
        Redirect(plan, "RedirectPorts", settings.RedirectPorts, nameof(ConnectionSettings.RedirectPorts), "Serial and parallel port redirection");
        Redirect(plan, "RedirectDevices", settings.RedirectDevices, nameof(ConnectionSettings.RedirectDevices), "Plug-and-play device redirection");

        // Three capitals in the middle of a name that is otherwise ordinary.
        // RedirectPosDevices reaches the same property — dispatch lookup is
        // case-insensitive — so this is the type library's spelling rather
        // than a requirement.
        Redirect(plan, "RedirectPOSDevices", settings.RedirectPointOfSaleDevices, nameof(ConnectionSettings.RedirectPointOfSaleDevices), "Point-of-sale device redirection");

        if (settings.AudioMode is { } audio)
        {
            plan.Add(new RdpSettingWrite
            {
                Target = RdpSettingTarget.SecuredSettings,
                Name = "AudioRedirectionMode",
                Value = (int)audio,
                Setting = nameof(ConnectionSettings.AudioMode),
                Purpose = "Where remote audio is played",

                // Same rule as a redirection. Failing to silence a session is
                // a surprise in an open-plan office; failing to bring its sound
                // over is a quiet one.
                IsMaterial = audio is Model.AudioMode.DoNotPlay,
            });
        }

        if (settings.RedirectMicrophone is { } microphone)
        {
            plan.Add(new RdpSettingWrite
            {
                Target = RdpSettingTarget.AdvancedSettings,

                // A mode by name and a boolean by type, which is how the
                // control declares it. Passing the 0 or 1 that "mode" suggests
                // happens to work and happens to mean the opposite of what the
                // number looks like it means.
                Name = "AudioCaptureRedirectionMode",
                Value = microphone,
                Setting = nameof(ConnectionSettings.RedirectMicrophone),
                Purpose = "Recording from this computer's microphone",
                IsMaterial = !microphone,
            });
        }

        if (settings.AudioQuality is { } quality)
        {
            plan.Add(new RdpSettingWrite
            {
                Target = RdpSettingTarget.AdvancedSettings,
                Name = "AudioQualityMode",
                Value = (int)quality,
                Setting = nameof(ConnectionSettings.AudioQuality),
                Purpose = "How much bandwidth audio may use",
            });
        }
    }

    // ── Experience ──────────────────────────────────────────────────────

    /// <summary>
    /// How the desktop is allowed to look, and what the link is (M4-14).
    ///
    /// Nothing here is material. Any of these failing shows up in the picture
    /// the moment it draws, and a warning raised every time an older control
    /// declines a flag is one people learn to dismiss unread.
    /// </summary>
    private static void AddExperience(List<RdpSettingWrite> plan, ConnectionSettings settings)
    {
        plan.Add(new RdpSettingWrite
        {
            Target = RdpSettingTarget.AdvancedSettings,
            Name = "PerformanceFlags",
            Value = RdpPerformanceFlags.For(settings),
            Setting = nameof(ConnectionSettings.DesktopBackground),
            Purpose = "How the remote desktop is allowed to look",
        });

        if (settings.PersistentBitmapCache is { } cache)
        {
            plan.Add(new RdpSettingWrite
            {
                Target = RdpSettingTarget.AdvancedSettings,

                // The spelt-correctly one, and the only name in this file
                // where getting it wrong actually costs anything. There is
                // also BitmapPeristence, missing its second s, on the oldest
                // interface; both are still present on a control from this
                // year. Case-insensitivity is no help here, because these are
                // two different words rather than one word spelt two ways.
                Name = "BitmapPersistence",
                Value = cache ? 1 : 0,
                Setting = nameof(ConnectionSettings.PersistentBitmapCache),
                Purpose = "Keeping the bitmap cache between sessions",
            });
        }

        if (settings.ConnectionQuality is not { } link)
        {
            return;
        }

        // Detect is not a link type, so it is not written as one. Naming a
        // speed and asking the control to measure at the same time is two
        // answers to one question.
        if (link is Model.ConnectionQuality.Detect)
        {
            plan.Add(new RdpSettingWrite
            {
                Target = RdpSettingTarget.AdvancedSettings,
                Name = "BandwidthDetection",
                Value = true,
                Setting = nameof(ConnectionSettings.ConnectionQuality),
                Purpose = "Measuring the link rather than being told about it",
            });

            return;
        }

        plan.Add(new RdpSettingWrite
        {
            Target = RdpSettingTarget.AdvancedSettings,
            Name = "BandwidthDetection",
            Value = false,
            Setting = nameof(ConnectionSettings.ConnectionQuality),
            Purpose = "Using the link speed that is configured here",
        });

        plan.Add(new RdpSettingWrite
        {
            Target = RdpSettingTarget.AdvancedSettings,
            Name = "NetworkConnectionType",
            Value = (int)link,
            Setting = nameof(ConnectionSettings.ConnectionQuality),
            Purpose = "What sort of link this is",
        });
    }

    // ── Security ────────────────────────────────────────────────────────

    /// <summary>
    /// What to do about a server that cannot prove who it is (M4-09).
    ///
    /// Material whenever it asks for more than nothing. A control that did not
    /// take "require authentication" connects to whatever answered, and a
    /// session to an unauthenticated server looks pixel-for-pixel like a
    /// session to an authenticated one until somebody types a password into
    /// it.
    /// </summary>
    private static void AddSecurity(List<RdpSettingWrite> plan, ConnectionSettings settings)
    {
        if (settings.ServerAuthentication is not { } authentication)
        {
            return;
        }

        plan.Add(new RdpSettingWrite
        {
            Target = RdpSettingTarget.AdvancedSettings,
            Name = "AuthenticationLevel",
            Value = AuthenticationLevel(authentication),
            Setting = nameof(ConnectionSettings.ServerAuthentication),
            Purpose = "What to do about a server that cannot be authenticated",
            IsMaterial = authentication is not Model.ServerAuthentication.Connect,
        });
    }

    /// <summary>
    /// The control's number for an authentication choice. Written out, and the
    /// order is the trap: 1 is the strict one and 2 is the lenient one, so the
    /// numbers do not rise with strictness and a cast off a sensibly-ordered
    /// enum would swap the two answers that matter.
    /// </summary>
    private static int AuthenticationLevel(ServerAuthentication authentication) => authentication switch
    {
        Model.ServerAuthentication.Require => 1,
        Model.ServerAuthentication.Warn => 2,
        _ => 0,
    };

    // ── Advanced ────────────────────────────────────────────────────────

    /// <summary>
    /// Keep-alive and the idle timeout (M4-15).
    /// </summary>
    private static void AddAdvanced(List<RdpSettingWrite> plan, ConnectionSettings settings)
    {
        if (settings.KeepAliveIntervalSeconds is { } keepAlive and >= 0)
        {
            plan.Add(new RdpSettingWrite
            {
                Target = RdpSettingTarget.AdvancedSettings,

                // Lower-case k, like the two connection timeouts and unlike
                // every other property on this object. Cosmetic — the lookup
                // ignores case — but it is what the IDL says.
                Name = "keepAliveInterval",

                // Milliseconds, which is not what the .rdp file setting of the
                // same name uses — that one is in minutes. The unit is the
                // whole content of this setting, and getting it wrong by sixty
                // thousand produces either a flood or a silence.
                Value = keepAlive * 1000,
                Setting = nameof(ConnectionSettings.KeepAliveIntervalSeconds),
                Purpose = "How often the client checks the session is alive",
            });
        }

        if (settings.IdleTimeoutMinutes is { } idle and >= 0)
        {
            plan.Add(new RdpSettingWrite
            {
                Target = RdpSettingTarget.AdvancedSettings,
                Name = "MinutesToIdleTimeout",
                Value = idle,
                Setting = nameof(ConnectionSettings.IdleTimeoutMinutes),
                Purpose = "How long the session may sit idle",

                // Material only when there is a timeout to miss. A session that
                // was supposed to close itself and did not is one left open on
                // an unattended machine, and nothing on screen says so; a
                // session that was never going to close cannot fail to.
                IsMaterial = idle > 0,
            });
        }
    }

    /// <summary>
    /// One redirection. Off is material and on is not, which is the rule the
    /// whole report turns on: a redirection that failed to switch on is
    /// noticed the first time somebody tries to use it, and one that failed to
    /// switch off is never noticed at all.
    /// </summary>
    private static void Redirect(
        List<RdpSettingWrite> plan,
        string name,
        bool? value,
        string setting,
        string purpose)
    {
        if (value is not { } wanted)
        {
            return;
        }

        plan.Add(new RdpSettingWrite
        {
            Target = RdpSettingTarget.AdvancedSettings,
            Name = name,
            Value = wanted,
            Setting = setting,
            Purpose = purpose,
            IsMaterial = !wanted,
        });
    }
}
