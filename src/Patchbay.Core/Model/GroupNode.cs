using System.Text.Json.Serialization;

namespace Patchbay.Core.Model;

/// <summary>
/// A folder in the tree. Its settings are the defaults for everything beneath
/// it, which is what makes "set the gateway once for Production" work.
/// </summary>
public sealed class GroupNode : ConnectionNode
{
    /// <summary>
    /// Get-only so callers go through <see cref="Add"/> and
    /// <see cref="Remove"/>, which keep <see cref="ConnectionNode.Parent"/>
    /// correct.
    ///
    /// The attribute is load-bearing. System.Text.Json defaults to
    /// <c>Replace</c>, which needs a setter, so a get-only collection is
    /// skipped in silence — the document writes correctly and reads back with
    /// every group empty. <c>Populate</c> makes it add into the existing list
    /// instead.
    /// </summary>
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public IList<ConnectionNode> Children { get; } = [];

    [JsonIgnore]
    public IEnumerable<GroupNode> ChildGroups => Children.OfType<GroupNode>();

    [JsonIgnore]
    public IEnumerable<ServerNode> ChildServers => Children.OfType<ServerNode>();

    /// <summary>Adds a child, detaching it from its previous parent first.</summary>
    /// <exception cref="InvalidOperationException">
    /// The child is this group or one of its ancestors, which would orphan the
    /// subtree.
    /// </exception>
    public void Add(ConnectionNode child)
    {
        ArgumentNullException.ThrowIfNull(child);

        if (IsSelfOrDescendantOf(child))
        {
            throw new InvalidOperationException(
                $"Cannot add '{child.Name}' to '{Name}': it is the same node or an ancestor of it.");
        }

        child.Parent?.Remove(child);
        child.Parent = this;
        Children.Add(child);
    }

    public void AddRange(IEnumerable<ConnectionNode> children)
    {
        ArgumentNullException.ThrowIfNull(children);

        foreach (ConnectionNode child in children.ToList())
        {
            Add(child);
        }
    }

    /// <summary>Inserts at a position. Used by drag-and-drop reordering (M2-11).</summary>
    public void Insert(int index, ConnectionNode child)
    {
        ArgumentNullException.ThrowIfNull(child);

        if (IsSelfOrDescendantOf(child))
        {
            throw new InvalidOperationException(
                $"Cannot insert '{child.Name}' into '{Name}': it is the same node or an ancestor of it.");
        }

        child.Parent?.Remove(child);
        child.Parent = this;
        Children.Insert(Math.Clamp(index, 0, Children.Count), child);
    }

    public bool Remove(ConnectionNode child)
    {
        ArgumentNullException.ThrowIfNull(child);

        if (!Children.Remove(child))
        {
            return false;
        }

        child.Parent = null;
        return true;
    }

    /// <summary>Every node beneath this one, depth first, excluding this one.</summary>
    public IEnumerable<ConnectionNode> Descendants()
    {
        foreach (ConnectionNode child in Children)
        {
            yield return child;

            if (child is GroupNode group)
            {
                foreach (ConnectionNode nested in group.Descendants())
                {
                    yield return nested;
                }
            }
        }
    }

    public IEnumerable<ServerNode> DescendantServers() => Descendants().OfType<ServerNode>();
}
