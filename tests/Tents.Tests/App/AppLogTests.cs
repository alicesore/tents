using Tents.App;

namespace Tents.Tests.App;

public sealed class AppLogTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "tents-log-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public async Task Write_AppendsLines_FlushedByDispose()
    {
        var path = Path.Combine(_folder, "tents.log");
        var log = new AppLog(path);

        log.Write("one");
        log.Write("two");
        await log.DisposeAsync();

        var lines = await File.ReadAllLinesAsync(path);
        Assert.Equal(2, lines.Length);
        Assert.EndsWith(" one", lines[0], StringComparison.Ordinal);
        Assert.EndsWith(" two", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnOversizedLog_IsMovedAsideAtStart()
    {
        Directory.CreateDirectory(_folder);
        var path = Path.Combine(_folder, "tents.log");
        await File.WriteAllBytesAsync(path, new byte[AppLog.MaxBytes + 1]);

        var log = new AppLog(path);
        log.Write("fresh");
        await log.DisposeAsync();

        Assert.True(File.Exists(path + ".old"));
        Assert.Single(await File.ReadAllLinesAsync(path));
    }

    [Fact]
    public async Task Write_AfterDispose_IsDropped()
    {
        var log = new AppLog(Path.Combine(_folder, "tents.log"));
        await log.DisposeAsync();

        log.Write("late"); // must not throw
    }
}
