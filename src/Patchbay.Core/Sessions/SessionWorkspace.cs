namespace Patchbay.Core.Sessions;

/// <summary>
/// The open sessions and which one is in front (M5-01).
///
/// A tab strip looks like a list and a highlight, and almost is. What it is
/// not is the two rules underneath, both of which are easy to get wrong and
/// neither of which is visible in the XAML:
///
/// <list type="bullet">
///   <item><b>Closing the front tab has to leave something in front.</b> The
///   one that slides into its place, or the last one if there was nothing to
///   the right. Leaving nothing selected while tabs remain shows an empty pane
///   next to a strip full of live sessions.</item>
///   <item><b>Opening a machine that is already open brings it forward</b>
///   rather than starting a second session to it. Two sessions to one server
///   almost always means the first was forgotten, and on Windows Server the
///   second frequently ends the first.</item>
/// </list>
///
/// Both live here, in <c>Core</c>, so they can be tested without a window.
/// What is left for the App is the strip itself.
///
/// <para>
/// Deliberately without events: everything here happens because someone
/// clicked, so the caller already knows what changed and can read the result.
/// A session that ends on its own announces that through its own
/// <see cref="IRemoteSession.StateChanged"/> — the tab stays open, because a
/// tab that vanishes when a server reboots takes the reconnect button with it.
/// </para>
///
/// <para>
/// <b>Threading.</b> Belongs to the thread that made it, like the sessions
/// inside it.
/// </para>
/// </summary>
public sealed class SessionWorkspace : IDisposable
{
    private readonly IRemoteSessionHost _host;
    private readonly List<IRemoteSession> _sessions = [];

    private bool _disposed;

    public SessionWorkspace(IRemoteSessionHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
    }

    /// <summary>What is doing the connecting, in words. Worth showing.</summary>
    public string HostDescription => _host.Description;

    /// <summary>
    /// True when nothing is really being connected to. The interface has to
    /// say so somewhere: a simulated session that looks real is how someone
    /// comes to believe they patched a server they never reached.
    /// </summary>
    public bool IsSimulated => _host.IsSimulated;

    /// <summary>The open sessions, in the order their tabs appear.</summary>
    public IReadOnlyList<IRemoteSession> Sessions => _sessions;

    /// <summary>The one in front, or null when nothing is open.</summary>
    public IRemoteSession? Active { get; private set; }

    public int Count => _sessions.Count;

    /// <summary>
    /// Brings the session for <paramref name="request"/> to the front, opening
    /// one if it is not already there. Nothing is connected — that is the
    /// caller's next move, and a tab exists before its session does.
    /// </summary>
    public IRemoteSession Open(SessionRequest request)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(request);

        if (Find(request.NodeId) is { } existing)
        {
            Active = existing;
            return existing;
        }

        IRemoteSession session = _host.CreateSession(request);

        _sessions.Add(session);
        Active = session;

        return session;
    }

    /// <summary>
    /// The open session for a tree node, if there is one. Nodes are matched by
    /// id rather than by host name, so two entries pointing at the same
    /// machine keep their own tabs — they usually differ in the credentials or
    /// the gateway, which is the whole reason both exist.
    ///
    /// A request with no node behind it never matches: it did not come from
    /// the tree, so there is nothing to be the same as.
    /// </summary>
    public IRemoteSession? Find(Guid nodeId) =>
        nodeId == Guid.Empty
            ? null
            : _sessions.FirstOrDefault(session => session.Request.NodeId == nodeId);

    /// <summary>
    /// Brings a session forward. Returns false for one this workspace does not
    /// have, rather than throwing: a tab can be closed between the click and
    /// the handler, and that is not worth a crash.
    /// </summary>
    public bool Activate(IRemoteSession session)
    {
        if (session is null || !_sessions.Contains(session))
        {
            return false;
        }

        Active = session;
        return true;
    }

    /// <summary>
    /// Closes a tab and ends its session. Disposing is what disconnects; a
    /// caller that wants a session wound down politely should await
    /// <see cref="IRemoteSession.DisconnectAsync"/> first and close after.
    /// </summary>
    public void Close(IRemoteSession session)
    {
        int index = _sessions.IndexOf(session);

        if (index < 0)
        {
            return;
        }

        bool wasActive = ReferenceEquals(Active, session);

        // Out of the list before it is disposed, so that anything watching the
        // session's last state change sees a workspace that already agrees the
        // tab has gone.
        _sessions.RemoveAt(index);

        if (wasActive)
        {
            Active = NextAfter(index);
        }

        session.Dispose();
    }

    /// <summary>Closes everything, front to back. Used when the window closes.</summary>
    public void CloseAll()
    {
        // Copied first: Close mutates the list it would otherwise be walking.
        foreach (IRemoteSession session in _sessions.ToArray())
        {
            Close(session);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        CloseAll();
    }

    /// <summary>
    /// What should be in front once the tab at <paramref name="index"/> has
    /// gone: the one that slid into its place, or the last one if it was at
    /// the end. Null only when nothing is left.
    /// </summary>
    private IRemoteSession? NextAfter(int index) =>
        _sessions.Count == 0 ? null : _sessions[Math.Min(index, _sessions.Count - 1)];
}
