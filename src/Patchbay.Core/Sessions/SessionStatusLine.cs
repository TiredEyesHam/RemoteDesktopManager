using System.Globalization;
using Patchbay.Core.Model;

namespace Patchbay.Core.Sessions;

/// <summary>
/// What the status bar says about a session (M5-17): host, resolution,
/// security layer, gateway and latency, in that order.
///
/// <para>
/// <b>One rule runs through all of it.</b> Every field prefers what the engine
/// reported, falls back to what the connection was configured for, and is
/// <see cref="SessionStatusTone.Muted"/> whenever it is showing the second
/// one. So a resolution is muted until the far end agrees to it, a gateway is
/// muted until a session has actually gone through it, and the moment either
/// becomes a fact it stops being muted. The alternative — showing the
/// configured value as though it were the negotiated one — hides the single
/// most useful thing a status bar can tell someone, which is that what they
/// asked for is not what they got.
/// </para>
///
/// <para>
/// <b>All five fields are always present.</b> A status bar whose fields appear
/// and disappear is harder to read than one with dashes in it, because the eye
/// learns where each value lives and then has to find it again. Nothing here
/// ever returns an empty value or a short list.
/// </para>
///
/// <para>
/// This lives in <c>Core</c> because it is entirely decisions — which of two
/// sources to trust, when a value is worth a colour, what to say about it —
/// and none of them are visible in a screenshot. Built in a view model, the
/// rule about muted values would be four scattered ternaries that quietly
/// stopped agreeing with each other.
/// </para>
/// </summary>
public static class SessionStatusLine
{
    /// <summary>
    /// What goes in a value that is not known. An em dash rather than blank,
    /// so the field is visibly empty rather than invisibly missing.
    /// </summary>
    public const string Unknown = "—";

    /// <summary>
    /// Round trip up to here feels immediate; past it, typing starts to lag.
    /// Chosen where it is because RDP stays comfortable to about this and
    /// stops being so fairly sharply afterwards.
    /// </summary>
    private static readonly TimeSpan ComfortableLatency = TimeSpan.FromMilliseconds(150);

    /// <summary>Past this, the session is behind the person using it.</summary>
    private static readonly TimeSpan PainfulLatency = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// The port that needs no mention. Taken from the defaults rather than
    /// written out again, so the two cannot drift apart.
    /// </summary>
    private static readonly int DefaultPort = ConnectionSettings.Defaults.Port!.Value;

    /// <summary>
    /// Builds the line.
    /// </summary>
    /// <param name="request">What the session was opened for.</param>
    /// <param name="state">Where the session is in its life.</param>
    /// <param name="vitals">What the engine has reported. May be <see cref="SessionVitals.Unknown"/>.</param>
    /// <param name="placement">
    /// What is being done with the picture (M5-09), for the percentage.
    /// <see cref="SessionPlacement.Nowhere"/> when nothing is drawn yet.
    /// </param>
    public static IReadOnlyList<SessionStatusField> Build(
        SessionRequest request,
        SessionState state,
        SessionVitals vitals,
        SessionPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(request);

        return
        [
            HostField(request),
            ResolutionField(request, vitals, placement),
            SecurityField(vitals, state),
            GatewayField(request, vitals),
            LatencyField(vitals),
        ];
    }

    private static SessionStatusField HostField(SessionRequest request)
    {
        // The default port on every row is noise, and noise in a status bar is
        // what stops people reading it. A port worth mentioning is one that
        // has been changed.
        string value = request.Port == DefaultPort ? request.HostName : request.Endpoint;

        // The tab is labelled with the node name, so the status bar is the only
        // place the address itself appears. When the two differ, the name goes
        // in the tooltip: that pairing is what someone checks when they want to
        // be sure which machine they are typing into.
        string? detail = string.Equals(request.DisplayName, request.HostName, StringComparison.Ordinal)
            ? null
            : $"{request.DisplayName} · {request.Endpoint}";

        return new SessionStatusField { Label = "Host", Value = value, Detail = detail };
    }

    private static SessionStatusField ResolutionField(
        SessionRequest request,
        SessionVitals vitals,
        SessionPlacement placement)
    {
        bool negotiated = !vitals.Resolution.IsEmpty;

        PixelSize size = negotiated
            ? vitals.Resolution
            : new PixelSize(
                request.Settings.DesktopWidth ?? 0,
                request.Settings.DesktopHeight ?? 0);

        if (size.IsEmpty)
        {
            return new SessionStatusField
            {
                Label = "Resolution",
                Value = Unknown,
                Tone = SessionStatusTone.Muted,
                Detail = "No resolution has been asked for or agreed to.",
            };
        }

        string value = string.Create(CultureInfo.CurrentCulture, $"{size.Width} × {size.Height}");

        // The percentage only when it is not a hundred. "at 100%" is a longer
        // way of writing nothing, and it would be on screen almost always.
        if (negotiated && placement.IsScaled)
        {
            value = string.Create(CultureInfo.CurrentCulture, $"{value} at {placement.ScalePercent}%");
        }

        return new SessionStatusField
        {
            Label = "Resolution",
            Value = value,
            Tone = negotiated ? SessionStatusTone.Normal : SessionStatusTone.Muted,
            Detail = ResolutionDetail(negotiated, placement),
        };
    }

    private static string ResolutionDetail(bool negotiated, SessionPlacement placement)
    {
        if (!negotiated)
        {
            return "The resolution Patchbay will ask for. What the far end agrees to is known "
                + "once the session connects.";
        }

        if (placement.IsScaled)
        {
            return placement.Scale < 1.0
                ? "Scaled down to fit this tab. The remote desktop has not changed size — text is "
                    + "smaller and softer, not smaller and sharper."
                : "Enlarged to fill this tab. The remote desktop has not changed size, so there is "
                    + "no more room on it than there was.";
        }

        return placement.NeedsScrolling
            ? "Shown pixel for pixel. The picture is larger than the tab, so the tab scrolls."
            : "Shown pixel for pixel.";
    }

    private static SessionStatusField SecurityField(SessionVitals vitals, SessionState state)
    {
        // Before there is a connection there is nothing to report, and
        // reporting the configured level instead would be the one place in
        // this type where a muted value could be read as an assurance.
        if (vitals.Security is SessionSecurity.Unknown)
        {
            return new SessionStatusField
            {
                Label = "Security",
                Value = Unknown,
                Tone = SessionStatusTone.Muted,
                Detail = state is SessionState.Connected
                    ? "The RDP engine did not report a security layer for this session."
                    : "The security layer is negotiated when the session connects.",
            };
        }

        return vitals.Security switch
        {
            SessionSecurity.NetworkLevel => new SessionStatusField
            {
                Label = "Security",
                Value = "TLS + NLA",
                Detail = "Both ends were proved before the session was created.",
            },
            SessionSecurity.Tls => new SessionStatusField
            {
                Label = "Security",
                Value = "TLS",
                Tone = SessionStatusTone.Warn,
                Detail = "The server proved itself with a certificate, but the logon happens "
                    + "inside the session rather than before it. Network level authentication "
                    + "would prove both ends first.",
            },
            _ => new SessionStatusField
            {
                Label = "Security",
                Value = "RDP (legacy)",
                Tone = SessionStatusTone.Bad,
                Detail = "The traffic is encrypted, but nothing has checked who is at the other "
                    + "end of it. Anything sitting in the path can read this session.",
            },
        };
    }

    private static SessionStatusField GatewayField(SessionRequest request, SessionVitals vitals)
    {
        // What the engine says, when it says anything.
        if (!string.IsNullOrWhiteSpace(vitals.GatewayHostName))
        {
            return new SessionStatusField
            {
                Label = "Gateway",
                Value = vitals.GatewayHostName,
                Detail = $"This session is going through {vitals.GatewayHostName}.",
            };
        }

        string? configured = request.Settings.GatewayHostName;
        GatewayUsage usage = request.Settings.GatewayUsage ?? GatewayUsage.None;

        if (usage is GatewayUsage.None || string.IsNullOrWhiteSpace(configured))
        {
            return new SessionStatusField
            {
                Label = "Gateway",
                Value = "Direct",
                Tone = SessionStatusTone.Muted,
                Detail = "No gateway is configured for this connection.",
            };
        }

        return new SessionStatusField
        {
            Label = "Gateway",
            Value = configured,
            Tone = SessionStatusTone.Muted,
            Detail = usage is GatewayUsage.Always
                ? $"Configured to route through {configured}, including on the local network."
                : $"Configured to use {configured} only if a direct connection fails, so whether "
                    + "this session went through it is not known here.",
        };
    }

    private static SessionStatusField LatencyField(SessionVitals vitals)
    {
        if (vitals.Latency is not { } latency || latency < TimeSpan.Zero)
        {
            return new SessionStatusField
            {
                Label = "Latency",
                Value = Unknown,
                Tone = SessionStatusTone.Muted,
                Detail = "Nothing has measured a round trip to this machine yet.",
            };
        }

        int milliseconds = (int)Math.Round(latency.TotalMilliseconds, MidpointRounding.AwayFromZero);

        // Below a millisecond is a loopback or a very short cable, and rounding
        // it to "0 ms" reads as a broken measurement rather than a fast one.
        string value = milliseconds < 1
            ? "<1 ms"
            : string.Create(CultureInfo.CurrentCulture, $"{milliseconds} ms");

        return new SessionStatusField
        {
            Label = "Latency",
            Value = value,
            Tone = LatencyTone(latency),
            Detail = LatencyDetail(latency),
        };
    }

    private static SessionStatusTone LatencyTone(TimeSpan latency) =>
        latency > PainfulLatency ? SessionStatusTone.Bad
        : latency > ComfortableLatency ? SessionStatusTone.Warn
        : SessionStatusTone.Normal;

    private static string LatencyDetail(TimeSpan latency) =>
        latency > PainfulLatency
            ? "Round trip to the far end. At this distance the session is visibly behind "
                + "whoever is using it."
        : latency > ComfortableLatency
            ? "Round trip to the far end. Typing and dragging will feel slightly delayed."
            : "Round trip to the far end.";
}
