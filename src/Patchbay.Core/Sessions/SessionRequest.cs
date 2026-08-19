using System.Globalization;
using Patchbay.Core.Inheritance;
using Patchbay.Core.Model;

namespace Patchbay.Core.Sessions;

/// <summary>
/// Everything needed to open one session: a host, and settings that have
/// already been through inheritance.
///
/// It is a snapshot, not a view onto the tree. <see cref="For"/> copies the
/// settings it is given, so editing a group while one of its servers is
/// connected cannot reach in and change a live session's configuration
/// half-way through — the change applies to the next connect, which is what
/// people expect and what the RDP control can actually honour.
/// </summary>
public sealed record SessionRequest
{
    private readonly string _hostName = string.Empty;
    private readonly string _displayName = string.Empty;
    private readonly ConnectionSettings _settings = new();

    /// <summary>Host name or IP address to connect to.</summary>
    public required string HostName
    {
        get => _hostName;
        init
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            _hostName = value.Trim();
        }
    }

    /// <summary>
    /// Resolved settings — every property that has a default must already hold
    /// a value. Handing over an unresolved <see cref="ConnectionSettings"/>
    /// straight off a node is rejected rather than quietly connecting to port
    /// zero with nothing redirected.
    /// </summary>
    public required ConnectionSettings Settings
    {
        get => _settings;
        init
        {
            ArgumentNullException.ThrowIfNull(value);

            if (value.Port is null)
            {
                throw new ArgumentException(
                    "Settings must be resolved before they reach a session request — "
                    + $"{nameof(ConnectionSettings.Port)} is still null, which means inherit. "
                    + $"Use {nameof(SettingsResolver)}.{nameof(SettingsResolver.Resolve)} first.",
                    nameof(Settings));
            }

            _settings = value;
        }
    }

    /// <summary>
    /// The sign-in to use for this attempt (M4-10), or
    /// <see cref="SessionCredentials.None"/> to let the control ask.
    ///
    /// <para>
    /// Not part of <see cref="Settings"/> and not copied from a node, because
    /// it does not come from the tree: it is assembled when a session is
    /// opened, from a profile or a prompt, and replaced when somebody answers
    /// a re-prompt. A request is a snapshot, so a session that is reconnecting
    /// with a new password is reconnecting with a <em>new request</em> — which
    /// is what keeps "the password that was refused" and "the password being
    /// tried now" from being the same object with two values over time.
    /// </para>
    /// </summary>
    public SessionCredentials Credentials { get; init; } = SessionCredentials.None;

    /// <summary>The node this came from, so the tab can be tied back to the tree.</summary>
    public Guid NodeId { get; init; }

    /// <summary>What the tab is labelled. Falls back to the host name.</summary>
    public string DisplayName
    {
        get => string.IsNullOrWhiteSpace(_displayName) ? HostName : _displayName;
        init => _displayName = value ?? string.Empty;
    }

    /// <summary>Resolved port. Never null — the <see cref="Settings"/> invariant sees to that.</summary>
    public int Port => Settings.Port!.Value;

    /// <summary>Host and port, for the status bar (M5-17) and for log lines.</summary>
    public string Endpoint => string.Create(CultureInfo.InvariantCulture, $"{HostName}:{Port}");

    /// <summary>
    /// Builds a request from a resolved server node.
    /// </summary>
    /// <param name="effective">
    /// Effective settings for a <see cref="ServerNode"/>, from
    /// <see cref="SettingsResolver.Resolve"/>.
    /// </param>
    /// <exception cref="ArgumentException">
    /// The settings belong to a group. Groups hold settings for their children
    /// to inherit; there is nothing to connect to.
    /// </exception>
    public static SessionRequest For(EffectiveSettings effective)
    {
        ArgumentNullException.ThrowIfNull(effective);

        if (effective.Node is not ServerNode server)
        {
            throw new ArgumentException(
                $"Only a {nameof(ServerNode)} can be connected to, but these settings were "
                + $"resolved for '{effective.Node.Name}', which is a group.",
                nameof(effective));
        }

        return new SessionRequest
        {
            HostName = server.HostName,
            Settings = effective.Values.Clone(),
            NodeId = server.Id,
            DisplayName = server.Name,
        };
    }
}
