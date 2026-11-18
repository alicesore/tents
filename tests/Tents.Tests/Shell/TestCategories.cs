namespace Tents.Tests.Shell;

/// <summary>Trait values for tests that cannot run in CI. See CONTRIBUTING.md, "Tests that need a desktop".</summary>
internal static class TestCategories
{
    public const string Key = "Category";

    /// <summary>Needs a signed-in desktop session with Explorer running. Reads the desktop, never changes it.</summary>
    public const string Desktop = "Desktop";
}
