using Tents.App;
using Tents.Core;
using Tents.Shell.Desktop;
using Tents.Shell.Hosting;

namespace Tents.Tests.App;

public sealed class TentWindowSetTests
{
    private static readonly MonitorConfigKey _monitors = MonitorConfigKey.Create(new MonitorDescriptor("D1", new PixelRect(0, 0, 1920, 1080)));
    private readonly FakeHost _host = new();
    private readonly FakeFactory _factory = new();

    [Fact]
    public void Sync_CreatesOneWindowPerTent()
    {
        var set = new TentWindowSet(_host, _factory);
        var layout = TwoTents(out var a, out var b);

        set.Sync(layout, NoItems);

        Assert.Equal(2, set.Count);
        Assert.Equal([a, b], _factory.Created.Select(w => w.TentId));
    }

    [Fact]
    public void Sync_UpdatesExistingWindows_AndClosesTheWindowsOfDeletedTents()
    {
        var set = new TentWindowSet(_host, _factory);
        var layout = TwoTents(out var a, out var b);
        set.Sync(layout, NoItems);

        set.Sync(layout.RenameTent(a, "Renamed").DeleteTent(b), NoItems);

        Assert.Equal(1, set.Count);
        Assert.Equal(2, _factory.Created.Count); // nothing new
        Assert.Equal("Renamed", _factory.Created[0].Tent.Title);
        Assert.True(_factory.Created[1].Closed);
    }

    [Fact]
    public void ExplorerRestart_ReattachesLiveWindows_AndRecreatesTheOnesThatAreGone()
    {
        var set = new TentWindowSet(_host, _factory);
        var layout = TwoTents(out var a, out var b);
        set.Sync(layout, NoItems);
        _host.Results[_factory.Created[0].Handle] = ReattachResult.Reattached;
        _host.Results[_factory.Created[1].Handle] = ReattachResult.WindowGone;

        var summary = set.OnExplorerRestarted(layout, NoItems);

        Assert.Equal(new ExplorerRestartSummary(Reattached: 1, AlreadyAttached: 0, Recreated: 1, WaitingForDesktop: 0), summary);
        Assert.Equal(3, _factory.Created.Count);
        Assert.Equal(b, _factory.Created[2].TentId);
        Assert.True(set.TryGet(b, out var window));
        Assert.Same(_factory.Created[2], window);
        Assert.True(set.TryGet(a, out window));
        Assert.Same(_factory.Created[0], window);
    }

    [Fact]
    public void ExplorerRestart_IsIdempotent()
    {
        var set = new TentWindowSet(_host, _factory);
        var layout = TwoTents(out _, out _);
        set.Sync(layout, NoItems);

        var first = set.OnExplorerRestarted(layout, NoItems);
        var second = set.OnExplorerRestarted(layout, NoItems);

        Assert.Equal(new ExplorerRestartSummary(0, 2, 0, 0), first);
        Assert.Equal(first, second);
        Assert.Equal(2, _factory.Created.Count);
    }

    [Fact]
    public void ExplorerRestart_WithoutADesktopYet_KeepsTheWindowsForTheNextTry()
    {
        var set = new TentWindowSet(_host, _factory);
        var layout = TwoTents(out _, out _);
        set.Sync(layout, NoItems);
        _host.Default = ReattachResult.NoDesktop;

        var summary = set.OnExplorerRestarted(layout, NoItems);

        Assert.Equal(2, summary.WaitingForDesktop);
        Assert.Equal(2, set.Count);
        Assert.DoesNotContain(_factory.Created, w => w.Closed);
    }

    [Fact]
    public void AWindowDestroyedFromOutside_GetsANewWindowAtTheNextRestart()
    {
        var set = new TentWindowSet(_host, _factory);
        var layout = TwoTents(out var a, out _);
        set.Sync(layout, NoItems);

        set.WindowDestroyed(_factory.Created[0]);
        Assert.Equal(1, set.Count);
        var summary = set.OnExplorerRestarted(layout, NoItems);

        Assert.Equal(1, summary.Recreated);
        Assert.True(set.TryGet(a, out var window));
        Assert.Same(_factory.Created[2], window);
    }

    [Fact]
    public void WindowDestroyed_ForAWindowAlreadyReplaced_IsIgnored()
    {
        var set = new TentWindowSet(_host, _factory);
        var layout = TwoTents(out var a, out _);
        set.Sync(layout, NoItems);
        var old = _factory.Created[0];
        _host.Results[old.Handle] = ReattachResult.WindowGone;
        set.OnExplorerRestarted(layout, NoItems);

        set.WindowDestroyed(old); // the old window's late Closed event

        Assert.True(set.TryGet(a, out var window));
        Assert.NotSame(old, window);
    }

    [Fact]
    public void CloseAll_ClosesEveryWindow()
    {
        var set = new TentWindowSet(_host, _factory);
        set.Sync(TwoTents(out _, out _), NoItems);

        set.CloseAll();

        Assert.Equal(0, set.Count);
        Assert.All(_factory.Created, w => Assert.True(w.Closed));
    }

    private static IReadOnlyList<DesktopItem> NoItems(Tent tent) => [];

    private static Layout TwoTents(out TentId a, out TentId b)
    {
        a = TentId.New();
        b = TentId.New();
        return Layout.CreateWithDefaultTent(_monitors, new Tent(a, "Desktop", new PixelRect(0, 0, 100, 100)))
            .CreateTent(b, "Work", new PixelRect(200, 0, 100, 100));
    }

    private sealed class FakeHost : IDesktopHost
    {
        public Dictionary<nint, ReattachResult> Results { get; } = [];

        public ReattachResult Default { get; set; } = ReattachResult.AlreadyAttached;

        public string Name => "fake";

        public nint GetOwnerForNewWindow() => 0;

        public void Prepare(nint hwnd)
        {
        }

        public bool ProcessMessage(nint hwnd, int message, nint wParam, nint lParam, out nint result)
        {
            result = 0;
            return false;
        }

        public ReattachResult Reattach(nint hwnd) => Results.TryGetValue(hwnd, out var result) ? result : Default;
    }

    private sealed class FakeFactory : ITentWindowFactory
    {
        public List<FakeWindow> Created { get; } = [];

        public ITentWindow Create(Tent tent, IReadOnlyList<DesktopItem> items)
        {
            var window = new FakeWindow(tent, Created.Count + 1);
            Created.Add(window);
            return window;
        }
    }

    private sealed class FakeWindow(Tent tent, nint handle) : ITentWindow
    {
        public Tent Tent { get; private set; } = tent;

        public bool Closed { get; private set; }

        public TentId TentId => Tent.Id;

        public nint Handle => Closed ? 0 : handle;

        public void Update(Tent tent, IReadOnlyList<DesktopItem> items) => Tent = tent;

        public void ClearSelection()
        {
        }

        public void Close() => Closed = true;
    }
}
