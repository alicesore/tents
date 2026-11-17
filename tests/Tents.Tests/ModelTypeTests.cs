using System.Collections.Immutable;
using Tents.Core;
using static Tents.Tests.TestData;

namespace Tents.Tests;

public class TentIdTests
{
    [Fact]
    public void Constructor_RejectsEmptyGuid() => Assert.Throws<ArgumentException>(() => new TentId(Guid.Empty));

    [Fact]
    public void Default_IsEmpty() => Assert.True(default(TentId).IsEmpty);

    [Fact]
    public void New_ReturnsDistinctNonEmptyIds()
    {
        var a = TentId.New();
        var b = TentId.New();

        Assert.False(a.IsEmpty);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Equality_IsByValue() => Assert.Equal(Id(7), Id(7));
}

public class ItemIdTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_RejectsEmptyOrWhitespace(string value) =>
        Assert.Throws<ArgumentException>(() => new ItemId(value));

    [Fact]
    public void Constructor_RejectsNull() => Assert.Throws<ArgumentNullException>(() => new ItemId(null!));

    [Fact]
    public void Equality_IgnoresCase()
    {
        var lower = new ItemId(@"x:\testdesktop\notes.txt");
        var upper = new ItemId(@"X:\TestDesktop\Notes.TXT");

        Assert.Equal(lower, upper);
        Assert.True(lower == upper);
        Assert.Equal(lower.GetHashCode(), upper.GetHashCode());
    }

    [Fact]
    public void Equality_DistinguishesDifferentNames() => Assert.NotEqual(Item("a"), Item("b"));

    [Fact]
    public void Default_IsEmptyWithEmptyValue()
    {
        Assert.True(default(ItemId).IsEmpty);
        Assert.Equal(string.Empty, default(ItemId).Value);
    }
}

public class TentTests
{
    [Fact]
    public void Constructor_RejectsEmptyId() =>
        Assert.Throws<ArgumentException>(() => new Tent(default, "t", SomeBounds));

    [Fact]
    public void Constructor_RejectsNullTitle() =>
        Assert.Throws<ArgumentNullException>(() => new Tent(Id(1), null!, SomeBounds));

    [Theory]
    [InlineData(0, 10)]
    [InlineData(10, 0)]
    [InlineData(-5, 10)]
    public void Constructor_RejectsNonPositiveSize(int width, int height) =>
        Assert.Throws<ArgumentException>(() => new Tent(Id(1), "t", new PixelRect(0, 0, width, height)));

    [Fact]
    public void With_RevalidatesBounds()
    {
        var tent = Tent(1);

        Assert.Throws<ArgumentException>(() => tent with { Bounds = default });
    }

    [Fact]
    public void Constructor_AllowsEmptyTitleAndNegativePosition()
    {
        var tent = new Tent(Id(1), string.Empty, new PixelRect(-1920, -40, 300, 200));

        Assert.Equal(string.Empty, tent.Title);
        Assert.Equal(-1920, tent.Bounds.X);
    }

    [Fact]
    public void DefaultItems_BecomeEmptyArray()
    {
        var tent = new Tent(Id(1), "t", SomeBounds);

        Assert.False(tent.Items.IsDefault);
        Assert.Empty(tent.Items);
        Assert.False(tent.IsRolledUp);
    }

    [Fact]
    public void Equality_ComparesItemsBySequence()
    {
        var a = new Tent(Id(1), "t", SomeBounds, items: Items("a", "b"));
        var b = new Tent(Id(1), "t", SomeBounds, items: ImmutableArray.Create(Item("a"), Item("b")));

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Equality_IsOrderSensitiveForItems() =>
        Assert.NotEqual(Tent(1, "a", "b"), Tent(1, "b", "a"));

    [Fact]
    public void Equality_ComparesTitleOrdinally() =>
        Assert.NotEqual(new Tent(Id(1), "Work", SomeBounds), new Tent(Id(1), "work", SomeBounds));
}

public class MonitorConfigKeyTests
{
    private static readonly MonitorDescriptor _left = new("TEST-MONITOR-1", new PixelRect(0, 0, 1920, 1080));
    private static readonly MonitorDescriptor _right = new("TEST-MONITOR-2", new PixelRect(1920, 0, 2560, 1440));

    [Fact]
    public void Create_RejectsNoMonitors() =>
        Assert.Throws<ArgumentException>(() => MonitorConfigKey.Create(Array.Empty<MonitorDescriptor>()));

    [Fact]
    public void Create_RejectsDefaultDescriptor() =>
        Assert.Throws<ArgumentException>(() => MonitorConfigKey.Create(_left, default));

    [Fact]
    public void Descriptor_RejectsEmptyBounds() =>
        Assert.Throws<ArgumentException>(() => new MonitorDescriptor("M", new PixelRect(0, 0, 0, 1080)));

    [Fact]
    public void Equality_IgnoresReportingOrder()
    {
        var a = MonitorConfigKey.Create(_left, _right);
        var b = MonitorConfigKey.Create(_right, _left);

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.Equal(a.Monitors.AsEnumerable(), b.Monitors.AsEnumerable());
    }

    [Fact]
    public void Equality_IgnoresDeviceIdCase()
    {
        var a = MonitorConfigKey.Create(new MonitorDescriptor("display-ABC", _left.Bounds));
        var b = MonitorConfigKey.Create(new MonitorDescriptor("DISPLAY-abc", _left.Bounds));

        Assert.Equal(a, b);
        Assert.True(a == b);
    }

    [Fact]
    public void Equality_DependsOnResolutionAndPosition()
    {
        var a = MonitorConfigKey.Create(_left);
        var resized = MonitorConfigKey.Create(new MonitorDescriptor(_left.DeviceId, new PixelRect(0, 0, 2560, 1440)));
        var moved = MonitorConfigKey.Create(new MonitorDescriptor(_left.DeviceId, new PixelRect(10, 0, 1920, 1080)));

        Assert.NotEqual(a, resized);
        Assert.NotEqual(a, moved);
    }

    [Fact]
    public void Equality_DependsOnMonitorCount() =>
        Assert.NotEqual(MonitorConfigKey.Create(_left), MonitorConfigKey.Create(_left, _right));

    [Fact]
    public void Create_AllowsIdenticalDescriptors()
    {
        var key = MonitorConfigKey.Create(_left, _left);

        Assert.Equal(2, key.Monitors.Length);
    }
}
