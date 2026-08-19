using Patchbay.Core.Model;
using Patchbay.Core.Sessions;

namespace Patchbay.Tests;

/// <summary>
/// The part of a tab strip that can be wrong (M5-01). Which tab is in front
/// after a close, and whether opening a machine twice gets you two tabs, are
/// decisions with right answers; the strip that draws them is not.
/// </summary>
public class SessionWorkspaceTests
{
    private static SessionRequest RequestFor(string name, Guid? nodeId = null) => new()
    {
        HostName = name,
        Settings = ConnectionSettings.Defaults,
        DisplayName = name.ToUpperInvariant(),
        NodeId = nodeId ?? Guid.NewGuid(),
    };

    private static (SessionWorkspace Workspace, IRemoteSession[] Tabs) With(int tabs)
    {
        SessionWorkspace workspace = new(new FakeRemoteSessionHost());
        IRemoteSession[] opened = new IRemoteSession[tabs];

        for (int i = 0; i < tabs; i++)
        {
            opened[i] = workspace.Open(RequestFor($"web-{i:00}"));
        }

        return (workspace, opened);
    }

    [Fact]
    public void A_new_workspace_has_nothing_open()
    {
        SessionWorkspace workspace = new(new FakeRemoteSessionHost());

        Assert.Empty(workspace.Sessions);
        Assert.Null(workspace.Active);
        Assert.True(workspace.IsSimulated);
    }

    [Fact]
    public void Opening_a_connection_puts_it_in_front()
    {
        (SessionWorkspace workspace, IRemoteSession[] tabs) = With(3);

        Assert.Equal(3, workspace.Count);
        Assert.Same(tabs[2], workspace.Active);
    }

    [Fact]
    public void Nothing_is_connected_by_opening_a_tab()
    {
        (SessionWorkspace workspace, IRemoteSession[] tabs) = With(1);

        // A tab exists before its session does, which is what lets the strip
        // show "connecting" rather than appearing once the server answers.
        Assert.Equal(SessionState.Idle, tabs[0].State);
        Assert.Equal(1, workspace.Count);
    }

    [Fact]
    public void Opening_a_machine_that_is_already_open_brings_it_forward()
    {
        SessionWorkspace workspace = new(new FakeRemoteSessionHost());
        Guid node = Guid.NewGuid();

        IRemoteSession first = workspace.Open(RequestFor("web-01", node));
        workspace.Open(RequestFor("web-02"));
        IRemoteSession again = workspace.Open(RequestFor("web-01", node));

        // Two live sessions to one server usually means the first was
        // forgotten, and on Windows Server the second often ends the first.
        Assert.Same(first, again);
        Assert.Equal(2, workspace.Count);
        Assert.Same(first, workspace.Active);
    }

    [Fact]
    public void Two_entries_pointing_at_one_machine_keep_their_own_tabs()
    {
        SessionWorkspace workspace = new(new FakeRemoteSessionHost());

        workspace.Open(RequestFor("web-01"));
        workspace.Open(RequestFor("web-01"));

        // Same host name, different nodes: they differ in credentials or
        // gateway, which is why both exist in the tree.
        Assert.Equal(2, workspace.Count);
    }

    [Fact]
    public void A_session_opened_from_nowhere_never_matches_another()
    {
        SessionWorkspace workspace = new(new FakeRemoteSessionHost());

        workspace.Open(RequestFor("web-01", Guid.Empty));
        workspace.Open(RequestFor("web-01", Guid.Empty));

        Assert.Equal(2, workspace.Count);
        Assert.Null(workspace.Find(Guid.Empty));
    }

    [Fact]
    public void Closing_the_front_tab_promotes_the_one_to_its_right()
    {
        (SessionWorkspace workspace, IRemoteSession[] tabs) = With(3);
        workspace.Activate(tabs[0]);

        workspace.Close(tabs[0]);

        Assert.Same(tabs[1], workspace.Active);
    }

    [Fact]
    public void Closing_the_last_tab_falls_back_to_the_one_on_its_left()
    {
        (SessionWorkspace workspace, IRemoteSession[] tabs) = With(3);

        workspace.Close(tabs[2]);

        Assert.Same(tabs[1], workspace.Active);
    }

    [Fact]
    public void Closing_a_tab_that_was_not_in_front_leaves_the_front_alone()
    {
        (SessionWorkspace workspace, IRemoteSession[] tabs) = With(3);

        workspace.Close(tabs[0]);

        Assert.Same(tabs[2], workspace.Active);
        Assert.Equal(2, workspace.Count);
    }

    [Fact]
    public void Closing_the_only_tab_leaves_nothing_in_front()
    {
        (SessionWorkspace workspace, IRemoteSession[] tabs) = With(1);

        workspace.Close(tabs[0]);

        Assert.Null(workspace.Active);
        Assert.Empty(workspace.Sessions);
    }

    [Fact]
    public async Task Closing_a_tab_ends_its_session()
    {
        (SessionWorkspace workspace, IRemoteSession[] tabs) = With(1);

        workspace.Close(tabs[0]);

        // Disposed, so using it again is refused rather than quietly working
        // on a session nobody can see.
        await Assert.ThrowsAsync<ObjectDisposedException>(() => tabs[0].ConnectAsync());
    }

    [Fact]
    public void A_session_the_workspace_never_had_is_ignored()
    {
        (SessionWorkspace workspace, IRemoteSession[] tabs) = With(2);
        using IRemoteSession stranger = new FakeRemoteSessionHost().CreateSession(RequestFor("web-99"));

        workspace.Close(stranger);

        Assert.False(workspace.Activate(stranger));
        Assert.Equal(2, workspace.Count);
        Assert.Same(tabs[1], workspace.Active);
    }

    [Fact]
    public async Task Closing_everything_closes_everything()
    {
        (SessionWorkspace workspace, IRemoteSession[] tabs) = With(4);

        workspace.CloseAll();

        Assert.Empty(workspace.Sessions);
        Assert.Null(workspace.Active);

        foreach (IRemoteSession tab in tabs)
        {
            await Assert.ThrowsAsync<ObjectDisposedException>(() => tab.ConnectAsync());
        }
    }

    [Fact]
    public async Task A_disposed_workspace_takes_its_sessions_with_it()
    {
        (SessionWorkspace workspace, IRemoteSession[] tabs) = With(2);

        workspace.Dispose();
        workspace.Dispose();

        Assert.Empty(workspace.Sessions);
        Assert.Throws<ObjectDisposedException>(() => workspace.Open(RequestFor("web-01")));

        foreach (IRemoteSession tab in tabs)
        {
            await Assert.ThrowsAsync<ObjectDisposedException>(() => tab.ConnectAsync());
        }
    }

    [Fact]
    public async Task A_tab_stays_open_when_its_session_ends_on_its_own()
    {
        FakeRemoteSessionHost host = new();
        SessionWorkspace workspace = new(host);
        IRemoteSession session = workspace.Open(RequestFor("web-01"));

        await session.ConnectAsync();
        host.Sessions[0].SimulateDisconnect();

        // A tab that vanishes when a server reboots takes the reconnect button
        // with it, and leaves someone wondering whether they imagined it.
        Assert.Equal(1, workspace.Count);
        Assert.Same(session, workspace.Active);
        Assert.Equal(SessionState.Disconnected, session.State);
    }
}
