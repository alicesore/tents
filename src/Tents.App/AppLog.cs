using System.Globalization;
using System.Text;
using System.Threading.Channels;

namespace Tents.App;

/// <summary>
/// A small diagnostic log (<c>%LOCALAPPDATA%\Tents\tents.log</c>) for the manual checks: what happened and when, never the
/// names of desktop items (counts only).
/// </summary>
/// <remarks>
/// <see cref="Write"/> never blocks and never touches the disk on the caller's thread: lines go through a channel to one
/// writer that awaits the next line. An awaiting reader holds no thread and arms no timer, so the log costs nothing while
/// idle. The file is capped: at start, a file over <see cref="MaxBytes"/> becomes <c>tents.log.old</c>.
/// </remarks>
internal sealed class AppLog : IAsyncDisposable
{
    public const long MaxBytes = 512 * 1024;

    private readonly Channel<string> _lines = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
    private readonly TimeProvider _time;
    private readonly Task _writer;

    public AppLog(string? filePath = null, TimeProvider? timeProvider = null)
    {
        FilePath = filePath ?? DefaultFilePath;
        _time = timeProvider ?? TimeProvider.System;
        _writer = Task.Run(WriteLinesAsync);
    }

    public static string DefaultFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Tents",
        "tents.log");

    public string FilePath { get; }

    /// <summary>Queues one line. Never blocks; lines written after <see cref="DisposeAsync"/> are dropped.</summary>
    public void Write(string message)
    {
        var line = string.Create(CultureInfo.InvariantCulture, $"{_time.GetLocalNow():yyyy-MM-dd HH:mm:ss.fff} [{Environment.CurrentManagedThreadId}] {message}");
        _lines.Writer.TryWrite(line);
    }

    /// <summary>Writes the queued lines and closes the file.</summary>
    public async ValueTask DisposeAsync()
    {
        _lines.Writer.TryComplete();
        await _writer.ConfigureAwait(false);
    }

    private async Task WriteLinesAsync()
    {
        StreamWriter? writer = null;
        try
        {
            writer = Open();
            while (await _lines.Reader.WaitToReadAsync().ConfigureAwait(false))
            {
                while (_lines.Reader.TryRead(out var line))
                {
                    if (writer is not null)
                    {
                        await writer.WriteLineAsync(line).ConfigureAwait(false);
                    }
                }

                if (writer is not null)
                {
                    await writer.FlushAsync().ConfigureAwait(false);
                }
            }
        }
        catch (IOException)
        {
            // A log that cannot be written is not worth failing the app for.
        }
        catch (UnauthorizedAccessException)
        {
        }
        finally
        {
            if (writer is not null)
            {
                await writer.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private StreamWriter? Open()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var info = new FileInfo(FilePath);
            if (info.Exists && info.Length > MaxBytes)
            {
                File.Move(FilePath, FilePath + ".old", overwrite: true);
            }

            var stream = new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            return new StreamWriter(stream, new UTF8Encoding(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
