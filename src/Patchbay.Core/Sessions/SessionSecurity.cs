namespace Patchbay.Core.Sessions;

/// <summary>
/// How a live session is protected, as negotiated (M5-17). Not what was asked
/// for — what was agreed to.
///
/// <para>
/// The distinction the status bar exists to draw is the last one. Legacy RDP
/// security encrypts the traffic and does nothing to establish who is at the
/// other end, so it is defeated by anything sitting in the path; TLS proves
/// the server; network level authentication proves both parties before a
/// session exists at all, which is what stops an unauthenticated stranger
/// making a Windows logon screen appear. Three different things, all of which
/// the far end is happy to call "encrypted".
/// </para>
///
/// <para>
/// This is deliberately not a request setting. The client asks and the server
/// decides, and the gap between the two is exactly the thing worth showing
/// someone: a connection configured for NLA that came up without it connected
/// anyway, and nothing else about it looks different.
/// </para>
/// </summary>
public enum SessionSecurity
{
    /// <summary>
    /// Not connected, or the engine has not said. Reported as nothing rather
    /// than as a reassurance — an unknown security layer is not a safe one.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// The original RDP encryption, with no server certificate. Encrypted to
    /// whoever answered, which is a different promise from the one people
    /// think they are getting.
    /// </summary>
    RdpLegacy = 1,

    /// <summary>
    /// TLS with a server certificate, but the logon happens inside the
    /// session. The server is proved; the client is not, until someone types
    /// a password at a screen the server has already drawn.
    /// </summary>
    Tls = 2,

    /// <summary>
    /// TLS plus network level authentication. Both ends are proved before the
    /// session is created, which is the configuration to want.
    /// </summary>
    NetworkLevel = 3,
}
