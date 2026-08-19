using Patchbay.Core.Model;
using Patchbay.Core.Validation;

namespace Patchbay.Tests;

public class NodeValidatorTests
{
    [Theory]
    [InlineData("dc01")]
    [InlineData("WEB-PRD-01")]
    [InlineData("web.corp.local")]
    [InlineData("web.corp.local.")]
    [InlineData("10.20.4.11")]
    [InlineData("::1")]
    [InlineData("2001:db8::1")]
    [InlineData("[2001:db8::1]")]
    [InlineData("build_server")]
    public void Real_host_names_are_accepted(string host) =>
        Assert.True(NodeValidator.IsValidHost(host));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("-leading")]
    [InlineData("trailing-")]
    [InlineData("two..dots")]
    [InlineData("has space")]
    [InlineData("has/slash")]
    [InlineData("rdp://web")]
    public void Nonsense_host_names_are_rejected(string host) =>
        Assert.False(NodeValidator.IsValidHost(host));

    /// <summary>
    /// IPAddress.TryParse reads a bare number as an integer IPv4 address, so
    /// "12345" parses as 0.0.48.57. Nobody typing that means an address, and a
    /// machine really can be called 12345.
    /// </summary>
    [Fact]
    public void A_bare_number_is_a_host_name_not_an_address()
    {
        Assert.True(NodeValidator.IsValidHost("12345"));
        Assert.Empty(NodeValidator.ValidateServer("Box", "12345", null));
    }

    [Fact]
    public void A_label_over_sixty_three_characters_is_rejected() =>
        Assert.False(NodeValidator.IsValidHost(new string('a', 64)));

    [Fact]
    public void A_valid_server_produces_no_issues() =>
        Assert.Empty(NodeValidator.ValidateServer("WEB-PRD-01", "10.20.4.11", 3389));

    [Fact]
    public void A_missing_name_and_host_are_both_reported()
    {
        IReadOnlyList<ValidationIssue> issues = NodeValidator.ValidateServer("  ", "", null);

        Assert.Equal(2, issues.Count);
        Assert.Contains(issues, i => i.Field == NodeValidator.NameField);
        Assert.Contains(issues, i => i.Field == NodeValidator.HostNameField);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public void A_port_outside_the_range_is_rejected(int port)
    {
        IReadOnlyList<ValidationIssue> issues =
            NodeValidator.ValidateServer("Box", "host", port);

        Assert.Equal(NodeValidator.PortField, Assert.Single(issues).Field);
    }

    /// <summary>Null means inherit the port, which is always allowed.</summary>
    [Fact]
    public void An_absent_port_is_not_a_problem() =>
        Assert.Empty(NodeValidator.ValidateServer("Box", "host", null));

    [Fact]
    public void Two_siblings_cannot_share_a_name()
    {
        GroupNode parent = new() { Name = "Production" };
        parent.Add(new ServerNode { Name = "WEB-01", HostName = "10.0.0.1" });

        IReadOnlyList<ValidationIssue> issues =
            NodeValidator.ValidateServer("web-01", "10.0.0.2", null, parent);

        Assert.Equal(NodeValidator.NameField, Assert.Single(issues).Field);
        Assert.Contains("Production", issues[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_same_name_in_a_different_group_is_fine()
    {
        GroupNode production = new() { Name = "Production" };
        GroupNode staging = new() { Name = "Staging" };
        production.Add(new ServerNode { Name = "WEB-01", HostName = "10.0.0.1" });

        Assert.Empty(NodeValidator.ValidateServer("WEB-01", "10.1.0.1", null, staging));
    }

    /// <summary>
    /// Editing a node without renaming it must not fail on its own name, which
    /// is the classic uniqueness-check bug.
    /// </summary>
    [Fact]
    public void A_node_does_not_clash_with_itself()
    {
        GroupNode parent = new() { Name = "Production" };
        ServerNode server = new() { Name = "WEB-01", HostName = "10.0.0.1" };
        parent.Add(server);

        Assert.Empty(NodeValidator.ValidateServer("WEB-01", "10.0.0.9", null, parent, server));
    }

    [Fact]
    public void A_group_needs_a_name_and_a_free_one()
    {
        GroupNode parent = new() { Name = "Connections" };
        parent.Add(new GroupNode { Name = "Production" });

        Assert.Empty(NodeValidator.ValidateGroup("Staging", parent));
        Assert.Single(NodeValidator.ValidateGroup("Production", parent));
        Assert.Single(NodeValidator.ValidateGroup(" ", parent));
    }
}
