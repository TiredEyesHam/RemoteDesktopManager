using System.Globalization;
using System.Text.RegularExpressions;
using Patchbay.Core.Model;

namespace Patchbay.Core.Editing;

/// <summary>
/// Tree edits that are worth doing once, correctly, rather than in whichever
/// view model happens to need them.
/// </summary>
public static partial class NodeOperations
{
    /// <summary>
    /// Returns <paramref name="desired"/>, or the next free "name (2)" style
    /// variant, so a new or duplicated node never lands on a sibling's name.
    /// </summary>
    /// <param name="parent">Group the node will live in.</param>
    /// <param name="desired">Name to start from.</param>
    /// <param name="ignore">A node already in the group that may keep its name.</param>
    public static string UniqueName(GroupNode parent, string desired, ConnectionNode? ignore = null)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentException.ThrowIfNullOrWhiteSpace(desired);

        string trimmed = desired.Trim();

        if (IsFree(parent, trimmed, ignore))
        {
            return trimmed;
        }

        // Strip an existing suffix first, so duplicating "Web (2)" gives
        // "Web (3)" rather than "Web (2) (2)".
        Match match = SuffixPattern().Match(trimmed);
        string stem = match.Success ? match.Groups["stem"].Value : trimmed;

        for (int n = 2; n < int.MaxValue; n++)
        {
            string candidate = string.Create(CultureInfo.InvariantCulture, $"{stem} ({n})");

            if (IsFree(parent, candidate, ignore))
            {
                return candidate;
            }
        }

        return trimmed;
    }

    /// <summary>
    /// Deep copy with fresh ids throughout, detached from any parent. Fresh
    /// ids matter: the copy has to be a different connection everywhere ids
    /// are used — session tabs, credential bindings, saved views — not a
    /// second reference to the original.
    /// </summary>
    public static ConnectionNode Duplicate(ConnectionNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        switch (node)
        {
            case ServerNode server:
                ServerNode serverCopy = new()
                {
                    Name = server.Name,
                    Notes = server.Notes,
                    HostName = server.HostName,
                    Settings = server.Settings.Clone(),
                };

                foreach (string tag in server.Tags)
                {
                    serverCopy.Tags.Add(tag);
                }

                return serverCopy;

            case GroupNode group:
                GroupNode groupCopy = new()
                {
                    Name = group.Name,
                    Notes = group.Notes,
                    Settings = group.Settings.Clone(),
                };

                foreach (ConnectionNode child in group.Children)
                {
                    groupCopy.Add(Duplicate(child));
                }

                return groupCopy;

            default:
                throw new ArgumentException(
                    $"Cannot duplicate a {node.GetType().Name}.", nameof(node));
        }
    }

    /// <summary>
    /// How many servers a delete would take with it. Groups are deleted whole,
    /// and someone about to remove a group of forty machines should be told so
    /// before it happens rather than after.
    /// </summary>
    public static int CountServers(ConnectionNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node switch
        {
            ServerNode => 1,
            GroupNode group => group.DescendantServers().Count(),
            _ => 0,
        };
    }

    private static bool IsFree(GroupNode parent, string name, ConnectionNode? ignore) =>
        !parent.Children.Any(child =>
            !ReferenceEquals(child, ignore)
            && string.Equals(child.Name, name, StringComparison.OrdinalIgnoreCase));

    [GeneratedRegex(@"^(?<stem>.+?)\s*\(\d+\)$")]
    private static partial Regex SuffixPattern();
}
