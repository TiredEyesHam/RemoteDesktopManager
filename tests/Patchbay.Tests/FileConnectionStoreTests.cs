using Patchbay.Core.Model;
using Patchbay.Core.Serialization;
using Patchbay.Core.Storage;

namespace Patchbay.Tests;

/// <summary>
/// Exercises the store against a real temporary directory. These touch the
/// disk on purpose — the whole point of the class is what happens to actual
/// files, and a mocked filesystem would test the mock.
/// </summary>
public sealed class FileConnectionStoreTests : IDisposable
{
    private readonly string _directory;
    private readonly string _path;

    public FileConnectionStoreTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "patchbay-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _path = Path.Combine(_directory, "connections.json");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private FileConnectionStore CreateStore(IReadOnlyList<ISchemaMigration>? migrations = null) =>
        new(_path, migrations);

    private static ConnectionDocument BuildDocument(string serverName = "WEB-PRD-01")
    {
        ConnectionDocument doc = new();
        GroupNode prod = new() { Name = "Production" };
        prod.Settings.GatewayHostName = "rdg.corp.local";
        prod.Add(new ServerNode { Name = serverName, HostName = "10.20.4.11" });
        doc.Root.Add(prod);
        return doc;
    }

    [Fact]
    public async Task Loading_when_nothing_exists_creates_an_empty_document()
    {
        using FileConnectionStore store = CreateStore();

        LoadResult result = await store.LoadAsync(CancellationToken.None);

        Assert.True(result.WasCreated);
        Assert.False(result.IsClean);
        Assert.Empty(result.Document.Root.Children);
        Assert.False(store.Exists);
    }

    [Fact]
    public async Task Save_then_load_returns_the_same_tree()
    {
        using FileConnectionStore store = CreateStore();

        await store.SaveAsync(BuildDocument(), CancellationToken.None);
        LoadResult result = await store.LoadAsync(CancellationToken.None);

        Assert.True(result.IsClean);
        Assert.Null(result.Notice);
        Assert.Equal("WEB-PRD-01", result.Document.AllServers.Single().Name);
        Assert.Equal(
            "rdg.corp.local",
            result.Document.AllGroups.Single(g => g.Name == "Production").Settings.GatewayHostName);
    }

    [Fact]
    public async Task The_first_save_leaves_no_backup_and_no_temp_file()
    {
        using FileConnectionStore store = CreateStore();

        await store.SaveAsync(BuildDocument(), CancellationToken.None);

        Assert.True(File.Exists(_path));
        Assert.Empty(store.EnumerateBackups());
        Assert.Empty(Directory.GetFiles(_directory, "*.saving"));
    }

    [Fact]
    public async Task Each_save_pushes_the_previous_version_into_a_backup()
    {
        using FileConnectionStore store = CreateStore();

        await store.SaveAsync(BuildDocument("FIRST"), CancellationToken.None);
        await store.SaveAsync(BuildDocument("SECOND"), CancellationToken.None);

        Assert.Single(store.EnumerateBackups());

        string backup = File.ReadAllText(store.BackupPath(1));
        Assert.Contains("FIRST", backup, StringComparison.Ordinal);

        LoadResult current = await store.LoadAsync(CancellationToken.None);
        Assert.Equal("SECOND", current.Document.AllServers.Single().Name);
    }

    [Fact]
    public async Task Backups_rotate_oldest_first_and_stop_at_the_limit()
    {
        using FileConnectionStore store = CreateStore();

        // Eight saves against a five-deep history: generations 1..5 should hold
        // saves 7 down to 3, and the first two should be gone.
        for (int i = 1; i <= 8; i++)
        {
            await store.SaveAsync(
                BuildDocument($"SAVE-{i}"),
                CancellationToken.None);
        }

        Assert.Equal(FileConnectionStore.BackupCount, store.EnumerateBackups().Count());

        for (int generation = 1; generation <= FileConnectionStore.BackupCount; generation++)
        {
            string expected = $"SAVE-{8 - generation}";
            string contents = File.ReadAllText(store.BackupPath(generation));

            Assert.Contains(expected, contents, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task A_corrupt_document_is_recovered_from_the_newest_backup()
    {
        using FileConnectionStore store = CreateStore();

        await store.SaveAsync(BuildDocument("GOOD"), CancellationToken.None);
        await store.SaveAsync(BuildDocument("ALSO-GOOD"), CancellationToken.None);

        await File.WriteAllTextAsync(_path, "{ this is not json", CancellationToken.None);

        LoadResult result = await store.LoadAsync(CancellationToken.None);

        Assert.NotNull(result.RecoveredFromBackup);
        Assert.Equal("GOOD", result.Document.AllServers.Single().Name);
        Assert.Contains("backup", result.Notice!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Recovery_walks_back_past_backups_that_are_also_broken()
    {
        using FileConnectionStore store = CreateStore();

        await store.SaveAsync(BuildDocument("OLDEST"), CancellationToken.None);
        await store.SaveAsync(BuildDocument("MIDDLE"), CancellationToken.None);
        await store.SaveAsync(BuildDocument("NEWEST"), CancellationToken.None);

        await File.WriteAllTextAsync(_path, "garbage", CancellationToken.None);
        await File.WriteAllTextAsync(store.BackupPath(1), "also garbage", CancellationToken.None);

        LoadResult result = await store.LoadAsync(CancellationToken.None);

        Assert.Equal("OLDEST", result.Document.AllServers.Single().Name);
        Assert.Equal(store.BackupPath(2), result.RecoveredFromBackup);
    }

    /// <summary>
    /// Whatever is wrong with the damaged file, it is still the only copy of
    /// anything the backups are missing. Overwriting it on the next save would
    /// destroy the one chance of getting that back by hand.
    /// </summary>
    [Fact]
    public async Task A_damaged_document_is_set_aside_rather_than_overwritten()
    {
        using FileConnectionStore store = CreateStore();

        // Two saves, so a backup exists to recover from. The first save has
        // nothing to preserve and correctly leaves no backup behind.
        await store.SaveAsync(BuildDocument("GOOD"), CancellationToken.None);
        await store.SaveAsync(BuildDocument("NEWER"), CancellationToken.None);
        await File.WriteAllTextAsync(_path, "{ broken", CancellationToken.None);

        await store.LoadAsync(CancellationToken.None);

        string damaged = _path + ".damaged";
        Assert.True(File.Exists(damaged));
        Assert.Equal("{ broken", await File.ReadAllTextAsync(damaged, CancellationToken.None));
    }

    [Fact]
    public async Task An_empty_file_counts_as_corrupt()
    {
        using FileConnectionStore store = CreateStore();

        await store.SaveAsync(BuildDocument("GOOD"), CancellationToken.None);
        await store.SaveAsync(BuildDocument("NEWER"), CancellationToken.None);

        // A zero-length file is the classic result of an interrupted write on
        // a store without durable saves. It parses as nothing, not as garbage,
        // so it has to be caught explicitly or it looks like an empty tree.
        await File.WriteAllTextAsync(_path, string.Empty, CancellationToken.None);

        LoadResult result = await store.LoadAsync(CancellationToken.None);

        Assert.NotNull(result.RecoveredFromBackup);
        Assert.Equal("GOOD", result.Document.AllServers.Single().Name);
    }

    /// <summary>
    /// The genuinely unrecoverable case: a damaged file on the very first run,
    /// with no backup yet in existence. Nothing can be salvaged, so the only
    /// correct behaviour is to stop rather than present an empty tree that the
    /// next save would make permanent.
    /// </summary>
    [Fact]
    public async Task A_damaged_document_with_no_history_at_all_stops_the_load()
    {
        using FileConnectionStore store = CreateStore();

        await File.WriteAllTextAsync(_path, "{ broken", CancellationToken.None);

        await Assert.ThrowsAsync<ConnectionDocumentException>(
            () => store.LoadAsync(CancellationToken.None));

        Assert.Equal("{ broken", await File.ReadAllTextAsync(_path, CancellationToken.None));
    }

    [Fact]
    public async Task An_unreadable_document_with_no_usable_backup_fails_loudly()
    {
        using FileConnectionStore store = CreateStore();

        await File.WriteAllTextAsync(_path, "{ broken", CancellationToken.None);

        ConnectionDocumentException ex = await Assert.ThrowsAsync<ConnectionDocumentException>(
            () => store.LoadAsync(CancellationToken.None));

        Assert.Contains("neither could any of its backups", ex.Message, StringComparison.Ordinal);

        // The file must survive a failed load — it is the only copy left.
        Assert.True(File.Exists(_path));
    }

    /// <summary>
    /// A leftover temp file means a previous save was interrupted. The real
    /// document is untouched in that case, so the temp is debris; leaving it
    /// around would confuse the next crash investigation.
    /// </summary>
    [Fact]
    public async Task An_interrupted_save_leaves_the_document_intact_and_is_tidied_up()
    {
        using FileConnectionStore store = CreateStore();
        await store.SaveAsync(BuildDocument("SAFE"), CancellationToken.None);

        string temp = _path + ".saving";
        await File.WriteAllTextAsync(temp, "{ half-written", CancellationToken.None);

        LoadResult result = await store.LoadAsync(CancellationToken.None);

        Assert.True(result.IsClean);
        Assert.Equal("SAFE", result.Document.AllServers.Single().Name);
        Assert.False(File.Exists(temp));
    }

    [Fact]
    public async Task Saving_creates_missing_directories()
    {
        string nested = Path.Combine(_directory, "a", "b", "connections.json");
        using FileConnectionStore store = new(nested);

        await store.SaveAsync(BuildDocument(), CancellationToken.None);

        Assert.True(File.Exists(nested));
    }

    [Fact]
    public async Task Concurrent_saves_do_not_corrupt_the_document()
    {
        using FileConnectionStore store = CreateStore();
        await store.SaveAsync(BuildDocument("INITIAL"), CancellationToken.None);

        await Task.WhenAll(
            Enumerable.Range(0, 20).Select(i =>
                store.SaveAsync(BuildDocument($"HOST-{i}"), CancellationToken.None)));

        LoadResult result = await store.LoadAsync(CancellationToken.None);

        Assert.True(result.IsClean);
        Assert.StartsWith("HOST-", result.Document.AllServers.Single().Name, StringComparison.Ordinal);
    }

    [Fact]
    public void Backup_generation_must_be_within_range()
    {
        using FileConnectionStore store = CreateStore();

        Assert.Throws<ArgumentOutOfRangeException>(() => store.BackupPath(0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => store.BackupPath(FileConnectionStore.BackupCount + 1));
    }
}
