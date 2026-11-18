using Tents.Shell.IconLayer;

namespace Tents.App;

/// <summary>
/// The app's side of the native desktop icons, on top of the part B <see cref="IconLayerGuard"/>: startup recovery,
/// turning the icons off while tents show the items, and back on for Pause and Exit.
/// </summary>
/// <remarks>
/// <para><b>Recovery always runs</b>, also while switching is disabled: if an earlier Tents died with the icons off, they
/// come back at the next start.</para>
/// <para><b>Switching</b> only happens when <see cref="SwitchingEnabled"/> is true (the <c>--hide-native-icons</c>
/// developer flag in this step; on by default once crash safety, M1 step 21, is in place). Otherwise tents show next to
/// the native icons and Pause only hides the tents.</para>
/// <para>Every call awaits the guard, which runs on the desktop view's own thread; the UI thread never blocks. Failures
/// (no desktop view while Explorer restarts) are logged and leave the icons as they are.</para>
/// </remarks>
internal sealed class NativeIconLayer
{
    private readonly IconLayerGuard _guard;
    private readonly AppLog _log;

    public NativeIconLayer(IconLayerGuard guard, AppLog log, bool switchingEnabled)
    {
        ArgumentNullException.ThrowIfNull(guard);
        ArgumentNullException.ThrowIfNull(log);
        _guard = guard;
        _log = log;
        SwitchingEnabled = switchingEnabled;
    }

    /// <summary>True when Tents turns the native icons off while it shows the tents.</summary>
    public bool SwitchingEnabled { get; }

    /// <summary>True while Tents has the icons off.</summary>
    public bool OwnsIconsOff => _guard.OwnsIconsOff;

    /// <summary>Checks the state file of an earlier run and turns the icons back on if it left them off.</summary>
    public async Task RecoverAsync()
    {
        try
        {
            var result = await _guard.RecoverAsync().ConfigureAwait(false);
            if (result != StartupRecovery.NothingToDo)
            {
                _log.Write($"Icon layer recovery: {result}.");
            }
        }
        catch (Exception ex) when (IsShellFailure(ex))
        {
            _log.Write($"Icon layer recovery failed ({ex.GetType().Name}); the state file stays for the next try.");
        }
    }

    /// <summary>Turns the native icons off (tents are showing). No-op while switching is disabled.</summary>
    public async Task HideNativeIconsAsync()
    {
        if (!SwitchingEnabled)
        {
            return;
        }

        try
        {
            var result = await _guard.TurnIconsOffAsync().ConfigureAwait(false);
            _log.Write($"Native icons off: {result}.");
        }
        catch (Exception ex) when (IsShellFailure(ex))
        {
            _log.Write($"Turning the native icons off failed ({ex.GetType().Name}); tents show next to them.");
        }
    }

    /// <summary>Turns the native icons back on if Tents turned them off (Pause, Exit).</summary>
    public async Task ShowNativeIconsAsync()
    {
        try
        {
            if (await _guard.TurnIconsOnAsync().ConfigureAwait(false))
            {
                _log.Write("Native icons on.");
            }
        }
        catch (Exception ex) when (IsShellFailure(ex))
        {
            _log.Write($"Turning the native icons on failed ({ex.GetType().Name}); the state file stays, so the next start or the watchdog retries.");
        }
    }

    /// <summary>
    /// True when the user turned Show desktop icons back on while Tents had them off (part B step 8): the app treats it
    /// as Pause. Called at start, resume, unlock and Explorer restart.
    /// </summary>
    public async Task<bool> UserTurnedIconsOnAsync()
    {
        if (!SwitchingEnabled)
        {
            return false;
        }

        try
        {
            return await _guard.RecheckAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (IsShellFailure(ex))
        {
            _log.Write($"Icon layer recheck failed ({ex.GetType().Name}).");
            return false;
        }
    }

    private static bool IsShellFailure(Exception ex) => ex is not (OutOfMemoryException or ObjectDisposedException);
}
