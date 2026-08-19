namespace Patchbay.Core.Sessions;

/// <summary>
/// What the control says about how the server proved itself (M4-09), and the
/// one engine-reported fact behind <see cref="SessionVitals.Security"/>.
///
/// <para>
/// <b>The certificate itself is not on offer, and that is the finding this
/// type carries.</b> M4-09 asked for a warning showing the server's subject,
/// thumbprint and expiry with a trust-once button, and none of it can be built
/// on this control. Nothing in the type library of <c>mstscax.dll</c>
/// (10.0.26100.8875) hands the container a server certificate — the one member
/// with the word in its name, <c>PublisherCertificateChain</c>, is for signing
/// RemoteApp publishers and has nothing to do with the machine at the other
/// end. The control puts its own warning up, keeps its own record of what was
/// answered, and tells the container only that a warning appeared and later
/// went away. So Patchbay's share of this is three things: choosing whether
/// the warning appears at all (<c>AuthenticationLevel</c>, M4-04), saying
/// while it is up that the session is waiting on a person (M4-06), and this —
/// reporting afterwards what was actually agreed to.
/// </para>
///
/// <para>
/// <c>AuthenticationType</c> is read-only, lives on the advanced settings from
/// <c>IMsRdpClientAdvancedSettings6</c> onwards, and is documented with the
/// four values below. It answers a narrower question than
/// <see cref="SessionSecurity"/> does — how the <em>server</em> was proved,
/// not what the session as a whole is worth — so the mapping is deliberately
/// not one to one, and the places it declines to guess are the point of it.
/// </para>
/// </summary>
public static class RdpAuthenticationType
{
    /// <summary>
    /// No authentication was used. Also what an unconnected control answers,
    /// which is why <see cref="ToSecurity"/> refuses to read anything into it.
    /// </summary>
    public const int None = 0;

    /// <summary>The server was proved by a certificate.</summary>
    public const int Certificate = 1;

    /// <summary>The server was proved by Kerberos.</summary>
    public const int Kerberos = 2;

    /// <summary>Both.</summary>
    public const int CertificateAndKerberos = 3;

    /// <summary>
    /// What a session with this authentication type is worth saying about
    /// (M5-17). Two rules run through it, and both are about not saying more
    /// than the control actually said.
    ///
    /// <para>
    /// <b>Kerberos means network level authentication.</b> Kerberos only
    /// enters an RDP connection through CredSSP, and CredSSP is what NLA is —
    /// so a session the control says was proved by Kerberos was proved before
    /// it existed, which is the whole of what
    /// <see cref="SessionSecurity.NetworkLevel"/> claims.
    /// </para>
    ///
    /// <para>
    /// <b>A certificate on its own is reported as TLS even though it may be
    /// more.</b> NLA against a machine that is not in a domain runs over NTLM
    /// rather than Kerberos, and the control reports only the certificate — so
    /// some sessions reported here as TLS really did prove both ends. That
    /// under-reports, on purpose: this field is read as an assurance, and a
    /// field that occasionally claims NLA it cannot demonstrate is worth less
    /// than one that occasionally withholds it.
    /// </para>
    ///
    /// <para>
    /// <b>Zero is <see cref="SessionSecurity.Unknown"/> and not
    /// <see cref="SessionSecurity.RdpLegacy"/>.</b> Zero is documented as "no
    /// authentication is used", which is exactly what legacy RDP security is —
    /// but it is also what the property reads before anything has connected,
    /// and this repo has no server yet against which to tell a control that
    /// means it from a control that has not filled the value in. Reading it as
    /// legacy would paint the status bar red on every session if that guess is
    /// wrong, and an alarm that is wrong every time is one people stop seeing.
    /// Unknown is muted and says the engine did not report a layer, which is
    /// true either way. <b>This is the one thing on the M4-17 matrix that
    /// changes a line of code:</b> connect to a server three times — NLA on,
    /// NLA off, and legacy RDP security — write down the three values, and if
    /// zero really does mean legacy on a live session, map it here.
    /// </para>
    /// </summary>
    public static SessionSecurity ToSecurity(int authenticationType) => authenticationType switch
    {
        Kerberos or CertificateAndKerberos => SessionSecurity.NetworkLevel,
        Certificate => SessionSecurity.Tls,
        _ => SessionSecurity.Unknown,
    };
}
