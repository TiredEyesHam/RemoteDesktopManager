namespace Patchbay.Rdp.Interop;

/// <summary>
/// How new the scriptable half of an RDP control is.
///
/// Ordered so that <c>&gt;=</c> reads as "at least this generation", which is
/// how every caller wants to ask the question. The numbers match the interface
/// names on purpose — <see cref="Client9"/> is <c>IMsRdpClient9</c> — so a log
/// line naming a level can be looked up in Microsoft's documentation without a
/// translation step.
/// </summary>
public enum RdpClientLevel
{
    /// <summary>Not an RDP control at all, or too broken to say.</summary>
    None = 0,

    /// <summary>Only <c>IMsTscAx</c>: the Windows 2000 Terminal Services control.</summary>
    Base = 1,

    /// <summary><c>IMsRdpClient</c>. Everything between this and <see cref="Client8"/> is folded in here.</summary>
    Client = 2,

    /// <summary><c>IMsRdpClient8</c> — Windows 8 / Server 2012.</summary>
    Client8 = 8,

    /// <summary><c>IMsRdpClient9</c> — Windows 8.1 / Server 2012 R2.</summary>
    Client9 = 9,

    /// <summary>
    /// <c>IMsRdpClient10</c> — Windows 10 onwards, and the end of the line.
    /// There is no eleventh scriptable interface, however high the coclass
    /// numbers on a machine go.
    /// </summary>
    Client10 = 10,
}

/// <summary>
/// How far the non-scriptable chain reaches.
///
/// Tracked separately because it is a different inheritance chain with its own
/// numbering, and because it is the half that matters for credentials: only a
/// non-scriptable interface will take a password from its host. Recorded now,
/// consumed by M3-02 and M4-10.
/// </summary>
public enum RdpNonScriptableLevel
{
    /// <summary>None of them answered. Patchbay cannot supply credentials to this control.</summary>
    None = 0,

    /// <summary><c>IMsTscNonScriptable</c>, which is where <c>ClearTextPassword</c> lives.</summary>
    Base = 1,

    /// <summary><c>IMsRdpClientNonScriptable5</c>.</summary>
    V5 = 5,

    /// <summary><c>IMsRdpClientNonScriptable6</c>.</summary>
    V6 = 6,

    /// <summary><c>IMsRdpClientNonScriptable7</c>.</summary>
    V7 = 7,

    /// <summary><c>IMsRdpClientNonScriptable8</c>.</summary>
    V8 = 8,
}
