namespace Tents.Core.Persistence;

/// <summary>
/// Reads and writes the layout file (by default <c>%APPDATA%\Tents\layout.json</c>, PROJECT_PLAN.md §6) with atomic
/// replacement and a backup copy. Synchronous: call it from a background thread, never from the tent UI thread.
/// Not thread-safe: use one instance, and let <see cref="DebouncedSaver{T}"/> serialize the saves.
/// </summary>
/// <remarks>
/// <para>Files, next to <see cref="FilePath"/>:</para>
/// <list type="table">
/// <item><term><c>layout.json</c></term><description>the current layout.</description></item>
/// <item><term><c>layout.json.bak</c></term><description>the previous version, kept by every save.</description></item>
/// <item><term><c>layout.json.tmp</c></term><description>a save in progress. Never read: a crash can leave it
/// partly written, and the next save overwrites it.</description></item>
/// <item><term><c>layout.json.corrupt</c></term><description>the last main file that could not be read.</description></item>
/// <item><term><c>layout.json.v{N}</c></term><description>a main file from a newer Tents with schema version N,
/// moved aside untouched so a downgrade does not destroy it.</description></item>
/// </list>
/// <para>Save writes the temp file, flushes it to disk, then swaps it in with <see cref="File.Replace(string, string, string?, bool)"/>
/// (Win32 <c>ReplaceFileW</c>), which also turns the old file into the <c>.bak</c>. If there is no main file yet, the
/// temp file is moved into place. At every point either the old or the new file is complete under
/// <c>layout.json</c> or <c>layout.json.bak</c>, and <see cref="Load"/> falls back to the backup.</para>
/// </remarks>
public sealed class LayoutFileStore
{
    private readonly LayoutSerializer _serializer;

    /// <param name="filePath">Full path of the main layout file.</param>
    /// <param name="serializer">Defaults to <see cref="LayoutSerializer.Default"/>.</param>
    public LayoutFileStore(string filePath, LayoutSerializer? serializer = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        FilePath = Path.GetFullPath(filePath);
        _serializer = serializer ?? LayoutSerializer.Default;
    }

    /// <summary><c>%APPDATA%\Tents\layout.json</c> for the current user.</summary>
    public static string DefaultFilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Tents", "layout.json");

    public string FilePath { get; }

    public string BackupPath => FilePath + ".bak";

    public string TempPath => FilePath + ".tmp";

    public string CorruptPath => FilePath + ".corrupt";

    /// <summary>Path a main file with a newer schema version is moved to.</summary>
    public string NewerVersionPath(int schemaVersion) => FormattableString.Invariant($"{FilePath}.v{schemaVersion}");

    /// <summary>
    /// Loads the layouts. A missing, corrupt or too-new main file never throws: the result says what happened and
    /// falls back to the backup, or to <see cref="LayoutSet.Empty"/>. A corrupt or too-new main file is moved aside
    /// first, so the next save cannot turn it into the backup and overwrite the good one.
    /// </summary>
    /// <exception cref="IOException">The file exists but could not be read (for example it is locked). The caller
    /// should not save over it.</exception>
    /// <exception cref="UnauthorizedAccessException">Access to the file was denied.</exception>
    public LayoutLoadResult Load()
    {
        var main = TryRead(FilePath);
        switch (main.Outcome)
        {
            case ReadOutcome.Ok:
                return new LayoutLoadResult(main.Layouts!, LayoutFileStatus.Loaded, LayoutSource.MainFile);

            case ReadOutcome.Missing:
                return FromBackup(LayoutFileStatus.NotFound, preservedPath: null, detail: null);

            case ReadOutcome.Corrupt:
                File.Move(FilePath, CorruptPath, overwrite: true);
                return FromBackup(LayoutFileStatus.Corrupt, CorruptPath, main.Detail);

            default:
                var preserved = NewerVersionPath(main.Version);
                File.Move(FilePath, preserved, overwrite: true);
                return FromBackup(LayoutFileStatus.NewerSchemaVersion, preserved, main.Detail);
        }
    }

    /// <summary>Saves the layouts atomically and keeps the previous file as <see cref="BackupPath"/>.</summary>
    /// <exception cref="IOException">Writing or replacing failed. The previous main file and backup are unchanged,
    /// except that a failure inside the final replace step can leave the previous file only as the backup.</exception>
    public void Save(LayoutSet layouts)
    {
        ArgumentNullException.ThrowIfNull(layouts);
        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using (var stream = new FileStream(TempPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 16 * 1024))
        {
            LayoutSerializer.Serialize(stream, layouts);

            // Make sure the new content is on disk before it replaces the old file, so a power loss cannot leave a
            // replaced but empty file.
            stream.Flush(flushToDisk: true);
        }

        if (File.Exists(FilePath))
        {
            File.Replace(TempPath, FilePath, BackupPath, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(TempPath, FilePath, overwrite: true);
        }
    }

    private LayoutLoadResult FromBackup(LayoutFileStatus status, string? preservedPath, string? detail)
    {
        var backup = TryRead(BackupPath);
        return backup.Outcome == ReadOutcome.Ok
            ? new LayoutLoadResult(backup.Layouts!, status, LayoutSource.Backup, preservedPath, detail)
            : new LayoutLoadResult(LayoutSet.Empty, status, LayoutSource.None, preservedPath, Combine(detail, backup));
    }

    private static string? Combine(string? detail, ReadResult backup) => backup.Outcome switch
    {
        ReadOutcome.Missing => detail,
        _ => detail is null ? "Backup: " + backup.Detail : detail + " Backup: " + backup.Detail,
    };

    private ReadResult TryRead(string path)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return new ReadResult(ReadOutcome.Missing, null, 0, null);
        }

        try
        {
            return new ReadResult(ReadOutcome.Ok, _serializer.Deserialize(bytes), LayoutSerializer.CurrentSchemaVersion, null);
        }
        catch (LayoutFormatException ex)
        {
            return new ReadResult(ReadOutcome.Corrupt, null, 0, ex.Message);
        }
        catch (UnsupportedLayoutVersionException ex)
        {
            return new ReadResult(ReadOutcome.Newer, null, ex.FileVersion, ex.Message);
        }
    }

    private enum ReadOutcome
    {
        Ok,
        Missing,
        Corrupt,
        Newer,
    }

    private readonly record struct ReadResult(ReadOutcome Outcome, LayoutSet? Layouts, int Version, string? Detail);
}
