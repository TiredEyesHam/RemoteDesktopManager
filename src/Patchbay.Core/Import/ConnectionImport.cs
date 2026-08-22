using Patchbay.Core.Model;

namespace Patchbay.Core.Import;

/// <summary>
/// Picks the reader for a file, and puts several files' worth of imports
/// together (M1-14).
///
/// <para>
/// The two formats are not alike in shape. A <c>.rdg</c> is a whole tree with
/// its own groups and its own root, and a <c>.rdp</c> is one connection with
/// no structure at all — which is why importing a folder of them is ordinary
/// and importing a folder of <c>.rdg</c> files is not. Both end up in one
/// group so that what arrived can be looked at before it is mixed in with what
/// was already there.
/// </para>
///
/// <para>
/// The extension decides, and an extension this does not know is refused
/// rather than guessed at. Sniffing the contents of a file that arrived from
/// somewhere else, to decide which parser to hand it to, is a way of letting
/// the file choose — and the whole point of having two readers is that each
/// one knows what it is looking at.
/// </para>
/// </summary>
public static class ConnectionImport
{
    /// <summary>What can be imported, lower case and with the dot.</summary>
    public static IReadOnlyList<string> Extensions { get; } = [".rdg", ".rdp"];

    /// <summary>Whether a path is one of the formats Patchbay reads.</summary>
    public static bool CanImport(string? path) =>
        path is not null
        && Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>Imports one file, chosen by its extension.</summary>
    /// <exception cref="ImportException">The file cannot be read, or is not a format Patchbay knows.</exception>
    public static ImportResult From(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return From([path]);
    }

    /// <summary>
    /// Imports a selection, which may mix the two formats.
    /// </summary>
    /// <exception cref="ImportException">Nothing in the selection could be read.</exception>
    public static ImportResult From(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        if (paths.Count == 0)
        {
            throw new ImportException("No files were chosen, so there was nothing to import.");
        }

        List<string> warnings = [];
        List<ImportResult> results = [];
        ImportException? first = null;

        // Every .rdp at once, so that twenty files asking for the same
        // redirection produce one sentence about it rather than twenty.
        List<string> rdp = [.. paths.Where(IsRdp)];

        foreach (string path in paths.Where(p => !IsRdp(p)))
        {
            if (!CanImport(path))
            {
                ImportException unknown = new(
                    $"Patchbay imports .rdg and .rdp files, and '{Path.GetFileName(path)}' is "
                    + "neither.");

                first ??= unknown;
                warnings.Add(unknown.Message);
                continue;
            }

            try
            {
                results.Add(RdgImporter.ImportFile(path));
            }
            catch (ImportException ex)
            {
                first ??= ex;
                warnings.Add($"{Path.GetFileName(path)} was not imported. {ex.Message}");
            }
        }

        if (rdp.Count > 0)
        {
            try
            {
                results.Add(RdpImporter.ImportFiles(rdp));
            }
            catch (ImportException ex)
            {
                first ??= ex;
                warnings.Add(ex.Message);
            }
        }

        if (results.Count == 0)
        {
            throw first ?? new ImportException("Nothing in that selection could be imported.");
        }

        if (results is [ImportResult only] && warnings.Count == 0)
        {
            return only;
        }

        return Combine(results, warnings);
    }

    private static bool IsRdp(string path) =>
        string.Equals(Path.GetExtension(path), ".rdp", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Puts several imports into one group. A <c>.rdg</c> keeps its own root
    /// as a group inside it, because that root carries the settings everything
    /// below it inherits; a <c>.rdp</c> has no root of its own worth keeping,
    /// so its connections go in directly.
    /// </summary>
    private static ImportResult Combine(IReadOnlyList<ImportResult> results, List<string> warnings)
    {
        GroupNode combined = new() { Name = RdpImporter.GroupName };
        int groups = 0;
        int servers = 0;

        foreach (ImportResult result in results)
        {
            if (result.RootIsWrapper)
            {
                combined.AddRange(result.Root.Children);
            }
            else
            {
                combined.Add(result.Root);
                groups++;
            }

            groups += result.GroupCount;
            servers += result.ServerCount;
            warnings.AddRange(result.Warnings);
        }

        // The same sentence arrives once per file it was true of, and reading
        // it twice tells nobody anything the first one did not.
        return new ImportResult(
            combined,
            [.. warnings.Distinct(StringComparer.Ordinal)],
            groups,
            servers);
    }
}
