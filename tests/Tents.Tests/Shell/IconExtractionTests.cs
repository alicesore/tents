using System.Diagnostics;
using Tents.Core;
using Tents.Shell.Desktop;
using Tents.Shell.Icons;
using Tents.Shell.Interop;
using Tents.Shell.Threading;
using Xunit.Abstractions;

namespace Tents.Tests.Shell;

/// <summary>
/// Real icons from the shell. Desktop items are only counted, never named. The other tests use generated
/// <c>.ico</c> files in a temporary folder (not on the desktop), deleted afterwards.
/// </summary>
[Trait(TestCategories.Key, TestCategories.Desktop)]
public sealed class IconExtractionTests(ITestOutputHelper output) : IDisposable
{
    private readonly StaWorker _worker = new("test icons");
    private readonly string _folder = Directory.CreateTempSubdirectory("tents-icon-test-").FullName;

    public void Dispose()
    {
        _worker.Dispose();
        Directory.Delete(_folder, recursive: true);
    }

    [Theory]
    [InlineData(48)]
    [InlineData(96)]
    public async Task DesktopItems_HaveIconsAtTheExactSize(int size)
    {
        using var source = new DesktopItemSource();
        var snapshot = await source.ReadAsync();
        using var cache = new IconCache();

        var icons = await Task.WhenAll(snapshot.Items.Select(item => cache.GetAsync(item, size)));

        output.WriteLine($"{snapshot.Count} desktop items at {size} px: {icons.Count(i => i is not null)} icons, {cache.GetStatistics().DistinctBitmaps} distinct");
        Assert.All(icons, icon =>
        {
            Assert.NotNull(icon);
            Assert.True(icon.IsFrozen);
            Assert.Equal(size, Math.Max(icon.PixelWidth, icon.PixelHeight));
        });
    }

    [Theory]
    [InlineData(IconKind.IconOnly)]
    [InlineData(IconKind.IconOrThumbnail)]
    public async Task FolderIcon_KeepsItsTransparency(IconKind kind)
    {
        // A folder icon has transparent corners. If GetDIBits or the conversion lost alpha, every pixel would be opaque.
        var image = await _worker.InvokeAsync(() => ShellIconExtractor.Extract(Environment.SystemDirectory, 48, kind));

        Assert.NotNull(image);
        Assert.Equal(48, image.Width);
        Assert.Equal(48, image.Height);
        Assert.Equal(AlphaKind.Premultiplied, AlphaConversion.Classify(image.Pixels));
        var alphas = image.Pixels.Where((_, i) => i % 4 == 3).ToArray();
        output.WriteLine($"source alpha: {image.SourceAlpha}; transparent pixels: {alphas.Count(a => a == 0)}, partial: {alphas.Count(a => a is > 0 and < 255)}, opaque: {alphas.Count(a => a == 255)}");
        Assert.Contains(alphas, a => a == 0);
        Assert.Contains(alphas, a => a == 255);
    }

    [Theory]
    [InlineData(48)]
    public async Task GeneratedIcon_ComesBackAsItsPremultipliedPixels(int size)
    {
        // End to end with known pixels: straight-alpha icon file -> shell -> GetDIBits -> conversion.
        // 48 px only: asked for 96 px, the shell does not return the file's own 96 px image (observed on 26H2: a
        // smaller, framed rendering), so there is no exact reference at that size.
        var path = Path.Combine(_folder, "known.ico");
        SyntheticIcons.WriteIcoFile(path, seed: 7, 48, 96);
        var expected = SyntheticIcons.Render(size, seed: 7);
        AlphaConversion.ToPremultiplied(expected);

        var image = await _worker.InvokeAsync(() => ShellIconExtractor.Extract(path, size, IconKind.IconOnly));

        Assert.NotNull(image);
        Assert.Equal((size, size), (image.Width, image.Height));
        var worst = expected.Zip(image.Pixels, (e, a) => Math.Abs(e - a)).Max();
        output.WriteLine($"{size} px: shell returned {image.SourceAlpha} alpha; largest channel difference {worst}");
        Assert.True(worst <= 1, $"largest channel difference {worst}");
    }

    [Theory]
    [InlineData(48)]
    [InlineData(96)]
    public async Task Memory_For200DistinctRealIcons(int size)
    {
        const int count = 200;
        var paths = Enumerable.Range(0, count).Select(i => Path.Combine(_folder, $"icon-{i:D3}.ico")).ToList();
        for (var i = 0; i < count; i++)
        {
            SyntheticIcons.WriteIcoFile(paths[i], seed: 1000 + i, 48, 96);
        }

        var extract = Stopwatch.StartNew();
        var images = await _worker.InvokeAsync(() => paths.ToDictionary(p => new ItemId(p), p => ShellIconExtractor.Extract(p, size, IconKind.IconOnly)!));
        extract.Stop();
        Assert.All(images.Values, image => Assert.Equal((size, size), (image?.Width, image?.Height)));

        using var cache = new IconCache(new PreloadedSource(images));
        var items = images.Keys.Select(id => new DesktopItem(id, id.Value, true, false, ShellIdList.Empty)).ToList();

        // Pay one-time costs (WPF imaging, the cache thread and its dispatcher) before measuring, then start empty.
        await Task.WhenAll(items.Take(10).Select(item => cache.GetAsync(item, size)));
        cache.Clear();

        var before = Measure();
        var bitmaps = await Task.WhenAll(items.Select(item => cache.GetAsync(item, size)));
        var after = Measure();
        GC.KeepAlive(bitmaps);

        var stats = cache.GetStatistics();
        Assert.Equal(count, stats.DistinctBitmaps);
        output.WriteLine(
            $"{count} distinct icons at {size} px: pixel data {stats.PixelBytes / 1024.0:F0} KiB; " +
            $"private bytes +{(after.PrivateBytes - before.PrivateBytes) / 1024.0:F0} KiB; " +
            $"managed heap +{(after.ManagedBytes - before.ManagedBytes) / 1024.0:F0} KiB; " +
            $"shell extraction {extract.ElapsedMilliseconds} ms");
    }

    private static (long PrivateBytes, long ManagedBytes) Measure()
    {
        for (var i = 0; i < 3; i++)
        {
            GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
        }

        using var process = Process.GetCurrentProcess();
        return (process.PrivateMemorySize64, GC.GetTotalMemory(forceFullCollection: true));
    }

    private sealed class PreloadedSource(Dictionary<ItemId, IconImage> images) : IIconSource
    {
        public IconImage? Extract(DesktopItem item, int pixelSize) => images[item.Id];

        public void Dispose()
        {
        }
    }
}
