using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tents.Shell.IconLayer;

/// <summary>What the state file records about the Tents process that turned the desktop icons off.</summary>
/// <param name="Version">Format version, 1.</param>
/// <param name="ProcessId">The process that turned the icons off.</param>
/// <param name="ProcessStartTime">Its start time, so a reused process id is not mistaken for it.</param>
/// <param name="TurnedOffAt">When it turned them off.</param>
public sealed record IconLayerState(int Version, int ProcessId, DateTimeOffset ProcessStartTime, DateTimeOffset TurnedOffAt)
{
    public const int CurrentVersion = 1;
}

/// <summary>
/// The file that says "Tents turned the desktop icons off". It exists exactly while Tents owns the icons-off state:
/// written before the icons are switched off, deleted after they are switched back on. If Tents dies in between,
/// the next start (or the watchdog, M1 step 21) finds it and turns the icons back on.
/// </summary>
/// <remarks>
/// <para><b>Location.</b> <c>%LOCALAPPDATA%\Tents\icon-layer.json</c>: per user and per machine (not roaming, unlike the
/// layout), because it describes this machine's desktop.</para>
/// <para><b>The file's existence is the signal.</b> Its content is detail. A file that cannot be read or parsed still
/// means "turn the icons back on", so <see cref="Read"/> returns a state with process id 0 for it.</para>
/// <para><b>Writes are atomic</b> (temp file, flush, replace), so a crash never leaves half a file. Format:</para>
/// <code>{ "version": 1, "processId": 1234, "processStartTime": "2026-11-18T09:00:00+00:00", "turnedOffAt": "2026-11-18T09:00:01+00:00" }</code>
/// </remarks>
public sealed class IconLayerStateFile
{
    /// <param name="filePath">Defaults to <see cref="DefaultFilePath"/>; tests pass a temporary path.</param>
    public IconLayerStateFile(string? filePath = null)
    {
        FilePath = filePath ?? DefaultFilePath;
    }

    public static string DefaultFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Tents",
        "icon-layer.json");

    public string FilePath { get; }

    private string TempPath => FilePath + ".tmp";

    public bool Exists => File.Exists(FilePath);

    /// <summary>The recorded state, null when there is no file. An unreadable file gives a state with process id 0.</summary>
    public IconLayerState? Read()
    {
        if (!File.Exists(FilePath))
        {
            return null;
        }

        try
        {
            var state = JsonSerializer.Deserialize(File.ReadAllBytes(FilePath), IconLayerJsonContext.Default.IconLayerState);
            if (state is not null && state.ProcessId > 0)
            {
                return state;
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Fall through: the file exists, so the icons must be turned back on whatever it holds.
        }

        return new IconLayerState(0, 0, DateTimeOffset.MinValue, DateTimeOffset.MinValue);
    }

    /// <summary>Writes <paramref name="state"/> atomically. Throws when it cannot be written.</summary>
    public void Write(IconLayerState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(state, IconLayerJsonContext.Default.IconLayerState);
        using (var stream = new FileStream(TempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }

        File.Move(TempPath, FilePath, overwrite: true);
    }

    /// <summary>Deletes the file (and a leftover temp file). No error when there is none.</summary>
    public void Delete()
    {
        File.Delete(FilePath);
        File.Delete(TempPath);
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(IconLayerState))]
internal sealed partial class IconLayerJsonContext : JsonSerializerContext;
