using System.Text.Json.Serialization;

namespace Patchbay.Core.Model;

/// <summary>
/// A node in the connection tree: either a <see cref="GroupNode"/> or a
/// <see cref="ServerNode"/>. Both carry settings, which is the whole point —
/// a group exists to hold the settings its children inherit.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
[JsonDerivedType(typeof(GroupNode), "group")]
[JsonDerivedType(typeof(ServerNode), "server")]
public abstract class ConnectionNode
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public string? Notes { get; set; }

    public ConnectionSettings Settings { get; set; } = new();

    /// <summary>
    /// Not serialised — it would make the document a cycle. Rebuilt after load
    /// by <see cref="ConnectionDocument.RebuildParentLinks"/>, and maintained
    /// by <see cref="GroupNode.Add"/> and <see cref="GroupNode.Remove"/>.
    /// </summary>
    [JsonIgnore]
    public GroupNode? Parent { get; internal set; }

    [JsonIgnore]
    public bool IsRoot => Parent is null;

    [JsonIgnore]
    public int Depth
    {
        get
        {
            int depth = 0;
            for (GroupNode? p = Parent; p is not null; p = p.Parent)
            {
                depth++;
            }

            return depth;
        }
    }

    /// <summary>Human-readable trail, e.g. <c>Production / Web / WEB-PRD-01</c>.</summary>
    [JsonIgnore]
    public string DisplayPath => string.Join(" / ", AncestorsAndSelf().Reverse().Select(n => n.Name));

    /// <summary>This node, then each ancestor in turn, ending at the root.</summary>
    public IEnumerable<ConnectionNode> AncestorsAndSelf()
    {
        for (ConnectionNode? n = this; n is not null; n = n.Parent)
        {
            yield return n;
        }
    }

    /// <summary>Each ancestor in turn, nearest first. Excludes this node.</summary>
    public IEnumerable<GroupNode> Ancestors()
    {
        for (GroupNode? p = Parent; p is not null; p = p.Parent)
        {
            yield return p;
        }
    }

    /// <summary>
    /// True when <paramref name="candidate"/> is this node or sits above it.
    /// Guards against a drag-and-drop (M2-11) that would make a group its own
    /// ancestor and detach the subtree from the document.
    /// </summary>
    public bool IsSelfOrDescendantOf(ConnectionNode candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return AncestorsAndSelf().Any(n => ReferenceEquals(n, candidate));
    }

    public override string ToString() => $"{GetType().Name}({Name})";
}
