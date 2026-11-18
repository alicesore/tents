using Tents.Core;
using Tents.Shell.Desktop;

namespace Tents.Tests.Shell;

public class DesktopItemIdentityTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FromParsingName_NoName_GivesNoId(string? name) => Assert.Null(DesktopItemIdentity.FromParsingName(name));

    [Fact]
    public void FromParsingName_KeepsTheShellString()
    {
        const string name = @"C:\Users\Someone\Desktop\Report.txt";

        Assert.Equal(name, DesktopItemIdentity.FromParsingName(name)!.Value.Value);
    }

    [Fact]
    public void SameFile_DifferentCasing_IsTheSameItem() => Assert.Equal(
        DesktopItemIdentity.FromParsingName(@"C:\Users\Someone\Desktop\Report.txt"),
        DesktopItemIdentity.FromParsingName(@"c:\users\someone\desktop\REPORT.TXT"));

    [Fact]
    public void SameName_InUserAndPublicDesktop_AreDifferentItems() => Assert.NotEqual(
        DesktopItemIdentity.FromParsingName(@"C:\Users\Someone\Desktop\Tool.lnk"),
        DesktopItemIdentity.FromParsingName(@"C:\Users\Public\Desktop\Tool.lnk"));

    [Fact]
    public void Rename_IsANewId() => Assert.NotEqual(
        DesktopItemIdentity.FromFileSystemPath(@"C:\Users\Someone\Desktop\a.txt"),
        DesktopItemIdentity.FromFileSystemPath(@"C:\Users\Someone\Desktop\b.txt"));

    [Fact]
    public void WatcherPath_AndParsingName_GiveTheSameId() => Assert.Equal(
        DesktopItemIdentity.FromParsingName(@"C:\Users\Someone\Desktop\a.txt"),
        DesktopItemIdentity.FromFileSystemPath(@"C:\Users\Someone\Desktop\a.txt"));

    [Theory]
    [InlineData("a.txt")]
    [InlineData(@"Desktop\a.txt")]
    [InlineData(@"\a.txt")]
    public void FromFileSystemPath_RejectsRelativePaths(string path) =>
        Assert.Throws<ArgumentException>(() => DesktopItemIdentity.FromFileSystemPath(path));

    [Theory]
    [InlineData("::{20D04FE0-3AEA-1069-A2D8-08002B30309D}", true)]
    [InlineData(@"C:\Users\Someone\Desktop\a.txt", false)]
    public void IsVirtual_RecognizesClsidNames(string name, bool expected) =>
        Assert.Equal(expected, DesktopItemIdentity.IsVirtual(new ItemId(name)));
}
