using Tents.Shell.Interop;

namespace Tents.Tests.Shell;

public class ShellIdListTests
{
    // Two entries: sizes 4 and 5 (each size includes its own 2-byte size field), then the 2-byte terminator.
    private static readonly byte[] _twoItems = [4, 0, 0xAA, 0xBB, 5, 0, 1, 2, 3, 0, 0];

    [Fact]
    public void Empty_IsJustTheTerminator()
    {
        Assert.Equal(2, ShellIdList.Empty.Length);
        Assert.Equal(0, ShellIdList.Empty.ItemCount);
    }

    [Fact]
    public void FromBytes_MeasuresEntriesAndTerminator()
    {
        var list = ShellIdList.FromBytes(_twoItems);

        Assert.Equal(_twoItems.Length, list.Length);
        Assert.Equal(2, list.ItemCount);
    }

    [Fact]
    public void FromBytes_IgnoresBytesAfterTheTerminator()
    {
        var list = ShellIdList.FromBytes([.. _twoItems, 9, 9, 9]);

        Assert.Equal(_twoItems.Length, list.Length);
        Assert.Equal(ShellIdList.FromBytes(_twoItems), list);
    }

    [Theory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 0 })]
    [InlineData(new byte[] { 4, 0, 0xAA, 0xBB })] // no terminator
    [InlineData(new byte[] { 9, 0, 1, 2, 0, 0 })] // entry runs past the end
    [InlineData(new byte[] { 1, 0, 0, 0 })] // entry smaller than its size field
    public void FromBytes_RejectsMalformedLists(byte[] bytes) =>
        Assert.Throws<ArgumentException>(() => ShellIdList.FromBytes(bytes));

    [Fact]
    public void Equality_IsByContent()
    {
        var a = ShellIdList.FromBytes(_twoItems);
        var b = ShellIdList.FromBytes((byte[])_twoItems.Clone());
        var c = ShellIdList.FromBytes([4, 0, 0xAA, 0xBC, 0, 0]);

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, c);
    }

    [Fact]
    public unsafe void FromNative_CopiesTheWholeList()
    {
        fixed (byte* p = _twoItems)
        {
            var list = ShellIdList.FromNative((Windows.Win32.UI.Shell.Common.ITEMIDLIST*)p);
            Assert.Equal(ShellIdList.FromBytes(_twoItems), list);
        }
    }
}
