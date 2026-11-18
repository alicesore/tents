using System.Diagnostics;

namespace Tents.Shell.IconLayer;

/// <summary>Switches the desktop's native icons on and off ("Show desktop icons").</summary>
public interface IDesktopIconSwitch
{
    Task<bool> AreIconsShownAsync();

    Task SetIconsShownAsync(bool shown);
}

/// <summary>Tells whether a process recorded in the state file is still the same, running process.</summary>
public interface IProcessProbe
{
    bool IsRunning(int processId, DateTimeOffset startTime);

    (int ProcessId, DateTimeOffset StartTime) Current { get; }
}

/// <summary>What <see cref="IconLayerGuard.RecoverAsync"/> found at startup.</summary>
public enum StartupRecovery
{
    /// <summary>No state file: the last run cleaned up.</summary>
    NothingToDo,

    /// <summary>A previous Tents died with the icons off. They were turned back on.</summary>
    RestoredIcons,

    /// <summary>A previous Tents died, but the icons were already on (the user or a restart turned them on).</summary>
    IconsWereAlreadyOn,

    /// <summary>The process that turned them off is still running (a second instance). Nothing was changed.</summary>
    OwnerStillRunning,
}

/// <summary>What <see cref="IconLayerGuard.TurnIconsOffAsync"/> did.</summary>
public enum TurnOffResult
{
    /// <summary>The icons were on; the state file was written, then they were turned off.</summary>
    TurnedOff,

    /// <summary>Tents had already turned them off.</summary>
    AlreadyOffByTents,

    /// <summary>The user had them off. Tents does not take ownership and will not turn them on later.</summary>
    AlreadyOffByUser,
}

/// <summary>
/// Owns the "Tents turned the desktop icons off" state, so that no path leaves the user's desktop without icons:
/// state file first, then the switch; switch back, then delete the file; check the file at every start.
/// </summary>
/// <remarks>
/// <para><b>Order.</b> Off: write the state file, then switch (a crash between the two leaves a file but icons on,
/// which the next start cleans up). On: switch, then delete the file (a crash between the two leaves a file and icons
/// on, same). A failed write means the icons are not switched off at all.</para>
/// <para><b>The user's own setting wins.</b> If the icons are already off and no state file exists, the user turned them
/// off; Tents does not take ownership and never turns them on for them. If the user turns them back on while Tents runs,
/// the next <see cref="RecheckAsync"/> gives up ownership and reports it, and the app treats it as Pause.</para>
/// <para><b>After either switch</b> the native view lists 0 items for about 55 to 65 ms (measured; spike 2 saw it after
/// switching on). Nothing here reads the item list, and callers must not reconcile tents from a view read made right
/// after <see cref="TurnIconsOffAsync"/> or <see cref="TurnIconsOnAsync"/>.</para>
/// <para>Pure apart from the injected switch, state file and process probe, so the rules are unit-tested.</para>
/// </remarks>
public sealed class IconLayerGuard : IDisposable
{
    private readonly IDesktopIconSwitch _switch;
    private readonly IconLayerStateFile _stateFile;
    private readonly IProcessProbe _probe;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public IconLayerGuard(IDesktopIconSwitch iconSwitch, IconLayerStateFile stateFile, IProcessProbe? probe = null, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(iconSwitch);
        ArgumentNullException.ThrowIfNull(stateFile);
        _switch = iconSwitch;
        _stateFile = stateFile;
        _probe = probe ?? new SystemProcessProbe();
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Releases the guard's lock. Does not touch the icons: call <see cref="TurnIconsOnAsync"/> first.</summary>
    public void Dispose() => _gate.Dispose();

    /// <summary>True while Tents owns the icons-off state (the state file exists).</summary>
    public bool OwnsIconsOff => _stateFile.Exists;

    /// <summary>Checks the state file left by an earlier run. Call once at startup, before turning icons off.</summary>
    public async Task<StartupRecovery> RecoverAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_stateFile.Read() is not { } state)
            {
                return StartupRecovery.NothingToDo;
            }

            var current = _probe.Current;
            var isUs = state.ProcessId == current.ProcessId && SystemProcessProbe.Same(state.ProcessStartTime, current.StartTime);
            if (!isUs && state.ProcessId > 0 && _probe.IsRunning(state.ProcessId, state.ProcessStartTime))
            {
                return StartupRecovery.OwnerStillRunning;
            }

            var result = StartupRecovery.IconsWereAlreadyOn;
            if (!await _switch.AreIconsShownAsync().ConfigureAwait(false))
            {
                await _switch.SetIconsShownAsync(true).ConfigureAwait(false);
                result = StartupRecovery.RestoredIcons;
            }

            _stateFile.Delete();
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Turns the native icons off (Tents draws every item). State file first.</summary>
    public async Task<TurnOffResult> TurnIconsOffAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var shown = await _switch.AreIconsShownAsync().ConfigureAwait(false);
            if (!shown)
            {
                return _stateFile.Exists ? TurnOffResult.AlreadyOffByTents : TurnOffResult.AlreadyOffByUser;
            }

            var current = _probe.Current;
            _stateFile.Write(new IconLayerState(IconLayerState.CurrentVersion, current.ProcessId, current.StartTime, _time.GetUtcNow()));
            try
            {
                await _switch.SetIconsShownAsync(false).ConfigureAwait(false);
            }
            catch
            {
                // Keep the file unless the icons are verifiably still on: a half-done switch must stay recoverable.
                if (await IsShownOrUnknownAsync().ConfigureAwait(false) == true)
                {
                    _stateFile.Delete();
                }

                throw;
            }

            return TurnOffResult.TurnedOff;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<bool?> IsShownOrUnknownAsync()
    {
        try
        {
            return await _switch.AreIconsShownAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ShellPlatform.ReportCallbackException(ex))
        {
            return null;
        }
    }

    /// <summary>
    /// Turns the native icons back on if Tents turned them off (Pause, Exit), then deletes the state file. Leaves the
    /// user's own setting alone. If switching fails the file stays, so the next start or the watchdog retries.
    /// </summary>
    /// <returns>True when icons were turned on.</returns>
    public async Task<bool> TurnIconsOnAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_stateFile.Exists)
            {
                return false;
            }

            var turnedOn = false;
            if (!await _switch.AreIconsShownAsync().ConfigureAwait(false))
            {
                await _switch.SetIconsShownAsync(true).ConfigureAwait(false);
                turnedOn = true;
            }

            _stateFile.Delete();
            return turnedOn;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Checks whether the user turned Show desktop icons back on while Tents owned the icons-off state. If so, ownership
    /// ends and this returns true, which the app treats as Pause. Otherwise false, and nothing changes.
    /// </summary>
    /// <remarks>
    /// <para>Nothing notices the switch live: Explorer's switch raises no registry change notification (measured on
    /// Windows 11 build 26300), and a live trigger (a WinEvent hook on the desktop's icon window) was deferred. The app
    /// calls this at moments it is already woken for: start (after <see cref="RecoverAsync"/>), resume from sleep,
    /// session unlock, and Explorer restart (<c>TaskbarCreated</c>). Between those moments, icons the user switched on
    /// show twice until the next one or until they press Pause.</para>
    /// <para>It costs one view read, and it never turns icons on or off.</para>
    /// </remarks>
    public async Task<bool> RecheckAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_stateFile.Exists || !await _switch.AreIconsShownAsync().ConfigureAwait(false))
            {
                return false;
            }

            _stateFile.Delete();
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }
}

/// <summary>The real process table.</summary>
internal sealed class SystemProcessProbe : IProcessProbe
{
    public (int ProcessId, DateTimeOffset StartTime) Current { get; } = ReadCurrent();

    public bool IsRunning(int processId, DateTimeOffset startTime)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited && Same(new DateTimeOffset(process.StartTime.ToUniversalTime()), startTime);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false; // Not running, or not ours to inspect (then it is not a Tents of this user).
        }
    }

    // Start times are compared to the second: the recorded value went through JSON.
    internal static bool Same(DateTimeOffset a, DateTimeOffset b) => Math.Abs((a - b).TotalSeconds) < 1;

    private static (int, DateTimeOffset) ReadCurrent()
    {
        using var process = Process.GetCurrentProcess();
        return (process.Id, new DateTimeOffset(process.StartTime.ToUniversalTime()));
    }
}
