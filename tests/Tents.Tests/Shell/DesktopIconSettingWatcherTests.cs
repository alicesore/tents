using Microsoft.Win32;
using Tents.Shell.IconLayer;

namespace Tents.Tests.Shell;

/// <summary>
/// The registry change watch on a throwaway key under HKEY_CURRENT_USER\Software\Tents.Tests, deleted afterwards.
/// Explorer's own key is never written.
/// </summary>
public sealed class DesktopIconSettingWatcherTests : IDisposable
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);

    private readonly string _keyPath = $@"Software\Tents.Tests\{Guid.NewGuid():N}";
    private readonly SemaphoreSlim _changed = new(0);

    public DesktopIconSettingWatcherTests() => Registry.CurrentUser.CreateSubKey(_keyPath).Dispose();

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(_keyPath, throwOnMissingSubKey: false);
        using (var parent = Registry.CurrentUser.OpenSubKey(@"Software\Tents.Tests", writable: true))
        {
            if (parent is not null && parent.SubKeyCount == 0 && parent.ValueCount == 0)
            {
                Registry.CurrentUser.DeleteSubKey(@"Software\Tents.Tests", throwOnMissingSubKey: false);
            }
        }

        _changed.Dispose();
    }

    [Fact]
    public async Task ValueChange_IsReported_AndTheWatchIsArmedAgain()
    {
        using var watcher = new DesktopIconSettingWatcher(_keyPath);
        watcher.Changed += (_, _) => _changed.Release();
        Assert.True(watcher.Start());

        SetValue(1);
        Assert.True(await _changed.WaitAsync(_timeout), "The first change was not reported.");

        SetValue(0);
        Assert.True(await _changed.WaitAsync(_timeout), "The second change was not reported.");
    }

    [Fact]
    public async Task NothingChanging_ReportsNothing()
    {
        using var watcher = new DesktopIconSettingWatcher(_keyPath);
        watcher.Changed += (_, _) => _changed.Release();
        watcher.Start();

        Assert.False(await _changed.WaitAsync(TimeSpan.FromMilliseconds(300)));
    }

    [Fact]
    public async Task AfterDispose_ChangesAreNotReported()
    {
        var watcher = new DesktopIconSettingWatcher(_keyPath);
        watcher.Changed += (_, _) => _changed.Release();
        watcher.Start();

        watcher.Dispose();
        SetValue(1);

        Assert.False(await _changed.WaitAsync(TimeSpan.FromMilliseconds(500)));
    }

    [Fact]
    public void MissingKey_CannotBeWatched()
    {
        using var watcher = new DesktopIconSettingWatcher(_keyPath + @"\missing");

        Assert.False(watcher.Start());
    }

    private void SetValue(int value)
    {
        using var key = Registry.CurrentUser.OpenSubKey(_keyPath, writable: true)!;
        key.SetValue("HideIcons", value, RegistryValueKind.DWord);
    }
}
