using System.Collections.Concurrent;
using System.Windows.Media;
using Tents.Core;
using Tents.Shell.Desktop;
using Tents.Shell.Icons;
using Tents.Shell.Interop;

namespace Tents.Tests.Shell;

public sealed class IconCacheTests : IDisposable
{
    private readonly FakeIconSource _source = new();
    private readonly IconCache _cache;

    public IconCacheTests() => _cache = new IconCache(_source);

    public void Dispose() => _cache.Dispose();

    [Fact]
    public async Task SameItemAndSize_LoadsOnce_AndReturnsTheSameBitmap()
    {
        var item = Item("a");

        var first = _cache.GetAsync(item, 48);
        var second = _cache.GetAsync(item, 48);

        Assert.Same(await first, await second);
        Assert.Equal(1, _source.CallCount(item.Id, 48));
    }

    [Fact]
    public async Task Bitmap_IsFrozenPbgra32_AtTheImageSize()
    {
        var bitmap = await _cache.GetAsync(Item("a"), 48);

        Assert.NotNull(bitmap);
        Assert.True(bitmap.IsFrozen);
        Assert.Equal(PixelFormats.Pbgra32, bitmap.Format);
        Assert.Equal(48, bitmap.PixelWidth);
        Assert.Equal(48, bitmap.PixelHeight);
    }

    [Fact]
    public async Task Bitmap_HoldsTheConvertedPixels()
    {
        var item = Item("a");
        _source.Set(item.Id, 2, Image(2, fill: 77, alpha: 128));

        var bitmap = await _cache.GetAsync(item, 2);

        var pixels = new byte[16];
        bitmap!.CopyPixels(pixels, 8, 0);
        Assert.Equal(Image(2, fill: 77, alpha: 128).Pixels, pixels);
    }

    [Fact]
    public async Task DifferentSizes_AreSeparateEntries()
    {
        var item = Item("a");
        _source.Set(item.Id, 96, Image(96, fill: 5));

        var small = await _cache.GetAsync(item, 48);
        var large = await _cache.GetAsync(item, 96);

        Assert.Equal(48, small!.PixelWidth);
        Assert.Equal(96, large!.PixelWidth);
        Assert.Equal(2, _cache.GetStatistics().Entries);
    }

    [Fact]
    public async Task IdenticalImages_ShareOneBitmap()
    {
        var a = await _cache.GetAsync(Item("a"), 48);
        var b = await _cache.GetAsync(Item("b"), 48);

        Assert.Same(a, b);
        Assert.Equal(new IconCacheStatistics(2, 1, 48 * 48 * 4), _cache.GetStatistics());
    }

    [Fact]
    public async Task DifferentImages_AreNotShared()
    {
        _source.Set(new ItemId("b"), 48, Image(48, fill: 9));

        var a = await _cache.GetAsync(Item("a"), 48);
        var b = await _cache.GetAsync(Item("b"), 48);

        Assert.NotSame(a, b);
        Assert.Equal(2, _cache.GetStatistics().DistinctBitmaps);
    }

    [Fact]
    public async Task NoImage_IsCachedAsNull()
    {
        var item = Item("none");
        _source.Set(item.Id, 48, null);

        Assert.Null(await _cache.GetAsync(item, 48));
        Assert.Null(await _cache.GetAsync(item, 48));
        Assert.Equal(1, _source.CallCount(item.Id, 48));
    }

    [Fact]
    public async Task Invalidate_LoadsAgain_AndReleasesTheSharedBitmapWhenUnused()
    {
        var a = Item("a");
        var b = Item("b");
        await _cache.GetAsync(a, 48);
        await _cache.GetAsync(b, 48);

        _cache.Invalidate(a.Id);
        Assert.Equal(new IconCacheStatistics(1, 1, 48 * 48 * 4), _cache.GetStatistics());
        _cache.Invalidate(b.Id);
        Assert.Equal(new IconCacheStatistics(0, 0, 0), _cache.GetStatistics());

        await _cache.GetAsync(a, 48);
        Assert.Equal(2, _source.CallCount(a.Id, 48));
    }

    [Fact]
    public async Task Invalidate_CoversEverySize()
    {
        var a = Item("a");
        await _cache.GetAsync(a, 48);
        await _cache.GetAsync(a, 96);

        _cache.Invalidate(new ItemId("A"));

        Assert.Equal(0, _cache.GetStatistics().Entries);
    }

    [Fact]
    public async Task RemoveAllExcept_KeepsOnlyTheGivenItems()
    {
        _source.Set(new ItemId("b"), 48, Image(48, fill: 9));
        await _cache.GetAsync(Item("a"), 48);
        await _cache.GetAsync(Item("b"), 48);

        _cache.RemoveAllExcept(new HashSet<ItemId> { new("b") });

        Assert.False(_cache.TryGetCached(new ItemId("a"), 48, out _));
        Assert.True(_cache.TryGetCached(new ItemId("b"), 48, out var kept));
        Assert.NotNull(kept);
        Assert.Equal(new IconCacheStatistics(1, 1, 48 * 48 * 4), _cache.GetStatistics());
    }

    [Fact]
    public async Task FailedExtraction_FaultsTheRequest_AndTheNextRequestTriesAgain()
    {
        var item = Item("broken");
        _source.FailNext(item.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _cache.GetAsync(item, 48));
        Assert.NotNull(await _cache.GetAsync(item, 48));
        Assert.Equal(2, _source.CallCount(item.Id, 48));
    }

    [Fact]
    public async Task Extraction_RunsOnAnStaThread_NotTheCaller()
    {
        await _cache.GetAsync(Item("a"), 48);

        Assert.Equal(ApartmentState.STA, _source.LastApartment);
        Assert.NotEqual(Environment.CurrentManagedThreadId, _source.LastThreadId);
    }

    [Fact]
    public void TryGetCached_NeverStartsALoad()
    {
        Assert.False(_cache.TryGetCached(new ItemId("a"), 48, out _));
        Assert.Equal(0, _source.CallCount(new ItemId("a"), 48));
    }

    private static DesktopItem Item(string id) => new(new ItemId(id), id, true, false, ShellIdList.Empty);

    private static IconImage Image(int size, byte fill = 1, byte alpha = 255)
    {
        var pixels = new byte[size * size * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = pixels[i + 1] = pixels[i + 2] = Math.Min(fill, alpha);
            pixels[i + 3] = alpha;
        }

        return new IconImage(size, size, pixels, AlphaKind.Premultiplied);
    }

    private sealed class FakeIconSource : IIconSource
    {
        private readonly ConcurrentDictionary<(ItemId, int), IconImage?> _images = new();
        private readonly ConcurrentDictionary<(ItemId, int), int> _calls = new();
        private readonly ConcurrentDictionary<ItemId, bool> _failNext = new();

        public ApartmentState LastApartment { get; private set; }

        public int LastThreadId { get; private set; }

        public void Set(ItemId id, int size, IconImage? image) => _images[(id, size)] = image;

        public void FailNext(ItemId id) => _failNext[id] = true;

        public int CallCount(ItemId id, int size) => _calls.GetValueOrDefault((id, size));

        public IconImage? Extract(DesktopItem item, int pixelSize)
        {
            LastApartment = Thread.CurrentThread.GetApartmentState();
            LastThreadId = Environment.CurrentManagedThreadId;
            _calls.AddOrUpdate((item.Id, pixelSize), 1, (_, n) => n + 1);
            if (_failNext.TryRemove(item.Id, out _))
            {
                throw new InvalidOperationException("Synthetic icon handler failure.");
            }

            return _images.TryGetValue((item.Id, pixelSize), out var image) ? image : Image(pixelSize);
        }

        public void Dispose()
        {
        }
    }
}
