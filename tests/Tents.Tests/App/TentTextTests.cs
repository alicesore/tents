using Tents.App;

namespace Tents.Tests.App;

public sealed class TentTextTests
{
    [Fact]
    public void DeleteQuestion_NamesTheTent_TheItemsAndWhereTheyGo()
    {
        var text = TentText.DeleteQuestion("Work", 12, "Desktop");

        Assert.Equal("Delete the tent \u201CWork\u201D?\n\nIts 12 items move to \u201CDesktop\u201D. No files are deleted.", text);
    }

    [Fact]
    public void DeleteQuestion_SingularAndEmpty()
    {
        Assert.Contains("Its item moves to", TentText.DeleteQuestion("A", 1, "Desktop"), StringComparison.Ordinal);
        Assert.Contains("It is empty.", TentText.DeleteQuestion("A", 0, "Desktop"), StringComparison.Ordinal);
    }

    [Fact]
    public void DeleteQuestion_UntitledTents()
    {
        var text = TentText.DeleteQuestion("  ", 2, "");

        Assert.StartsWith("Delete this tent?", text, StringComparison.Ordinal);
        Assert.Contains("move to the default tent", text, StringComparison.Ordinal);
    }
}
