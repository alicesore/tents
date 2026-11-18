using System.Xml.Linq;

namespace Tents.Tests.App;

/// <summary>Tents.exe declares per-monitor v2 DPI awareness (M1 step 13), and the build embeds that manifest.</summary>
public sealed class AppManifestTests
{
    private static readonly XNamespace _windowsSettings2016 = "http://schemas.microsoft.com/SMI/2016/WindowsSettings";
    private static readonly XNamespace _windowsSettings2005 = "http://schemas.microsoft.com/SMI/2005/WindowsSettings";
    private static readonly XNamespace _compatibility = "urn:schemas-microsoft-com:compatibility.v1";

    [Fact]
    public void Manifest_DeclaresPerMonitorV2_WithTheOlderFallback()
    {
        var manifest = XDocument.Load(RepoFile("src", "Tents.App", "app.manifest"));

        Assert.Equal("PerMonitorV2", manifest.Descendants(_windowsSettings2016 + "dpiAwareness").Single().Value.Trim());
        Assert.Equal("true/pm", manifest.Descendants(_windowsSettings2005 + "dpiAware").Single().Value.Trim());
    }

    [Fact]
    public void Manifest_DeclaresWindows10And11()
    {
        var manifest = XDocument.Load(RepoFile("src", "Tents.App", "app.manifest"));

        var ids = manifest.Descendants(_compatibility + "supportedOS").Select(e => (string?)e.Attribute("Id"));
        Assert.Contains("{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}", ids);
    }

    [Fact]
    public void Project_EmbedsTheManifest()
    {
        var project = XDocument.Load(RepoFile("src", "Tents.App", "Tents.App.csproj"));

        Assert.Equal("app.manifest", project.Descendants("ApplicationManifest").Single().Value.Trim());
    }

    [Fact]
    public void BuiltExecutable_ContainsThePerMonitorV2Declaration()
    {
        // The app's output is copied next to the tests; the manifest is an embedded resource in Tents.exe.
        var exe = Path.Combine(AppContext.BaseDirectory, "Tents.exe");
        Assert.True(File.Exists(exe), $"Expected {exe}");

        var text = System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(exe));
        Assert.Contains("PerMonitorV2", text, StringComparison.Ordinal);
    }

    private static string RepoFile(params string[] relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Tents.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return Path.Combine([dir.FullName, .. relativePath]);
    }
}
