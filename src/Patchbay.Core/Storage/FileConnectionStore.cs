using System.Globalization;
using System.Text;
using Patchbay.Core.Model;
using Patchbay.Core.Serialization;

namespace Patchbay.Core.Storage;

/// <summary>
/// Stores the connection document as a single JSON file, with rotating
/// backups and a save that cannot leave the file half-written.
///
/// The care here is out of proportion to the amount of code because of what
/// the file is: someone's entire list of servers, built up over years, often
/// never backed up anywhere else. A truncated write during a Windows update
/// restart is not a hypothetical, and "your connections are gone" is not a
/// recoverable user experience.
/// </summary>
public sealed class FileConnectionStore : IConnectionStore, IDisposable
{
    /// <summary>How many previous versions to keep. Five covers a bad week.</summary>
    public const int BackupCount = 5;

    private const string TempSuffix = ".saving";

    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly IReadOnlyList<ISchemaMigration>? _migrations;

    public FileConnectionStore(string filePath, IReadOnlyList<ISchemaMigration>? migrations = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        FilePath = Path.GetFullPath(filePath);
        _migrations = migrations;
    }

    public string FilePath { get; }

    public bool Exists => File.Exists(FilePath);

    public async Task<LoadResult> LoadAsync(CancellationToken cancellationToken = default)
    {
        // A leftover temp file means a previous save was interrupted. The real
        // file is intact either way, so the temp is just noise — clear it.
        DeleteIfPresent(TempPath);

        if (!File.Exists(FilePath))
        {
            return new LoadResult { Document = new ConnectionDocument(), WasCreated = true };
        }

        ConnectionDocumentException? primaryFailure;

        try
        {
            (ConnectionDocument document, int? migratedFrom) = await ReadAsync(FilePath, cancellationToken)
                .ConfigureAwait(false);

            // Upgrading rewrites the file, so keep the original as a backup
            // before the first save overwrites it.
            if (migratedFrom is not null)
            {
                await SaveAsync(document, cancellationToken).ConfigureAwait(false);
            }

            return new LoadResult { Document = document, MigratedFromVersion = migratedFrom };
        }
        catch (ConnectionDocumentException ex)
        {
            primaryFailure = ex;
        }

        // The main file is unreadable. Work back through the backups rather
        // than presenting someone with an empty tree and no explanation.
        foreach (string backup in EnumerateBackups())
        {
            try
            {
                (ConnectionDocument document, _) = await ReadAsync(backup, cancellationToken)
                    .ConfigureAwait(false);

                PreserveDamagedFile();

                return new LoadResult { Document = document, RecoveredFromBackup = backup };
            }
            catch (ConnectionDocumentException)
            {
                // Try the next one back.
            }
            catch (IOException)
            {
                // Likewise — a locked or missing backup is not fatal here.
            }
        }

        throw new ConnectionDocumentException(
            $"'{FilePath}' could not be read, and neither could any of its backups. "
            + $"The file has been left untouched. Original error: {primaryFailure.Message}",
            primaryFailure);
    }

    public async Task SaveAsync(ConnectionDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        await _saveGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            string json = ConnectionDocumentSerializer.Serialize(document);

            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

            // Write the replacement in full, and get it onto the physical disk,
            // before touching the real file. Everything below this line is
            // either instant or reversible.
            await WriteAllTextDurablyAsync(TempPath, json, cancellationToken).ConfigureAwait(false);

            if (!File.Exists(FilePath))
            {
                File.Move(TempPath, FilePath);
                return;
            }

            RotateBackups();

            // Atomic on NTFS: the old contents become the backup and the new
            // ones become the file, or neither happens.
            File.Replace(TempPath, FilePath, BackupPath(1), ignoreMetadataErrors: true);
        }
        finally
        {
            DeleteIfPresent(TempPath);
            _saveGate.Release();
        }
    }

    /// <summary>Backup paths that currently exist, newest first.</summary>
    public IEnumerable<string> EnumerateBackups()
    {
        for (int i = 1; i <= BackupCount; i++)
        {
            string path = BackupPath(i);

            if (File.Exists(path))
            {
                yield return path;
            }
        }
    }

    public string BackupPath(int generation)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(generation, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(generation, BackupCount);

        string directory = Path.GetDirectoryName(FilePath)!;
        string name = Path.GetFileNameWithoutExtension(FilePath);

        return Path.Combine(
            directory,
            string.Create(CultureInfo.InvariantCulture, $"{name}.{generation}.bak"));
    }

    /// <inheritdoc />
    public int OlderCopies => EnumerateBackups().Count();

    /// <inheritdoc />
    public int ForgetOlderCopies()
    {
        int gone = 0;

        foreach (string backup in EnumerateBackups().ToList())
        {
            DeleteIfPresent(backup);
            gone++;
        }

        return gone;
    }

    public void Dispose() => _saveGate.Dispose();

    private string TempPath => FilePath + TempSuffix;

    private async Task<(ConnectionDocument Document, int? MigratedFrom)> ReadAsync(
        string path,
        CancellationToken cancellationToken)
    {
        string json;

        try
        {
            json = await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            throw new ConnectionDocumentException(
                $"'{path}' could not be opened: {ex.Message}", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new ConnectionDocumentException(
                $"Patchbay is not allowed to read '{path}'.", ex);
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ConnectionDocumentException($"'{path}' is empty.");
        }

        return ConnectionDocumentSerializer.DeserializeWithMigrationInfo(json, _migrations);
    }

    /// <summary>
    /// Shifts every backup one generation older and drops the oldest, leaving
    /// generation 1 free for <see cref="File.Replace(string, string, string)"/>.
    /// </summary>
    private void RotateBackups()
    {
        DeleteIfPresent(BackupPath(BackupCount));

        for (int i = BackupCount - 1; i >= 1; i--)
        {
            string from = BackupPath(i);

            if (File.Exists(from))
            {
                File.Move(from, BackupPath(i + 1), overwrite: true);
            }
        }
    }

    /// <summary>
    /// Moves an unreadable document aside instead of letting the next save
    /// overwrite it. Whatever is wrong with it, it is still the only copy of
    /// whatever the backups are missing.
    /// </summary>
    private void PreserveDamagedFile()
    {
        string damaged = FilePath + ".damaged";

        try
        {
            File.Move(FilePath, damaged, overwrite: true);
        }
        catch (IOException)
        {
            // Best effort. Failing to set this aside must not stop the load
            // that just succeeded from a backup.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static async Task WriteAllTextDurablyAsync(
        string path,
        string contents,
        CancellationToken cancellationToken)
    {
        FileStream stream = new(
            path,
            new FileStreamOptions
            {
                Mode = FileMode.Create,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.Asynchronous,
            });

        await using (stream.ConfigureAwait(false))
        {
            await using StreamWriter writer = new(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            await writer.WriteAsync(contents.AsMemory(), cancellationToken).ConfigureAwait(false);
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);

            // Push it past the OS cache. Without this the atomic replace below
            // can still leave a zero-length file after a power cut, which is
            // the exact failure this whole class exists to prevent.
            stream.Flush(flushToDisk: true);
        }
    }

    private static void DeleteIfPresent(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
