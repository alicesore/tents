using System.Globalization;

namespace Tents.App;

/// <summary>User-facing text that depends on data. Pure, so the wording is tested.</summary>
internal static class TentText
{
    public const string NewTentTitle = "New tent";

    public const string DeleteCaption = "Delete tent";

    /// <summary>The question asked before a tent is deleted.</summary>
    public static string DeleteQuestion(string title, int itemCount, string defaultTitle)
    {
        var name = string.IsNullOrWhiteSpace(title) ? "this tent" : $"the tent \u201C{title}\u201D";
        var target = string.IsNullOrWhiteSpace(defaultTitle) ? "the default tent" : $"\u201C{defaultTitle}\u201D";
        var items = itemCount switch
        {
            0 => "It is empty.",
            1 => $"Its item moves to {target}.",
            _ => string.Create(CultureInfo.CurrentCulture, $"Its {itemCount} items move to {target}."),
        };
        return $"Delete {name}?\n\n{items} No files are deleted.";
    }
}
