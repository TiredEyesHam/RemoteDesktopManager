using Patchbay.Core.Model;

namespace Patchbay.Tests;

public class TreeStructureTests
{
    [Fact]
    public void Adding_a_child_sets_its_parent()
    {
        GroupNode group = new() { Name = "Production" };
        ServerNode server = new() { Name = "WEB-PRD-01" };

        group.Add(server);

        Assert.Same(group, server.Parent);
        Assert.Contains(server, group.Children);
    }

    [Fact]
    public void Adding_to_a_new_group_detaches_from_the_old_one()
    {
        GroupNode from = new() { Name = "Staging" };
        GroupNode to = new() { Name = "Production" };
        ServerNode server = new() { Name = "APP-01" };

        from.Add(server);
        to.Add(server);

        Assert.Empty(from.Children);
        Assert.Same(to, server.Parent);
        Assert.Single(to.Children);
    }

    [Fact]
    public void Removing_a_child_clears_its_parent()
    {
        GroupNode group = new() { Name = "Production" };
        ServerNode server = new() { Name = "WEB-PRD-01" };
        group.Add(server);

        Assert.True(group.Remove(server));
        Assert.Null(server.Parent);
        Assert.Empty(group.Children);
    }

    [Fact]
    public void Removing_something_that_is_not_a_child_reports_false()
    {
        GroupNode group = new() { Name = "Production" };

        Assert.False(group.Remove(new ServerNode { Name = "Elsewhere" }));
    }

    /// <summary>
    /// The drag-and-drop hazard (M2-11): dropping a group onto its own
    /// descendant would splice the subtree out of the document and lose every
    /// connection under it.
    /// </summary>
    [Fact]
    public void A_group_cannot_be_added_to_its_own_descendant()
    {
        GroupNode outer = new() { Name = "Production" };
        GroupNode inner = new() { Name = "Web" };
        outer.Add(inner);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => inner.Add(outer));

        Assert.Contains("ancestor", ex.Message, StringComparison.Ordinal);
        Assert.Same(outer, inner.Parent);
        Assert.Contains(inner, outer.Children);
    }

    [Fact]
    public void A_group_cannot_be_added_to_itself()
    {
        GroupNode group = new() { Name = "Production" };

        Assert.Throws<InvalidOperationException>(() => group.Add(group));
    }

    [Fact]
    public void Insert_places_a_node_at_the_requested_position()
    {
        GroupNode group = new() { Name = "Production" };
        ServerNode first = new() { Name = "A" };
        ServerNode third = new() { Name = "C" };
        ServerNode second = new() { Name = "B" };

        group.Add(first);
        group.Add(third);
        group.Insert(1, second);

        Assert.Equal(["A", "B", "C"], group.Children.Select(c => c.Name));
    }

    [Fact]
    public void Insert_clamps_an_out_of_range_index()
    {
        GroupNode group = new() { Name = "Production" };
        group.Add(new ServerNode { Name = "A" });

        group.Insert(99, new ServerNode { Name = "B" });
        group.Insert(-5, new ServerNode { Name = "C" });

        Assert.Equal(["C", "A", "B"], group.Children.Select(c => c.Name));
    }

    [Fact]
    public void Descendants_walks_the_whole_subtree_depth_first()
    {
        GroupNode root = new() { Name = "Connections" };
        GroupNode prod = new() { Name = "Production" };
        GroupNode web = new() { Name = "Web" };

        root.Add(prod);
        prod.Add(web);
        web.Add(new ServerNode { Name = "WEB-PRD-01" });
        prod.Add(new ServerNode { Name = "SQL-PRD-01" });

        Assert.Equal(
            ["Production", "Web", "WEB-PRD-01", "SQL-PRD-01"],
            root.Descendants().Select(n => n.Name));

        Assert.Equal(
            ["WEB-PRD-01", "SQL-PRD-01"],
            root.DescendantServers().Select(n => n.Name));
    }

    [Fact]
    public void Depth_and_display_path_follow_the_ancestry()
    {
        ConnectionDocument doc = new();
        GroupNode prod = new() { Name = "Production" };
        ServerNode server = new() { Name = "WEB-PRD-01" };

        doc.Root.Add(prod);
        prod.Add(server);

        Assert.Equal(0, doc.Root.Depth);
        Assert.Equal(1, prod.Depth);
        Assert.Equal(2, server.Depth);
        Assert.True(doc.Root.IsRoot);
        Assert.False(server.IsRoot);
        Assert.Equal("Connections / Production / WEB-PRD-01", server.DisplayPath);
    }

    [Fact]
    public void FindById_locates_any_node_including_the_root()
    {
        ConnectionDocument doc = new();
        GroupNode prod = new() { Name = "Production" };
        ServerNode server = new() { Name = "WEB-PRD-01" };

        doc.Root.Add(prod);
        prod.Add(server);

        Assert.Same(doc.Root, doc.FindById(doc.Root.Id));
        Assert.Same(prod, doc.FindById(prod.Id));
        Assert.Same(server, doc.FindById(server.Id));
        Assert.Null(doc.FindById(Guid.NewGuid()));
    }

    [Fact]
    public void RebuildParentLinks_repairs_a_tree_assembled_by_hand()
    {
        // What an importer produces: children added straight to the list, so
        // no parent links exist yet.
        ConnectionDocument doc = new();
        GroupNode prod = new() { Name = "Production" };
        ServerNode server = new() { Name = "WEB-PRD-01" };

        prod.Children.Add(server);
        doc.Root.Children.Add(prod);

        Assert.Null(server.Parent);

        doc.RebuildParentLinks();

        Assert.Same(prod, server.Parent);
        Assert.Same(doc.Root, prod.Parent);
        Assert.Null(doc.Root.Parent);
    }
}
