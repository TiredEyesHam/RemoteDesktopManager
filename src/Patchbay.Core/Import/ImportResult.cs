using Patchbay.Core.Model;

namespace Patchbay.Core.Import;

/// <summary>
/// What came out of an import, including what did not.
///
/// The warnings are the important half. An import that silently drops
/// passwords, start programs and smart-card redirection looks like it worked
/// and leaves someone to discover the gaps one failed connection at a time.
/// </summary>
/// <param name="Root">
/// The imported tree, detached from any document. The caller decides where it
/// goes and what it is called.
/// </param>
/// <param name="Warnings">Things worth telling the person who asked.</param>
/// <param name="GroupCount">Groups imported, excluding <paramref name="Root"/>.</param>
/// <param name="ServerCount">Connections imported.</param>
public sealed record ImportResult(
    GroupNode Root,
    IReadOnlyList<string> Warnings,
    int GroupCount,
    int ServerCount)
{
    /// <summary>
    /// Whether <see cref="Root"/> is a container the importer invented rather
    /// than something the file described.
    ///
    /// A single <c>.rdp</c> holds one connection and no structure at all, so
    /// the group around it exists only because this type needs one. Saying so
    /// lets the caller put the connection straight into the tree instead of a
    /// folder holding one thing — and keeps it from doing the same to a
    /// <c>.rdg</c>, whose root group carries settings its children inherit.
    /// </summary>
    public bool RootIsWrapper { get; init; }

    /// <summary>
    /// What to put in the tree: the connection itself where the group around
    /// it is scaffolding, and the group everywhere else.
    /// </summary>
    public ConnectionNode Node =>
        RootIsWrapper && Root.Children is [ConnectionNode only] ? only : Root;

    /// <summary>A sentence summarising the import, for the status line.</summary>
    public string Summary
    {
        get
        {
            if (ServerCount == 0)
            {
                return "That file had no connections in it.";
            }

            string servers = ServerCount == 1 ? "1 connection" : $"{ServerCount} connections";

            return GroupCount switch
            {
                0 => $"Imported {servers}.",
                1 => $"Imported {servers} in 1 group.",
                _ => $"Imported {servers} in {GroupCount} groups.",
            };
        }
    }
}
