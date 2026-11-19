using System.Reflection;
using System.Xml.Linq;
using Tents.Core;

namespace Tents.Tests;

/// <summary>Fails if Tents.Core gains a UI, Win32 or Shell dependency.</summary>
public class CoreBoundaryTests
{
    private static readonly string[] _forbiddenAssemblyPrefixes =
    [
        "PresentationFramework",
        "PresentationCore",
        "WindowsBase",
        "System.Windows",
        "System.Drawing",
        "Microsoft.Win32",
        "Microsoft.Windows",
        "Tents.Shell",
        "Tents.App",
    ];

    // The app's assembly is named after the product (Tents.exe), so it is not caught by the Tents.App prefix.
    private const string _appAssemblyName = "Tents";

    [Fact]
    public void CoreAssembly_DoesNotReferenceUiOrWin32Assemblies()
    {
        var references = typeof(Layout).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .Where(name => _forbiddenAssemblyPrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                || string.Equals(name, _appAssemblyName, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.Empty(references);
    }

    [Fact]
    public void CoreProject_HasNoReferencesAndNoWindowsTarget()
    {
        var project = XDocument.Load(FindRepoFile("src", "Tents.Core", "Tents.Core.csproj"));
        var elements = project.Descendants().ToArray();

        Assert.DoesNotContain(elements, e => e.Name.LocalName is "ProjectReference" or "PackageReference" or "FrameworkReference");
        Assert.DoesNotContain(elements, e => e.Name.LocalName is "UseWPF" or "UseWindowsForms");

        var targets = elements.Where(e => e.Name.LocalName is "TargetFramework" or "TargetFrameworks").Select(e => e.Value);
        Assert.All(targets, t => Assert.DoesNotContain("-windows", t, StringComparison.OrdinalIgnoreCase));
    }

    private static string FindRepoFile(params string[] relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Tents.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        var path = Path.Combine([dir.FullName, .. relativePath]);
        Assert.True(File.Exists(path), $"Expected file not found: {path}");
        return path;
    }
}
