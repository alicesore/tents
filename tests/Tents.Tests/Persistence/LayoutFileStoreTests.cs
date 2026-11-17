using Tents.Core;
using Tents.Core.Persistence;
using static Tents.Tests.Persistence.PersistenceTestData;
using static Tents.Tests.TestData;

namespace Tents.Tests.Persistence;

/// <summary>Each test works in its own new folder under the system temp directory, deleted afterwards.</summary>
public sealed class LayoutFileStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "Tents.Tests", Guid.NewGuid().ToString("N"));
    private readonly LayoutFileStore _store;

    public LayoutFileStoreTests() => _store = new LayoutFileStore(Path.Combine(_folder, "layout.json"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private static LayoutSet Other() => LayoutSet.Create([LayoutWith(Tent(0, "only"))]);

    private void Write(string path, string content)
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(path, content);
    }

    // ---- Missing ----

    [Fact]
    public void Load_MissingFile_ReturnsEmptyAndCreatesNothing()
    {
        var result = _store.Load();

        Assert.Equal(LayoutFileStatus.NotFound, result.Status);
        Assert.Equal(LayoutSource.None, result.Source);
        Assert.Same(LayoutSet.Empty, result.Layouts);
        Assert.False(Directory.Exists(_folder));
    }

    [Fact]
    public void Load_MissingFileButBackup_UsesTheBackup()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllBytes(_store.BackupPath, LayoutSerializer.SerializeToUtf8Bytes(Sample()));

        var result = _store.Load();

        Assert.Equal(LayoutFileStatus.NotFound, result.Status);
        Assert.Equal(LayoutSource.Backup, result.Source);
        Assert.Equal(Sample(), result.Layouts);
    }

    // ---- Round trip ----

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        _store.Save(Sample());

        var result = _store.Load();

        Assert.Equal(LayoutFileStatus.Loaded, result.Status);
        Assert.Equal(LayoutSource.MainFile, result.Source);
        Assert.Equal(Sample(), result.Layouts);
        Assert.Null(result.Detail);
    }

    [Fact]
    public void Save_CreatesTheFolderAndLeavesNoTempFile()
    {
        _store.Save(Sample());

        Assert.True(File.Exists(_store.FilePath));
        Assert.False(File.Exists(_store.TempPath));
        Assert.False(File.Exists(_store.BackupPath));
    }

    [Fact]
    public void Save_KeepsThePreviousFileAsBackup()
    {
        _store.Save(Sample());
        var first = File.ReadAllBytes(_store.FilePath);

        _store.Save(Other());

        Assert.Equal(first, File.ReadAllBytes(_store.BackupPath));
        Assert.Equal(Other(), _store.Load().Layouts);
        Assert.False(File.Exists(_store.TempPath));
    }

    // ---- Corrupt ----

    [Fact]
    public void Load_CorruptFileWithBackup_RecoversAndMovesTheCorruptFileAside()
    {
        _store.Save(Sample());
        _store.Save(Sample());
        Write(_store.FilePath, "{ \"schemaVersion\": 1, \"layouts\": [ garbage");

        var result = _store.Load();

        Assert.Equal(LayoutFileStatus.Corrupt, result.Status);
        Assert.Equal(LayoutSource.Backup, result.Source);
        Assert.Equal(Sample(), result.Layouts);
        Assert.Equal(_store.CorruptPath, result.PreservedPath);
        Assert.NotNull(result.Detail);
        Assert.False(File.Exists(_store.FilePath));
        Assert.StartsWith("{ \"schemaVersion\": 1, \"layouts\": [ garbage", File.ReadAllText(_store.CorruptPath), StringComparison.Ordinal);
    }

    [Fact]
    public void Save_AfterCorruptRecovery_KeepsTheGoodBackup()
    {
        _store.Save(Sample());
        _store.Save(Sample());
        var goodBackup = File.ReadAllBytes(_store.BackupPath);
        Write(_store.FilePath, "corrupt");
        _store.Load();

        _store.Save(Other());

        Assert.Equal(goodBackup, File.ReadAllBytes(_store.BackupPath));
        Assert.Equal(Other(), _store.Load().Layouts);
    }

    [Fact]
    public void Load_CorruptFileWithoutBackup_ReturnsEmpty()
    {
        Write(_store.FilePath, "\0\0\0\0");

        var result = _store.Load();

        Assert.Equal(LayoutFileStatus.Corrupt, result.Status);
        Assert.Equal(LayoutSource.None, result.Source);
        Assert.Same(LayoutSet.Empty, result.Layouts);
        Assert.True(File.Exists(_store.CorruptPath));
    }

    [Fact]
    public void Load_CorruptFileAndCorruptBackup_ReturnsEmptyAndReportsBoth()
    {
        Write(_store.FilePath, "not json");
        Write(_store.BackupPath, "[]");

        var result = _store.Load();

        Assert.Equal(LayoutSource.None, result.Source);
        Assert.Contains("Backup:", result.Detail, StringComparison.Ordinal);
        Assert.True(File.Exists(_store.BackupPath), "A bad backup is left in place.");
    }

    [Fact]
    public void Load_FileThatBreaksALayoutRule_IsTreatedAsCorrupt()
    {
        Write(_store.FilePath, MinimalJson(1, items: """["X:\\TestDesktop\\a", "X:\\TestDesktop\\a"]"""));

        Assert.Equal(LayoutFileStatus.Corrupt, _store.Load().Status);
    }

    // ---- Newer schema ----

    [Fact]
    public void Load_NewerSchemaVersion_MovesTheFileAsideUntouchedAndUsesTheBackup()
    {
        _store.Save(Sample());
        _store.Save(Sample());
        var newer = """{ "schemaVersion": 2, "whatever": "a future Tents wrote this" }""";
        Write(_store.FilePath, newer);

        var result = _store.Load();

        Assert.Equal(LayoutFileStatus.NewerSchemaVersion, result.Status);
        Assert.Equal(LayoutSource.Backup, result.Source);
        Assert.Equal(Sample(), result.Layouts);
        Assert.Equal(_store.NewerVersionPath(2), result.PreservedPath);
        Assert.EndsWith("layout.json.v2", result.PreservedPath, StringComparison.Ordinal);
        Assert.Equal(newer, File.ReadAllText(_store.NewerVersionPath(2)));

        _store.Save(Other());

        Assert.Equal(newer, File.ReadAllText(_store.NewerVersionPath(2)));
    }

    [Fact]
    public void Load_NewerSchemaVersionWithNewerBackup_ReturnsEmpty()
    {
        Write(_store.FilePath, """{ "schemaVersion": 3 }""");
        Write(_store.BackupPath, """{ "schemaVersion": 3 }""");

        var result = _store.Load();

        Assert.Equal(LayoutFileStatus.NewerSchemaVersion, result.Status);
        Assert.Equal(LayoutSource.None, result.Source);
        Assert.Same(LayoutSet.Empty, result.Layouts);
    }

    // ---- Interrupted writes ----

    [Fact]
    public void Load_IgnoresAPartlyWrittenTempFile()
    {
        _store.Save(Sample());
        Write(_store.TempPath, "{ \"schemaVersion\": 1, \"layouts\": [ { \"monit");

        var result = _store.Load();

        Assert.Equal(LayoutFileStatus.Loaded, result.Status);
        Assert.Equal(Sample(), result.Layouts);
    }

    [Fact]
    public void Save_AfterAnInterruptedWrite_OverwritesTheStaleTempFile()
    {
        _store.Save(Sample());
        Write(_store.TempPath, "partial");

        _store.Save(Other());

        Assert.False(File.Exists(_store.TempPath));
        Assert.Equal(Other(), _store.Load().Layouts);
        Assert.Equal(Sample(), LayoutSerializer.Default.Deserialize(File.ReadAllBytes(_store.BackupPath)));
    }

    [Fact]
    public void Load_InterruptedReplace_FallsBackToTheBackupAndNeverReadsTheTempFile()
    {
        // The state after a replace that renamed the old file to .bak but did not move the new one into place.
        Directory.CreateDirectory(_folder);
        File.WriteAllBytes(_store.BackupPath, LayoutSerializer.SerializeToUtf8Bytes(Sample()));
        File.WriteAllBytes(_store.TempPath, LayoutSerializer.SerializeToUtf8Bytes(Other()));

        var result = _store.Load();

        Assert.Equal(LayoutFileStatus.NotFound, result.Status);
        Assert.Equal(LayoutSource.Backup, result.Source);
        Assert.Equal(Sample(), result.Layouts);
    }

    [Fact]
    public void Save_FailingWrite_LeavesTheMainFileAndBackupUnchanged()
    {
        _store.Save(Sample());
        _store.Save(Sample());
        var main = File.ReadAllBytes(_store.FilePath);
        var backup = File.ReadAllBytes(_store.BackupPath);

        // Another handle holds the temp file exclusively, so the write fails before anything is replaced.
        using (new FileStream(_store.TempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            Assert.ThrowsAny<IOException>(() => _store.Save(Other()));
        }

        Assert.Equal(main, File.ReadAllBytes(_store.FilePath));
        Assert.Equal(backup, File.ReadAllBytes(_store.BackupPath));
        Assert.Equal(Sample(), _store.Load().Layouts);
    }

    [Fact]
    public void Load_LockedFile_ThrowsInsteadOfStartingEmpty()
    {
        _store.Save(Sample());

        using var locked = new FileStream(_store.FilePath, FileMode.Open, FileAccess.Read, FileShare.None);

        Assert.ThrowsAny<IOException>(() => _store.Load());
    }
}
