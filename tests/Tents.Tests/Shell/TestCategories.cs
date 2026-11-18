namespace Tents.Tests.Shell;

/// <summary>Trait values for tests that cannot run in CI. See CONTRIBUTING.md, "Tests that need a desktop".</summary>
internal static class TestCategories
{
    public const string Key = "Category";

    /// <summary>Needs a signed-in desktop session with Explorer running. Reads the desktop, never changes it.</summary>
    public const string Desktop = "Desktop";

    /// <summary>
    /// Changes the desktop (switches the native icons off and on). Never in CI; runs only with
    /// TENTS_ALLOW_DESKTOP_CHANGES=1 and with the user's consent.
    /// </summary>
    public const string DesktopChanges = "DesktopChanges";
}
