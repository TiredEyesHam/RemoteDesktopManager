using Patchbay.Core.Model;

namespace Patchbay.Core.Storage;

/// <summary>
/// The outcome of a load, not just the document.
///
/// A silent fallback to a backup is the worst possible behaviour here: someone
/// carries on working against a version of their connection list that is
/// missing yesterday's changes, and the next save makes that permanent. So
/// recovery is reported, and the shell is expected to say so.
/// </summary>
public sealed record LoadResult
{
    public required ConnectionDocument Document { get; init; }

    /// <summary>
    /// Path of the backup the document was recovered from, or null when the
    /// main file read cleanly.
    /// </summary>
    public string? RecoveredFromBackup { get; init; }

    /// <summary>True when no document existed and an empty one was created.</summary>
    public bool WasCreated { get; init; }

    /// <summary>
    /// The version the document was on before migrations ran, or null when no
    /// migration was needed.
    /// </summary>
    public int? MigratedFromVersion { get; init; }

    /// <summary>Nothing unusual happened; no need to tell anyone.</summary>
    public bool IsClean => RecoveredFromBackup is null && !WasCreated && MigratedFromVersion is null;

    /// <summary>A sentence for the shell to show when the load was not clean.</summary>
    public string? Notice
    {
        get
        {
            if (RecoveredFromBackup is not null)
            {
                return $"The connection file could not be read, so Patchbay opened the most recent "
                    + $"backup instead ({Path.GetFileName(RecoveredFromBackup)}). Changes made after "
                    + "that backup are not here. The damaged file has been kept.";
            }

            if (MigratedFromVersion is not null)
            {
                return $"The connection file was upgraded from format {MigratedFromVersion} to "
                    + $"{Document.SchemaVersion}. A backup of the original was kept.";
            }

            return WasCreated ? "Started a new connection file." : null;
        }
    }
}
