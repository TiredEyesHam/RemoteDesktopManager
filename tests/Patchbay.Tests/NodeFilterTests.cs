using Patchbay.Core.Model;
using Patchbay.Core.Search;

namespace Patchbay.Tests;

public class NodeFilterTests
{
    private static GroupNode BuildTree()
    {
        GroupNode root = new() { Name = "Connections" };
        GroupNode production = new() { Name = "Production" };

        ServerNode web = new() { Name = "WEB-PRD-01", HostName = "10.20.4.11" };
        web.Tags.Add("iis");

        production.Add(web);
        production.Add(new ServerNode { Name = "SQL-PRD-01", HostName = "sql.corp.local" });
        root.Add(production);
        root.Add(new GroupNode { Name = "Lab" });

        return root;
    }

    private static ServerNode Web() =>
        BuildTree().DescendantServers().First(s => s.Name == "WEB-PRD-01");

    [Fact]
    public void An_empty_query_matches_everything()
    {
        GroupNode root = BuildTree();

        Assert.All(root.Descendants(), node => Assert.True(NodeFilter.MatchesTree(node, "")));
        Assert.True(NodeFilter.MatchesTree(root, null));
    }

    [Theory]
    [InlineData("web")]
    [InlineData("10.20")]
    [InlineData("IIS")]
    public void A_server_matches_on_name_address_or_tag(string query) =>
        Assert.True(NodeFilter.MatchesSelf(Web(), query));

    /// <summary>
    /// Every term has to land, so a second word narrows the list. The
    /// alternative, any term matching, makes typing more of the thing you want
    /// return more of the things you do not.
    /// </summary>
    [Fact]
    public void All_terms_must_match()
    {
        Assert.True(NodeFilter.MatchesSelf(Web(), "web 10.20"));
        Assert.False(NodeFilter.MatchesSelf(Web(), "web sql"));
    }

    [Fact]
    public void A_group_survives_because_a_child_matches()
    {
        GroupNode production = BuildTree().ChildGroups.First(g => g.Name == "Production");

        Assert.False(NodeFilter.MatchesSelf(production, "sql"));
        Assert.True(NodeFilter.MatchesTree(production, "sql"));
    }

    [Fact]
    public void A_group_with_nothing_matching_disappears()
    {
        GroupNode lab = BuildTree().ChildGroups.First(g => g.Name == "Lab");

        Assert.False(NodeFilter.MatchesTree(lab, "sql"));
    }

    [Fact]
    public void Searching_for_a_group_name_finds_the_group()
    {
        GroupNode production = BuildTree().ChildGroups.First(g => g.Name == "Production");

        Assert.True(NodeFilter.MatchesSelf(production, "prod"));
    }

    /// <summary>
    /// A group has settings, including a gateway host name. Those are not
    /// searchable text; matching them would return groups nobody asked for.
    /// </summary>
    [Fact]
    public void A_group_never_matches_on_a_host_name_in_its_settings()
    {
        GroupNode group = new() { Name = "Lab" };
        group.Settings.GatewayHostName = "rdg.corp.local";

        Assert.False(NodeFilter.MatchesSelf(group, "rdg"));
    }
}
