using Patchbay.Core.Model;

namespace Patchbay.Core.Search;

/// <summary>
/// Decides what the search box keeps on screen.
///
/// The rule that matters is the second one: a group survives if anything
/// beneath it survives. Filtering a tree by matching nodes alone hides the
/// folders the matches live in, and the results arrive as a flat list with no
/// idea where anything came from — which is the thing people are actually
/// looking for when they search a connection list.
/// </summary>
public static class NodeFilter
{
    /// <summary>
    /// Whether the node itself matches, ignoring its children. Every
    /// whitespace-separated term must be found somewhere, so "prod sql"
    /// narrows rather than widens.
    /// </summary>
    public static bool MatchesSelf(ConnectionNode node, string? query)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        string[] terms = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        return Array.TrueForAll(terms, term => MatchesTerm(node, term));
    }

    /// <summary>
    /// Whether the node should stay visible: it matches, or one of its
    /// descendants does.
    /// </summary>
    public static bool MatchesTree(ConnectionNode node, string? query)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        if (MatchesSelf(node, query))
        {
            return true;
        }

        return node is GroupNode group
            && group.Children.Any(child => MatchesTree(child, query));
    }

    private static bool MatchesTerm(ConnectionNode node, string term)
    {
        if (Contains(node.Name, term))
        {
            return true;
        }

        if (node is ServerNode server)
        {
            return Contains(server.HostName, term)
                || server.Tags.Any(tag => Contains(tag, term));
        }

        return false;
    }

    private static bool Contains(string? value, string term) =>
        value is not null && value.Contains(term, StringComparison.OrdinalIgnoreCase);
}
