using Patchbay.Core.Sessions;

namespace Patchbay.Tests;

/// <summary>
/// What the control's <c>AuthenticationType</c> is allowed to be turned into
/// (M4-09).
///
/// <para>
/// This is a four-value mapping and it would be a one-line switch if the only
/// question were arithmetic. It is not: the status bar reads this field as an
/// assurance, so the interesting cases are the two where the honest answer is
/// less than the tempting one — a certificate that may or may not have carried
/// network level authentication with it, and a zero that means either "nothing
/// proved the server" or "nothing has connected yet". Both are asserted here
/// so that a later reader who thinks they look wrong finds the reason attached
/// rather than changing them.
/// </para>
/// </summary>
public class RdpAuthenticationTypeTests
{
    // ── What the control can say ────────────────────────────────────────

    [Fact]
    public void Kerberos_means_both_ends_were_proved_before_the_session_existed()
    {
        // Kerberos only reaches an RDP connection through CredSSP, and CredSSP
        // is what network level authentication is. So this one is not an
        // inference about the client from a fact about the server — it is the
        // same fact.
        Assert.Equal(
            SessionSecurity.NetworkLevel,
            RdpAuthenticationType.ToSecurity(RdpAuthenticationType.Kerberos));

        Assert.Equal(
            SessionSecurity.NetworkLevel,
            RdpAuthenticationType.ToSecurity(RdpAuthenticationType.CertificateAndKerberos));
    }

    [Fact]
    public void A_certificate_alone_is_reported_as_no_more_than_TLS()
    {
        // Even though it is sometimes more. Network level authentication
        // against a machine outside a domain runs over NTLM, and the control
        // reports only the certificate — so some of the sessions this calls
        // TLS really did prove both ends. Withholding an assurance that cannot
        // be demonstrated is the error worth making here; the other direction
        // puts "TLS + NLA" on screen on the strength of a guess.
        Assert.Equal(
            SessionSecurity.Tls,
            RdpAuthenticationType.ToSecurity(RdpAuthenticationType.Certificate));
    }

    [Fact]
    public void Nothing_proving_the_server_is_reported_as_not_known_rather_than_as_legacy()
    {
        // Zero is documented as "no authentication is used", which is exactly
        // what legacy RDP security is — and it is also what the property reads
        // before anything has connected. Until a real server settles which of
        // those a live session produces (M4-17), reading it as legacy would
        // risk a red badge on every session, and an alarm that is always wrong
        // is one people stop seeing. Unknown is muted and says the engine did
        // not report a layer, which is true either way.
        Assert.Equal(
            SessionSecurity.Unknown,
            RdpAuthenticationType.ToSecurity(RdpAuthenticationType.None));
    }

    [Fact]
    public void A_value_nobody_documented_is_not_guessed_at()
    {
        Assert.Equal(SessionSecurity.Unknown, RdpAuthenticationType.ToSecurity(4));
        Assert.Equal(SessionSecurity.Unknown, RdpAuthenticationType.ToSecurity(-1));
        Assert.Equal(SessionSecurity.Unknown, RdpAuthenticationType.ToSecurity(int.MaxValue));
    }

    // ── The numbers themselves ──────────────────────────────────────────

    [Fact]
    public void The_four_values_are_the_ones_the_control_documents()
    {
        // Pinned because they are the whole contract with the control and
        // nothing else in the build would notice them changing.
        Assert.Equal(0, RdpAuthenticationType.None);
        Assert.Equal(1, RdpAuthenticationType.Certificate);
        Assert.Equal(2, RdpAuthenticationType.Kerberos);
        Assert.Equal(3, RdpAuthenticationType.CertificateAndKerberos);
    }

    [Fact]
    public void No_authentication_type_reports_a_layer_stronger_than_the_control_proved()
    {
        // The property that must never fail: whatever the mapping grows into,
        // NetworkLevel is the one answer that claims something about the
        // client, and only Kerberos is evidence of it.
        for (int type = -2; type <= 8; type++)
        {
            if (RdpAuthenticationType.ToSecurity(type) is SessionSecurity.NetworkLevel)
            {
                Assert.Contains(
                    type,
                    new[] { RdpAuthenticationType.Kerberos, RdpAuthenticationType.CertificateAndKerberos });
            }
        }
    }
}
