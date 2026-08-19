namespace Patchbay.Core.Sessions;

/// <summary>
/// Creates sessions. The single seam between the interface and the RDP engine:
/// swap the implementation and the entire UI runs unchanged, which is what
/// lets the tree, the tabs and the status bar be built and tested before any
/// COM interop exists (M4-02).
/// </summary>
public interface IRemoteSessionHost
{
    /// <summary>
    /// What is doing the connecting, in words — "Microsoft RDP client 11", or
    /// what the fake calls itself. Shown in About and in the status bar, and
    /// worth logging with every session.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// True when nothing is really being connected to. The interface must say
    /// so somewhere visible: a simulated session that looks like a real one is
    /// how someone ends up believing they patched a server they never reached.
    /// </summary>
    bool IsSimulated { get; }

    /// <summary>
    /// Builds a session for <paramref name="request"/>. Nothing is connected
    /// until <see cref="IRemoteSession.ConnectAsync"/> is called, so a tab can
    /// exist before its session does.
    /// </summary>
    IRemoteSession CreateSession(SessionRequest request);
}
