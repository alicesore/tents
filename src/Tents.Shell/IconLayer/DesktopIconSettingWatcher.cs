using Microsoft.Win32;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Registry;

namespace Tents.Shell.IconLayer;

/// <summary>
/// Raises <see cref="Changed"/> when Explorer's per-user Advanced settings change, which is when "Show desktop icons" is
/// toggled (the desktop writes <c>HideIcons</c> there at once, observed in spike 2). The handler then asks the desktop
/// view itself (<see cref="IconLayerGuard.HandleUserTurnedIconsOnAsync"/>): the registry value is only the trigger,
/// never the source of truth, because it is not documented.
/// </summary>
/// <remarks>
/// <para><b>No polling.</b> <c>RegNotifyChangeKeyValue</c> signals an event; a thread-pool wait without timeout waits on
/// it; after each signal the watch is armed again. Other Advanced settings changing also wake it, which is rare and
/// costs one view read. <c>REG_NOTIFY_THREAD_AGNOSTIC</c> keeps the watch alive when the arming thread is a thread-pool
/// thread.</para>
/// <para><b>Does not see Explorer's switch on Windows 11 build 26300.</b> Measured with the DesktopChanges tests:
/// after the switch <c>HideIcons</c> reads the new value, also through a handle opened before it, but no change
/// notification arrives, not on this key and not on the whole <c>HKCU\Software</c> tree. Writes from other processes
/// are reported. Until a different trigger is chosen, the app cannot rely on this watch to notice the user turning the
/// icons back on.</para>
/// <para>Source: <see href="https://learn.microsoft.com/windows/win32/api/winreg/nf-winreg-regnotifychangekeyvalue">RegNotifyChangeKeyValue</see>.</para>
/// </remarks>
public sealed class DesktopIconSettingWatcher : IDisposable
{
    /// <summary>The key Explorer writes the desktop icon setting to.</summary>
    public const string ExplorerAdvancedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";

    private readonly string _keyPath;
    private readonly Lock _gate = new();
    private RegistryKey? _key;
    private AutoResetEvent? _signal;
    private RegisteredWaitHandle? _wait;
    private bool _disposed;

    /// <param name="keyPath">A key under HKEY_CURRENT_USER. Defaults to <see cref="ExplorerAdvancedKey"/>; tests use their own.</param>
    public DesktopIconSettingWatcher(string keyPath = ExplorerAdvancedKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyPath);
        _keyPath = keyPath;
    }

    /// <summary>The key changed. Raised on the thread pool.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Explorer's own record of the setting (<c>HideIcons</c>): true hidden, false shown, null not recorded. Diagnostics
    /// only; <see cref="IDesktopIconSwitch.AreIconsShownAsync"/> is the source of truth.
    /// </summary>
    public static bool? ReadHideIcons()
    {
        using var key = Registry.CurrentUser.OpenSubKey(ExplorerAdvancedKey);
        return key?.GetValue("HideIcons") is int value ? value != 0 : null;
    }

    /// <summary>Starts watching. Returns false when the key does not exist.</summary>
    public bool Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_key is not null)
            {
                return true;
            }

            _key = Registry.CurrentUser.OpenSubKey(_keyPath); // KEY_READ includes KEY_NOTIFY
            if (_key is null)
            {
                return false;
            }

            _signal = new AutoResetEvent(false);
            ArmLocked();
            return true;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _wait?.Unregister(null);
            _wait = null;
            _key?.Dispose(); // closing the key ends the pending notification
            _key = null;
            _signal?.Dispose();
            _signal = null;
        }
    }

    // Under _gate. One notification per arm: RegNotifyChangeKeyValue reports one change, then has to be called again.
    private void ArmLocked()
    {
        var result = PInvoke.RegNotifyChangeKeyValue(
            _key!.Handle,
            false,
            REG_NOTIFY_FILTER.REG_NOTIFY_CHANGE_LAST_SET | REG_NOTIFY_FILTER.REG_NOTIFY_THREAD_AGNOSTIC,
            _signal!.SafeWaitHandle,
            true);
        if (result != WIN32_ERROR.ERROR_SUCCESS)
        {
            throw new InvalidOperationException($"RegNotifyChangeKeyValue failed: {result}.");
        }

        _wait = ThreadPool.RegisterWaitForSingleObject(_signal, OnSignaled, null, Timeout.Infinite, executeOnlyOnce: true);
    }

    private void OnSignaled(object? state, bool timedOut)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _wait?.Unregister(null);
            ArmLocked(); // arm before raising, so a change during the handler is not missed
        }

        try
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ShellPlatform.ReportCallbackException(ex))
        {
        }
    }
}
