using Patchbay.Core.Editing;
using Patchbay.Core.Model;

namespace Patchbay.Tests;

public class NodeOperationsTests
{
    private static GroupNode ParentWith(params string[] names)
    {
        GroupNode parent = new() { Name = "Production" };

        foreach (string name in names)
        {
            parent.Add(new ServerNode { Name = name, HostName = "host" });
        }

        return parent;
    }

    [Fact]
    public void A_free_name_is_returned_unchanged() =>
        Assert.Equal("WEB-02", NodeOperations.UniqueName(ParentWith("WEB-01"), "WEB-02"));

    [Fact]
    public void A_taken_name_gets_the_first_free_number() =>
        Assert.Equal("Web (3)", NodeOperations.UniqueName(ParentWith("Web", "Web (2)"), "Web"));

    /// <summary>Duplicating a duplicate must not stack suffixes.</summary>
    [Fact]
    public void An_existing_suffix_is_replaced_not_appended() =>
        Assert.Equal("Web (3)", NodeOperations.UniqueName(ParentWith("Web", "Web (2)"), "Web (2)"));

    [Fact]
    public void Clashes_ignore_case() =>
        Assert.Equal("web (2)", NodeOperations.UniqueName(ParentWith("WEB"), "web"));

    [Fact]
    public void A_node_keeping_its_own_name_is_not_a_clash()
    {
        GroupNode parent = ParentWith("Web");

        Assert.Equal("Web", NodeOperations.UniqueName(parent, "Web", parent.Children[0]));
    }

    [Fact]
    public void Duplicating_a_server_copies_everything_but_the_id()
    {
        ServerNode original = new() { Name = "WEB-01", HostName = "10.0.0.1", Notes = "front end" };
        original.Settings.Port = 3390;
        original.Tags.Add("iis");

        ServerNode copy = Assert.IsType<ServerNode>(NodeOperations.Duplicate(original));

        Assert.NotEqual(original.Id, copy.Id);
        Assert.Equal("WEB-01", copy.Name);
        Assert.Equal("10.0.0.1", copy.HostName);
        Assert.Equal("front end", copy.Notes);
        Assert.Equal(3390, copy.Settings.Port);
        Assert.Equal(["iis"], copy.Tags);
        Assert.Null(copy.Parent);
    }

    /// <summary>
    /// A shallow copy would leave both nodes sharing one settings object, so
    /// editing the copy would silently change the original.
    /// </summary>
    [Fact]
    public void A_copy_does_not_share_its_settings_with_the_original()
    {
        ServerNode original = new() { Name = "WEB-01", HostName = "10.0.0.1" };
        original.Settings.Port = 3389;

        ServerNode copy = Assert.IsType<ServerNode>(NodeOperations.Duplicate(original));
        copy.Settings.Port = 4000;

        Assert.Equal(3389, original.Settings.Port);
        Assert.NotSame(original.Tags, copy.Tags);
    }

    [Fact]
    public void Duplicating_a_group_copies_the_whole_subtree_with_new_ids()
    {
        GroupNode group = new() { Name = "Production" };
        GroupNode nested = new() { Name = "Web" };
        nested.Add(new ServerNode { Name = "WEB-01", HostName = "10.0.0.1" });
        group.Add(nested);
        group.Add(new ServerNode { Name = "SQL-01", HostName = "10.0.0.2" });

        GroupNode copy = Assert.IsType<GroupNode>(NodeOperations.Duplicate(group));

        Assert.Equal(3, copy.Descendants().Count());
        Assert.Equal("WEB-01", copy.DescendantServers().First().Name);

        HashSet<Guid> originalIds = [.. group.Descendants().Select(n => n.Id)];
        Assert.DoesNotContain(copy.Descendants(), n => originalIds.Contains(n.Id));

        // Parent links must be correct throughout, or the copy resolves its
        // inheritance against nothing.
        Assert.Same(copy, copy.Children[0].Parent);
        Assert.Same(copy.Children[0], copy.DescendantServers().First().Parent);
    }

    [Fact]
    public void Counting_servers_covers_the_delete_warning()
    {
        GroupNode group = new() { Name = "Production" };
        GroupNode nested = new() { Name = "Web" };
        nested.Add(new ServerNode { Name = "WEB-01", HostName = "h" });
        nested.Add(new ServerNode { Name = "WEB-02", HostName = "h" });
        group.Add(nested);

        Assert.Equal(2, NodeOperations.CountServers(group));
        Assert.Equal(1, NodeOperations.CountServers(nested.Children[0]));
        Assert.Equal(0, NodeOperations.CountServers(new GroupNode { Name = "Empty" }));
    }
}
