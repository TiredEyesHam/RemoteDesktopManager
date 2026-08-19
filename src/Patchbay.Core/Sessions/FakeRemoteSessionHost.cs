namespace Patchbay.Core.Sessions;

/// <summary>
/// A session host that connects to nothing.
///
/// This is the reason M4-01 comes before the rest of M4 (sequencing rule 1):
/// the tree, the tabs, the status bar and the disconnect handling can all be
/// built and tested against it, on any machine, with no server to connect to
/// and no COM object to marshal. The awkward paths — a connect that fails, a
/// connect cancelled half-way, a session dropped by the far end — are the ones
/// hardest to produce on demand against a real host, so this makes each of
/// them a property you set.
///
/// It ships rather than living in the test project because the App falls back
/// to it when the RDP engine is missing, and because a session that is not
/// real has to be able to say so — see <see cref="IsSimulated"/>.
/// </summary>
public sealed class FakeRemoteSessionHost : IRemoteSessionHost
{
    private readonly Lock _gate = new();
    private readonly List<FakeRemoteSession> _sessions = [];

    public string Description => "Simulated session host — nothing is really connected";

    public bool IsSimulated => true;

    /// <summary>
    /// How long a connect takes. Zero by default, so tests do not pay for
    /// realism; the App sets a second or so to exercise the spinner.
    /// </summary>
    public TimeSpan ConnectDelay { get; set; }

    /// <summary>How long a disconnect takes. Zero by default, as above.</summary>
    public TimeSpan DisconnectDelay { get; set; }

    /// <summary>
    /// Decides whether a connect fails: return a message to fail with, or null
    /// to let it through. Left null, everything connects.
    /// </summary>
    public Func<SessionRequest, string?>? ConnectFailure { get; set; }

    /// <summary>
    /// The security layer a simulated session claims to have negotiated
    /// (M5-17). Deliberately <see cref="SessionSecurity.Tls"/> rather than the
    /// best of the three: a fake that always reports the configuration to want
    /// is a fake that hides the status bar field which exists to notice when
    /// something weaker was agreed to.
    /// </summary>
    public SessionSecurity SimulatedSecurity { get; set; } = SessionSecurity.Tls;

    /// <summary>
    /// The round trip a simulated session reports, or null for none — which is
    /// what a session with no probe attached to it should say, and the default
    /// until M5-18 attaches one.
    /// </summary>
    public TimeSpan? SimulatedLatency { get; set; }

    /// <summary>Every session made here, in the order they were made.</summary>
    public IReadOnlyList<FakeRemoteSession> Sessions
    {
        get
        {
            lock (_gate)
            {
                return [.. _sessions];
            }
        }
    }

    public IRemoteSession CreateSession(SessionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        FakeRemoteSession session = new(request, this);

        lock (_gate)
        {
            _sessions.Add(session);
        }

        return session;
    }

    /// <summary>Fails every subsequent connect with <paramref name="message"/>.</summary>
    public void FailConnections(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ConnectFailure = _ => message;
    }
}
