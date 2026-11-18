using Tents.Core;

namespace Tents.Shell.Desktop;

/// <summary>
/// How a desktop item's identity is derived. Pure, so the rules are unit-tested without a desktop.
/// </summary>
/// <remarks>
/// <para><b>The rule.</b> An item's <see cref="ItemId"/> is its desktop-absolute parsing name, exactly as the shell
/// returns it. <see cref="ItemId"/> compares case-insensitively. No other normalization is applied: the shell already
/// returns a canonical full path for file system items, and rewriting it (for example resolving a redirected Desktop
/// folder) would make ids disagree with what the shell gives back later.</para>
/// <para><b>What it means.</b> The same file has the same id across restarts and Explorer restarts. A file in the
/// user's Desktop folder and one with the same name in the public Desktop folder have different ids (different
/// paths). A rename or a move is a new id; the file watcher (M1.7) reports the old and new path so the app can carry
/// tent membership over. Virtual items keep their <c>::{CLSID}</c> id.</para>
/// </remarks>
public static class DesktopItemIdentity
{
    /// <summary>The id for a parsing name, or null when the shell gave no usable name.</summary>
    public static ItemId? FromParsingName(string? parsingName) =>
        string.IsNullOrWhiteSpace(parsingName) ? null : new ItemId(parsingName);

    /// <summary>The id of a file or folder on disk, for paths reported by the file watcher.</summary>
    /// <exception cref="ArgumentException"><paramref name="fullPath"/> is not a full path.</exception>
    public static ItemId FromFileSystemPath(string fullPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);
        if (!Path.IsPathFullyQualified(fullPath))
        {
            throw new ArgumentException("A desktop item path must be fully qualified.", nameof(fullPath));
        }

        return new ItemId(fullPath);
    }

    /// <summary>True for a virtual (non-file) item's parsing name, such as <c>::{CLSID}</c>.</summary>
    public static bool IsVirtual(ItemId id) => id.Value.StartsWith("::", StringComparison.Ordinal);
}
