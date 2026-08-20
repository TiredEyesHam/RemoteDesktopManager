namespace Patchbay.Rdp.Interop;

/// <summary>
/// Class identifiers for the RDP ActiveX coclasses in <c>mstscax.dll</c>.
///
/// Two families are registered on every Windows box. <c>MsTscAx.MsTscAx.N</c>
/// is the "not safe for scripting" family and the one a desktop application
/// wants; <c>MsRDP.MsRDP.N</c> is the redistributable built for web pages, and
/// refuses to take a password from its host. Everything here is the first
/// family. The two are numbered differently as well, so a trailing number only
/// means something alongside the family it came from.
///
/// The names below are ProgIDs, which the type library numbers one lower:
/// <c>MsTscAx.MsTscAx.12</c> is the coclass it calls
/// <c>MsRdpClient11NotSafeForScripting</c>. The ProgID spelling is kept
/// because that is the string <see cref="RdpEngineProbe"/> passes to
/// <c>Type.GetTypeFromProgID</c>. The coclass name is on each line.
///
/// The coclass number is not the interface number either — the version 10
/// coclass hands out <c>IMsRdpClient9</c>. Ask the object what it implements
/// rather than inferring it from a name.
///
/// GUIDs were read from the registry and cross-checked against the type
/// library in <c>%SystemRoot%\System32\mstscax.dll</c> (10.0.26100.8875), not
/// copied from documentation: the published lists contradict each other, and a
/// wrong CLSID fails as <c>REGDB_E_CLASSNOTREG</c>, which looks identical to a
/// machine with no RDP client at all.
/// </summary>
internal static class RdpClsids
{
    /// <summary>Coclass <c>MsRdpClient12NotSafeForScripting</c>.</summary>
    internal const string MsTscAx13 = "3F859AA3-C2D4-4FAA-B0E4-FD0C9C4E5E3A";

    /// <summary>Coclass <c>MsRdpClient11NotSafeForScripting</c>.</summary>
    internal const string MsTscAx12 = "1DF7C823-B2D4-4B54-975A-F2AC5D7CF8B8";

    /// <summary>Coclass <c>MsRdpClient10NotSafeForScripting</c>.</summary>
    internal const string MsTscAx11 = "A0C63C30-F08D-4AB4-907C-34905D770C7D";

    /// <summary>Coclass <c>MsRdpClient9NotSafeForScripting</c>.</summary>
    internal const string MsTscAx10 = "8B918B82-7985-4C24-89DF-C33AD2BBFBCD";

    /// <summary>Coclass <c>MsRdpClient8NotSafeForScripting</c>.</summary>
    internal const string MsTscAx9 = "A3BC03A0-041D-42E3-AD22-882B7865C9C5";

    /// <summary>Coclass <c>MsRdpClient7NotSafeForScripting</c>.</summary>
    internal const string MsTscAx8 = "54D38BF7-B1EF-4479-9674-1BD6EA465258";
}

/// <summary>
/// Interface identifiers, from the same type library.
///
/// Only the generations Patchbay branches on. The chain runs
/// <c>IMsRdpClient</c> through <c>IMsRdpClient10</c>, each deriving from the
/// last, so 2 through 7 are implied by 8.
/// </summary>
internal static class RdpIids
{
    /// <summary>The original Windows 2000-era control. Everything implements it.</summary>
    internal const string IMsTscAx = "8C11EFAE-92C3-11D1-BC1E-00C04FA31489";

    internal const string IMsRdpClient = "92B4A539-7115-4B7C-A5A9-E5D9EFC2780A";
    internal const string IMsRdpClient8 = "4247E044-9271-43A9-BC49-E2AD9E855D62";
    internal const string IMsRdpClient9 = "28904001-04B6-436C-A55B-0AF1A0883DC9";

    /// <summary>
    /// The top of the scriptable chain. There is no <c>IMsRdpClient11</c>;
    /// past 10 the control only grows on the non-scriptable side.
    /// </summary>
    internal const string IMsRdpClient10 = "7ED92C39-EB38-4927-A70A-708AC5A59321";

    /// <summary>
    /// The non-scriptable tier: <c>BinaryPassword</c>, <c>PortablePassword</c>,
    /// the two salts, <c>ResetPassword</c>, and <c>ClearTextPassword</c> at
    /// DISPID 1.
    ///
    /// Recorded by the probe but not used. It is <c>IUnknown</c>-derived, so
    /// reaching it means transcribing a vtable by hand, and M4-10 turned out
    /// not to need that: <c>ClearTextPassword</c> is also on every generation
    /// of <c>IMsRdpClientAdvancedSettings</c> at DISPID 186, put only, and
    /// reachable late-bound. M3-02 may need this tier if a stored secret is
    /// ever handed over as a blob.
    /// </summary>
    internal const string IMsTscNonScriptable = "C1E6743A-41C1-4A74-832A-0DD06C1C7A0E";

    internal const string IMsRdpClientNonScriptable5 = "4F6996D5-D7B1-412C-B0FF-063718566907";
    internal const string IMsRdpClientNonScriptable6 = "05293249-B28B-4BD8-BE64-1B2F496B910E";
    internal const string IMsRdpClientNonScriptable7 = "71B4A60A-FE21-46D8-A39B-8E32BA0C5ECC";
    internal const string IMsRdpClientNonScriptable8 = "B2B3FA47-3F11-4148-AD24-DFF8684A16D0";

    /// <summary>
    /// The outgoing interface, the one the control calls and the only one
    /// Patchbay implements rather than consumes. Unchanged since the original
    /// control, so there is no chain of these.
    /// </summary>
    internal const string IMsTscAxEvents = "336D5562-EFA8-482E-8CB3-C5C0FC7A7DB6";
}
