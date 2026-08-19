using Patchbay.Core.Inheritance;
using Patchbay.Core.Model;
using Patchbay.Core.Sessions;

namespace Patchbay.Tests;

public class SessionRequestTests
{
    private static ServerNode ServerIn(GroupNode parent, string name = "WEB-PRD-01")
    {
        ServerNode server = new() { Name = name, HostName = "10.0.0.5" };
        parent.Add(server);
        return server;
    }

    [Fact]
    public void A_request_carries_the_host_the_name_and_the_node_it_came_from()
    {
        GroupNode root = new() { Name = "All" };
        ServerNode server = ServerIn(root);

        SessionRequest request = SessionRequest.For(SettingsResolver.Resolve(server));

        Assert.Equal("10.0.0.5", request.HostName);
        Assert.Equal("WEB-PRD-01", request.DisplayName);
        Assert.Equal(server.Id, request.NodeId);
    }

    [Fact]
    public void The_port_comes_through_inheritance_like_any_other_setting()
    {
        GroupNode root = new() { Name = "All" };
        root.Settings.Port = 3390;
        ServerNode server = ServerIn(root);

        SessionRequest request = SessionRequest.For(SettingsResolver.Resolve(server));

        Assert.Equal(3390, request.Port);
        Assert.Equal("10.0.0.5:3390", request.Endpoint);
    }

    [Fact]
    public void An_unset_port_falls_through_to_the_default_rather_than_staying_null()
    {
        GroupNode root = new() { Name = "All" };

        SessionRequest request = SessionRequest.For(SettingsResolver.Resolve(ServerIn(root)));

        Assert.Equal(3389, request.Port);
    }

    /// <summary>
    /// The whole point of the snapshot: a group edited during a live session
    /// must not reach into that session's configuration.
    /// </summary>
    [Fact]
    public void Editing_the_tree_afterwards_does_not_change_a_request()
    {
        GroupNode root = new() { Name = "All" };
        root.Settings.Port = 3390;
        ServerNode server = ServerIn(root);

        SessionRequest request = SessionRequest.For(SettingsResolver.Resolve(server));

        root.Settings.Port = 4000;
        server.Settings.RedirectDrives = true;

        Assert.Equal(3390, request.Port);
        Assert.False(request.Settings.RedirectDrives);
    }

    [Fact]
    public void A_group_cannot_be_connected_to()
    {
        GroupNode root = new() { Name = "Production" };

        ArgumentException error =
            Assert.Throws<ArgumentException>(() => SessionRequest.For(SettingsResolver.Resolve(root)));

        Assert.Contains("Production", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_server_with_no_host_name_is_rejected()
    {
        GroupNode root = new() { Name = "All" };
        ServerNode server = new() { Name = "Half finished", HostName = "   " };
        root.Add(server);

        Assert.Throws<ArgumentException>(() => SessionRequest.For(SettingsResolver.Resolve(server)));
    }

    /// <summary>
    /// Unresolved settings mean "inherit", and a session cannot inherit from
    /// anything. Connecting to port zero is the failure this prevents.
    /// </summary>
    [Fact]
    public void Settings_that_have_not_been_resolved_are_rejected()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() =>
            new SessionRequest { HostName = "web-01", Settings = new ConnectionSettings() });

        Assert.Contains(nameof(ConnectionSettings.Port), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unnamed_request_is_labelled_with_its_host()
    {
        SessionRequest request = new()
        {
            HostName = " web-01 ",
            Settings = ConnectionSettings.Defaults,
        };

        Assert.Equal("web-01", request.HostName);
        Assert.Equal("web-01", request.DisplayName);
    }
}
