using Microsoft.Win32;

namespace Tents.App;

/// <summary>Where a "start with Windows" entry is stored. The real one is the user's <c>Run</c> key.</summary>
internal interface IRunKey
{
    string? GetValue(string name);

    void SetValue(string name, string value);

    void DeleteValue(string name);
}

/// <summary>What <see cref="StartWithWindows.State"/> found.</summary>
internal enum StartupState
{
    /// <summary>No entry: Tents does not start with Windows.</summary>
    Off,

    /// <summary>The entry starts this Tents.exe.</summary>
    On,

    /// <summary>The entry starts a Tents.exe somewhere else (the app was moved or updated in another folder).</summary>
    OnElsewhere,
}

/// <summary>
/// The tray's "Start with Windows" toggle (M1 step 20): one value named <c>Tents</c> under
/// <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>, holding the quoted path of <c>Tents.exe</c>. Per user, no
/// administrator rights, nothing else touched.
/// </summary>
/// <remarks>
/// <para>Turning it on when the entry points elsewhere rewrites it to this executable. Turning it off deletes only
/// Tents' own value. The registry is read when the tray menu opens, never watched or polled.</para>
/// <para>If the user disables the entry in Task Manager's Startup apps, Windows keeps the value but does not run it; that
/// switch is not read here (it is not a documented interface), so the menu can show the toggle as on.</para>
/// <para>Source: <see href="https://learn.microsoft.com/windows/win32/setupapi/run-and-runonce-registry-keys">Run and RunOnce
/// Registry Keys</see>.</para>
/// </remarks>
internal sealed class StartWithWindows
{
    public const string ValueName = "Tents";
    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private readonly IRunKey _key;
    private readonly string _executable;

    public StartWithWindows(IRunKey key, string executable)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        _key = key;
        _executable = executable;
    }

    /// <summary>The value written for this executable: its full path in quotes, no arguments.</summary>
    public string Command => $"\"{_executable}\"";

    public StartupState State
    {
        get
        {
            var value = _key.GetValue(ValueName);
            if (string.IsNullOrWhiteSpace(value))
            {
                return StartupState.Off;
            }

            return string.Equals(PathOf(value), _executable, StringComparison.OrdinalIgnoreCase) ? StartupState.On : StartupState.OnElsewhere;
        }
    }

    public bool IsOn => State != StartupState.Off;

    /// <summary>Turns starting with Windows on (for this executable) or off.</summary>
    public void Set(bool on)
    {
        if (on)
        {
            if (State != StartupState.On)
            {
                _key.SetValue(ValueName, Command);
            }
        }
        else if (State != StartupState.Off)
        {
            _key.DeleteValue(ValueName);
        }
    }

    /// <summary>The executable path in a Run value: the quoted part, or the whole value when unquoted. Pure.</summary>
    internal static string PathOf(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.StartsWith('"'))
        {
            var end = trimmed.IndexOf('"', 1);
            return end > 1 ? trimmed[1..end] : trimmed.Trim('"');
        }

        return trimmed;
    }
}

/// <summary>The current user's <c>Run</c> key.</summary>
internal sealed class UserRunKey : IRunKey
{
    public string? GetValue(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(StartWithWindows.RunKeyPath);
        return key?.GetValue(name) as string;
    }

    public void SetValue(string name, string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(StartWithWindows.RunKeyPath);
        key.SetValue(name, value, RegistryValueKind.String);
    }

    public void DeleteValue(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(StartWithWindows.RunKeyPath, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }
}
