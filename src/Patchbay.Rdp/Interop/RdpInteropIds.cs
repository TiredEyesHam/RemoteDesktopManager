namespace Patchbay.Rdp.Interop;

/// <summary>
/// Class identifiers for the RDP ActiveX coclasses in <c>mstscax.dll</c>.
///
/// Two families are registered on every Windows box, and picking the wrong one
/// is a decision Patchbay cannot walk back from:
///
/// <list type="bullet">
///   <item><c>MsTscAx.MsTscAx.N</c> — the "not safe for scripting" family.
///   This is the one a desktop application wants.</item>
///   <item><c>MsRDP.MsRDP.N</c> — the "safe for scripting" redistributable,
///   built for web pages. It refuses to accept a password from its host, which
///   would leave Patchbay unable to do the one thing M3 exists for.</item>
/// </list>
///
/// The two families are also numbered differently — <c>MsRDP.MsRDP.9</c> and
/// <c>MsTscAx.MsTscAx.10</c> are the same control generation — so the trailing
/// number is only meaningful alongside the family it came from. Everything
/// here is the <c>MsTscAx</c> family.
///
/// Note that the coclass number is <b>not</b> the interface number either: the
/// version 10 coclass hands out <c>IMsRdpClient9</c>, and the interface chain
/// stops at <c>IMsRdpClient10</c> no matter how high the coclasses go. Ask the
/// object what it implements (see <see cref="RdpEngineProbe"/>); never infer it
/// from the name.
///
/// Every GUID below was read from the registry and cross-checked against the
/// type library embedded in <c>%SystemRoot%\System32\mstscax.dll</c>
/// (10.0.26100.8875) rather than copied from documentation, because the
/// published lists contradict each other and a wrong CLSID fails as
/// <c>REGDB_E_CLASSNOTREG</c> — indistinguishable, from the outside, from a
/// machine with no RDP client at all.
/// </summary>
/// <remarks>
/// <b>The names here are ProgIDs, and the type library calls the same classes
/// something else.</b> <c>MsTscAx.MsTscAx.12</c> is registered against the
/// coclass the library names <c>MsRdpClient11NotSafeForScripting</c>, and the
/// off-by-one holds all the way down. Both numberings are real and neither is
/// wrong; what would be wrong is reading one as the other, which is a third
/// way to be misled after the coclass-versus-interface trap above. The ProgID
/// spelling is kept because that is the string
/// <see cref="RdpEngineProbe"/> hands to <c>Type.GetTypeFromProgID</c>, and a
/// constant named after something other than the thing beside it is worse than
/// a comment. The true coclass name is on each line.
/// </remarks>
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
/// Interface identifiers, from the same type library and verified the same way.
///
/// Only the generations Patchbay actually branches on are listed. The chain
/// runs <c>IMsRdpClient</c> through <c>IMsRdpClient10</c> with each deriving
/// from the last, so 2 through 7 are implied by 8 and carry no decision.
/// </summary>
internal static class RdpIids
{
    /// <summary>The original Windows 2000-era control. Everything implements it.</summary>
    internal const string IMsTscAx = "8C11EFAE-92C3-11D1-BC1E-00C04FA31489";

    internal const string IMsRdpClient = "92B4A539-7115-4B7C-A5A9-E5D9EFC2780A";
    internal const string IMsRdpClient8 = "4247E044-9271-43A9-BC49-E2AD9E855D62";
    internal const string IMsRdpClient9 = "28904001-04B6-436C-A55B-0AF1A0883DC9";

    /// <summary>
    /// The top of the scriptable chain. There is no <c>IMsRdpClient11</c> —
    /// the backlog's "9/10/11" was reading coclass numbers as interface
    /// numbers. Past 10 the control only grows on the non-scriptable side.
    /// </summary>
    internal const string IMsRdpClient10 = "7ED92C39-EB38-4927-A70A-708AC5A59321";

    /// <summary>
    /// The bottom of the non-scriptable tier: <c>BinaryPassword</c>,
    /// <c>PortablePassword</c>, the two salts, <c>ResetPassword</c> — and
    /// <c>ClearTextPassword</c>, at DISPID 1.
    ///
    /// <para>
    /// <b>M4-10 was expected to need this and does not.</b> This is the
    /// interface every account of RDP credential passing points at, and
    /// reaching it means transcribing a vtable by hand, because it is
    /// <c>IUnknown</c>-derived and answers to no name. The type library says it
    /// is avoidable: <c>ClearTextPassword</c> is also on every generation of
    /// <c>IMsRdpClientAdvancedSettings</c> from the first, at DISPID 186, put
    /// only, reachable late-bound like every other setting — and a harness run
    /// against the live control applies it there and connects. So the tier is
    /// still recorded by the probe, and M4-10 does not use it. What may yet
    /// need it is M3-02, if a stored secret is ever handed over as a blob
    /// rather than in the clear.
    /// </para>
    /// </summary>
    internal const string IMsTscNonScriptable = "C1E6743A-41C1-4A74-832A-0DD06C1C7A0E";

    internal const string IMsRdpClientNonScriptable5 = "4F6996D5-D7B1-412C-B0FF-063718566907";
    internal const string IMsRdpClientNonScriptable6 = "05293249-B28B-4BD8-BE64-1B2F496B910E";
    internal const string IMsRdpClientNonScriptable7 = "71B4A60A-FE21-46D8-A39B-8E32BA0C5ECC";
    internal const string IMsRdpClientNonScriptable8 = "B2B3FA47-3F11-4148-AD24-DFF8684A16D0";

    /// <summary>
    /// The outgoing interface — the one the control calls, and the only one
    /// Patchbay implements rather than consumes (M4-06). Unchanged since the
    /// original control, so there is one of these rather than a chain.
    /// </summary>
    internal const string IMsTscAxEvents = "336D5562-EFA8-482E-8CB3-C5C0FC7A7DB6";
}
