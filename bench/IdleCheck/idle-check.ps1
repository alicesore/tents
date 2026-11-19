<#
.SYNOPSIS
    M1 step 22: the idle check. Runs Tents with the typical setup (10 tents, 200 items) and fails if it is over the
    budgets in PROJECT_PLAN.md section 7.

.DESCRIPTION
    Based on spikes/03-perf-baseline/measure.ps1. Needs no elevation and installs nothing.

    The typical setup is synthetic and lives in a new temporary folder that is deleted at the end:
      items\        200 entries: 20 folders, 20 shortcuts to programs in System32, 160 empty files of 8 types
      layout.json   10 tents of 20 items each, in a grid over the primary monitor
      *.log         one Tents log per launch
    Tents runs with --desktop-folder (shows that folder instead of the desktop, never touches the native desktop icons,
    starts no watchdog), --layout-file and --log-file, so neither the desktop, the real layout nor the real log is read
    or changed. The tents do show on screen, behind every other window, and a tray icon appears while Tents runs.

    What it measures (budgets in brackets):
      Startup     one discarded warm-up launch, then -StartupRuns launches. Tents logs the time from process creation
                  until every tent has drawn its first frame; the median counts. [<= 1000 ms]
      Idle        launch, wait -SettleSeconds, sample, wait -IdleSeconds, sample again:
                    CPU time used in the window, scaled to 60 s          [< 16 ms per 60 s]
                    thread wakeups (context switches, counted per thread), scaled to 60 s   [<= 5 per 60 s]
                    working set, private working set, private bytes      [<= 130 / 45 / 80 MB]
      Runtime     after the idle window, -CounterSeconds of dotnet-counters (System.Runtime): GC collections, timers,
                  thread-pool work items. [0 GCs, 0 timers, 0 work items]. Attaching wakes the process, which is why
                  this runs after the idle window, not during it. Skipped with -SkipCounters (then reported as not
                  measured, and the check fails unless -AllowUnmeasured).
      Resize      then one tent is grown in -ResizeSteps steps with SetWindowPos and shrunk back to its size the same
                  way (growing as in M1 step 12, which saw 106 MB private bytes right after resizing). Then the idle
                  window is measured again: memory, CPU and wakeups must be back under budget once idle, so a resize
                  leaves nothing behind. The grown tent's own memory is reported apart, not against the budgets (a
                  bigger tent has bigger render buffers). Skipped with -SkipResize.

    Close other heavy applications and leave the machine alone while it runs (about 4 minutes with the defaults).

    Wakeups here are the per-thread context-switch counters, as in spike 3. To confirm them with ETW, run the script
    with -WprTrace from an elevated PowerShell: it then records the first idle window with Windows Performance Recorder
    (CPU profile) into the results folder. The script never elevates by itself. See bench/IdleCheck/README.md.

.PARAMETER Exe
    The Tents.exe to measure. Default: publish src/Tents.App the way the release does (framework-dependent,
    ReadyToRun, win-x64) into bench/IdleCheck/artifacts/app and measure that.

.EXAMPLE
    .\bench\IdleCheck\idle-check.ps1

.EXAMPLE
    .\bench\IdleCheck\idle-check.ps1 -Exe .\artifacts\release\Tents\Tents.exe -StartupRuns 7
#>
[CmdletBinding()]
param(
    [string] $Exe,
    [int] $Tents = 10,
    [int] $Items = 200,
    [int] $StartupRuns = 5,
    [int] $SettleSeconds = 30,
    [int] $IdleSeconds = 60,
    [int] $CounterSeconds = 20,
    [int] $ResizeSteps = 120,
    [switch] $SkipCounters,
    [switch] $SkipResize,
    [switch] $AllowUnmeasured,
    [switch] $WprTrace,
    [string] $OutDir
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0
# Windows PowerShell 5.1 leaves $PSScriptRoot empty in the defaults of an advanced script's parameters.
if (-not $OutDir) { $OutDir = Join-Path $PSScriptRoot ('artifacts\results-{0:yyyyMMdd-HHmmss}' -f (Get-Date)) }

# PROJECT_PLAN.md section 7, typical setup, measured with nothing changing.
$Budget = [ordered]@{
    StartupMs      = 1000
    CpuMsPer60s    = 16     # below one scheduler tick
    WakeupsPer60s  = 5
    WorkingSetMB   = 130    # 120 until 2026-11-19, raised in M1 step 22; see PROJECT_PLAN.md section 7
    PrivateWsMB    = 45
    PrivateBytesMB = 80
    GcCount        = 0
    TimerCount     = 0
    WorkItems      = 0
}

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class TentsIdleNative
{
    private delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback, IntPtr lParam);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern int GetDeviceCaps(IntPtr hdc, int index);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern uint GetLongPathName(string path, StringBuilder buffer, uint size);
    [DllImport("kernel32.dll")] private static extern IntPtr OpenThread(uint access, bool inherit, uint threadId);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("ntdll.dll")] private static extern int NtQueryInformationThread(IntPtr thread, int infoClass, out IntPtr info, int length, IntPtr returned);

    // The thread's Win32 start address (ThreadQuerySetWin32StartAddress), or 0 if it cannot be read.
    public static long ThreadStart(int threadId)
    {
        var handle = OpenThread(0x0040 /* THREAD_QUERY_INFORMATION */, false, (uint)threadId);
        if (handle == IntPtr.Zero) { return 0; }
        try { IntPtr start; return NtQueryInformationThread(handle, 9, out start, IntPtr.Size, IntPtr.Zero) == 0 ? start.ToInt64() : 0; }
        finally { CloseHandle(handle); }
    }

    // Physical pixels, whatever the display scale: Tents is per-monitor DPI aware.
    private static void PerMonitorAware() { SetThreadDpiAwarenessContext(new IntPtr(-4)); }

    public static IntPtr[] Windows(int pid, string title, bool visibleOnly)
    {
        PerMonitorAware();
        var found = new List<IntPtr>();
        EnumWindows(delegate (IntPtr hwnd, IntPtr unused)
        {
            uint owner;
            GetWindowThreadProcessId(hwnd, out owner);
            if (owner != (uint)pid || (visibleOnly && !IsWindowVisible(hwnd))) { return true; }
            var text = new StringBuilder(64);
            GetWindowText(hwnd, text, text.Capacity);
            if (text.ToString() == title) { found.Add(hwnd); }
            return true;
        }, IntPtr.Zero);
        return found.ToArray();
    }

    public static bool Close(IntPtr hwnd) { return PostMessage(hwnd, 0x0010 /* WM_CLOSE */, IntPtr.Zero, IntPtr.Zero); }

    public static int[] Rect(IntPtr hwnd)
    {
        PerMonitorAware();
        RECT r;
        GetWindowRect(hwnd, out r);
        return new[] { r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top };
    }

    public static void Resize(IntPtr hwnd, int width, int height)
    {
        PerMonitorAware();
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, width, height, 0x0002 | 0x0004 | 0x0010); // NOMOVE | NOZORDER | NOACTIVATE
    }

    public static int[] PrimarySize()
    {
        var dc = GetDC(IntPtr.Zero);
        try { return new[] { GetDeviceCaps(dc, 118 /* DESKTOPHORZRES */), GetDeviceCaps(dc, 117 /* DESKTOPVERTRES */) }; }
        finally { ReleaseDC(IntPtr.Zero, dc); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORY_BASIC_INFORMATION { public IntPtr BaseAddress, AllocationBase; public uint AllocationProtect; public ushort PartitionId; public IntPtr RegionSize; public uint State, Protect, Type; }
    [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, int processId);
    [DllImport("kernel32.dll")] private static extern IntPtr VirtualQueryEx(IntPtr process, IntPtr address, out MEMORY_BASIC_INFORMATION info, IntPtr length);

    // Committed memory by type, in bytes: private, image (DLLs and the exe), mapped (sections and files).
    public static long[] CommitByType(int processId)
    {
        var result = new long[3];
        var process = OpenProcess(0x0400 /* PROCESS_QUERY_INFORMATION */, false, processId);
        if (process == IntPtr.Zero) { return result; }
        try
        {
            long address = 0;
            MEMORY_BASIC_INFORMATION info;
            while (VirtualQueryEx(process, new IntPtr(address), out info, new IntPtr(Marshal.SizeOf(typeof(MEMORY_BASIC_INFORMATION)))) != IntPtr.Zero)
            {
                if (info.State == 0x1000 /* MEM_COMMIT */)
                {
                    if (info.Type == 0x20000) { result[0] += info.RegionSize.ToInt64(); }
                    else if (info.Type == 0x1000000) { result[1] += info.RegionSize.ToInt64(); }
                    else if (info.Type == 0x40000) { result[2] += info.RegionSize.ToInt64(); }
                }
                long next = info.BaseAddress.ToInt64() + info.RegionSize.ToInt64();
                if (next <= address) { break; }
                address = next;
            }
        }
        finally { CloseHandle(process); }
        return result;
    }

    [DllImport("psapi.dll")] private static extern bool QueryWorkingSet(IntPtr process, IntPtr buffer, int size);

    // Working-set pages per address range (sorted bases), plus two totals at the end: pages outside every range that
    // are private, and that are shared. 4 KB pages.
    public static long[] WorkingSetPages(int processId, long[] bases, long[] sizes)
    {
        var counts = new long[bases.Length + 2];
        var process = OpenProcess(0x0410 /* QUERY_INFORMATION | VM_READ */, false, processId);
        if (process == IntPtr.Zero) { return counts; }
        int bytes = 1 << 20;
        IntPtr buffer = IntPtr.Zero;
        try
        {
            while (true)
            {
                buffer = Marshal.AllocHGlobal(bytes);
                if (QueryWorkingSet(process, buffer, bytes)) { break; }
                Marshal.FreeHGlobal(buffer); buffer = IntPtr.Zero;
                if (bytes > (1 << 28)) { return counts; }
                bytes *= 2;
            }
            long entries = Marshal.ReadIntPtr(buffer).ToInt64();
            for (long i = 0; i < entries; i++)
            {
                long entry = Marshal.ReadIntPtr(buffer, (int)((i + 1) * IntPtr.Size)).ToInt64();
                long address = entry & ~0xFFFL;
                int index = Array.BinarySearch(bases, address);
                if (index < 0) { index = ~index - 1; }
                if (index >= 0 && address < bases[index] + sizes[index]) { counts[index]++; }
                else if ((entry & 0x100) == 0) { counts[bases.Length]++; }
                else { counts[bases.Length + 1]++; }
            }
        }
        finally
        {
            if (buffer != IntPtr.Zero) { Marshal.FreeHGlobal(buffer); }
            CloseHandle(process);
        }
        return counts;
    }

    public static string LongPath(string path)
    {
        var buffer = new StringBuilder(1024);
        return GetLongPathName(path, buffer, (uint)buffer.Capacity) > 0 ? buffer.ToString() : path;
    }
}
'@

# ---------------------------------------------------------------- helpers

function Get-Median($values) {
    $sorted = @($values | Sort-Object)
    if ($sorted.Count -eq 0) { return $null }
    return $sorted[[int][math]::Floor(($sorted.Count - 1) / 2)]
}

function New-TypicalSetup([string] $root) {
    $itemsDir = Join-Path $root 'items'
    New-Item -ItemType Directory -Force $itemsDir | Out-Null
    $paths = [System.Collections.Generic.List[string]]::new()

    $folders = [int][math]::Floor($Items / 10)
    $shortcuts = [int][math]::Floor($Items / 10)
    foreach ($i in 1..$folders) {
        $paths.Add((New-Item -ItemType Directory (Join-Path $itemsDir ('Folder {0:D3}' -f $i))).FullName)
    }

    # Shortcuts to programs that ship with Windows, so the icons are real and mostly distinct.
    $targets = @('notepad.exe', 'calc.exe', 'cmd.exe', 'mspaint.exe', 'charmap.exe', 'regedit.exe', 'taskmgr.exe',
        'mmc.exe', 'control.exe', 'write.exe', 'magnify.exe', 'osk.exe', 'narrator.exe', 'msinfo32.exe', 'perfmon.exe',
        'resmon.exe', 'mstsc.exe', 'snippingtool.exe', 'cleanmgr.exe', 'dxdiag.exe') |
        ForEach-Object { Join-Path $env:WINDIR "System32\$_" } | Where-Object { Test-Path $_ }
    if (-not $targets) { $targets = @(Join-Path $env:WINDIR 'System32\cmd.exe') }
    $shell = New-Object -ComObject WScript.Shell
    try {
        foreach ($i in 1..$shortcuts) {
            $path = Join-Path $itemsDir ('Shortcut {0:D3}.lnk' -f $i)
            $link = $shell.CreateShortcut($path)
            $link.TargetPath = $targets[($i - 1) % $targets.Count]
            $link.Save()
            $paths.Add($path)
        }
    }
    finally {
        [void][Runtime.InteropServices.Marshal]::ReleaseComObject($shell)
    }

    $extensions = @('.txt', '.pdf', '.docx', '.xlsx', '.png', '.jpg', '.zip', '.md')
    foreach ($i in 1..($Items - $folders - $shortcuts)) {
        $path = Join-Path $itemsDir ('File {0:D3}{1}' -f $i, $extensions[($i - 1) % $extensions.Count])
        [IO.File]::WriteAllBytes($path, [byte[]]@())
        $paths.Add($path)
    }

    # 10 tents in a 5 x 2 grid over the primary monitor (physical pixels), 20 items each. The monitor key is a
    # placeholder: Tents adopts the layout for the real monitors and saves it once (in the warm-up run).
    $size = [TentsIdleNative]::PrimarySize()
    $columns = [int][math]::Ceiling($Tents / 2)
    $cellW = [int][math]::Floor($size[0] / $columns)
    $cellH = [int][math]::Floor($size[1] * 0.45)
    $perTent = [int][math]::Ceiling($paths.Count / $Tents)
    $tentList = @(foreach ($t in 0..($Tents - 1)) {
        [ordered]@{
            id       = [guid]::NewGuid().ToString()
            title    = 'Tent {0}' -f ($t + 1)
            bounds   = [ordered]@{ x = ($t % $columns) * $cellW + 10; y = [int][math]::Floor($t / $columns) * $cellH + 10; width = $cellW - 20; height = $cellH - 20 }
            rolledUp = $false
            items    = @($paths | Select-Object -Skip ($t * $perTent) -First $perTent)
        }
    })
    $layout = [ordered]@{
        schemaVersion = 1
        layouts       = @([ordered]@{
            monitors      = @([ordered]@{ deviceId = 'IDLE-CHECK'; bounds = [ordered]@{ x = 0; y = 0; width = $size[0]; height = $size[1] } })
            defaultTentId = $tentList[0].id
            tents         = $tentList
        })
    }
    $layoutFile = Join-Path $root 'layout.json'
    [IO.File]::WriteAllText($layoutFile, ($layout | ConvertTo-Json -Depth 8), (New-Object Text.UTF8Encoding $false))
    [pscustomobject]@{ Items = $itemsDir; Layout = $layoutFile; Count = $paths.Count }
}

function Start-Tents([string] $name) {
    $log = Join-Path $work "$name.log"
    $arguments = "`"--desktop-folder=$($setup.Items)`" `"--layout-file=$($setup.Layout)`" `"--log-file=$log`""
    $process = Start-Process -FilePath $Exe -ArgumentList $arguments -PassThru
    $started = [Diagnostics.Stopwatch]::StartNew()
    $firstFrame = $null
    $read = $null
    while ($started.Elapsed.TotalSeconds -lt 30 -and -not $process.HasExited -and ($null -eq $firstFrame -or $null -eq $read)) {
        Start-Sleep -Milliseconds 50
        if (Test-Path $log) {
            $text = Get-Content $log -Raw -ErrorAction SilentlyContinue
            if ($text -match 'First frame: (\d+) tent\(s\) drawn (\d+) ms') { $firstFrame = [pscustomobject]@{ Tents = [int]$Matches[1]; Ms = [int]$Matches[2] } }
            if ($text -match 'Desktop read: (\d+) item\(s\) in (\d+) tent\(s\)') { $read = [pscustomobject]@{ Items = [int]$Matches[1]; Tents = [int]$Matches[2] } }
        }
    }

    if ($process.HasExited) { throw "Tents exited at once (exit code $($process.ExitCode)); see $log" }
    if ($null -eq $firstFrame -or $null -eq $read) { Stop-Tents $process; throw "Tents did not report its first frame and first read within 30 s; see $log" }
    [pscustomobject]@{ Process = $process; Log = $log; FirstFrame = $firstFrame; Read = $read }
}

function Stop-Tents($process) {
    if ($process.HasExited) { return }
    foreach ($hwnd in [TentsIdleNative]::Windows($process.Id, 'Tents controller', $false)) { [void][TentsIdleNative]::Close($hwnd) }
    if (-not $process.WaitForExit(10000)) {
        Write-Warning 'Tents did not exit on WM_CLOSE within 10 s; killing it (its tray icon may linger until the mouse passes over it).'
        $process.Kill()
        [void]$process.WaitForExit(5000)
    }
}

function Get-Sample([int] $id) {
    $at = [Diagnostics.Stopwatch]::GetTimestamp()
    $p = Get-Process -Id $id
    $p.Refresh()
    $threads = @(Get-CimInstance Win32_PerfRawData_PerfProc_Thread -Filter "IDProcess=$id")
    $proc = Get-CimInstance Win32_PerfRawData_PerfProc_Process -Filter "IDProcess=$id"
    # Where each thread started, to tell which code a waking thread belongs to (resolved to a module when reported).
    $starts = @{}
    foreach ($t in $threads) { $starts[[int]$t.IDThread] = [TentsIdleNative]::ThreadStart([int]$t.IDThread) }
    [pscustomobject]@{
        ProcessId      = $id
        At             = $at
        CpuMs          = $p.TotalProcessorTime.TotalMilliseconds
        Threads        = $threads
        Starts         = $starts
        WorkingSetMB   = $p.WorkingSet64 / 1MB
        PrivateWsMB    = $proc.WorkingSetPrivate / 1MB
        PrivateBytesMB = $p.PrivateMemorySize64 / 1MB
        Handles        = $p.HandleCount
        CommitMB       = $null
        ModuleCount    = $null
        WorkingSetByModule = $null
    }
}

$moduleCache = @{}
function Get-Modules([int] $id) {
    if (-not $moduleCache.ContainsKey($id)) {
        $moduleCache[$id] = @(try { (Get-Process -Id $id).Modules | ForEach-Object { [pscustomobject]@{ Name = $_.ModuleName; Base = [int64]$_.BaseAddress; Size = [int64]$_.ModuleMemorySize } } } catch { })
    }
    $moduleCache[$id]
}

function Get-StartName($sample, [int] $threadId) {
    if (-not $sample.Starts.ContainsKey($threadId) -or $sample.Starts[$threadId] -eq 0) { return '?' }
    $address = $sample.Starts[$threadId]
    foreach ($m in (Get-Modules $sample.ProcessId)) {
        if ($address -ge $m.Base -and $address -lt $m.Base + $m.Size) { return '{0}+0x{1:x}' -f $m.Name, ($address - $m.Base) }
    }
    return '0x{0:x}' -f $address
}

# Wakeups counted per thread, not as a difference of two sums (see spikes/03-perf-baseline/measure.ps1):
#   in both samples -> difference; only in B -> its whole count (created in the window); only in A -> exited, not
#   countable, reported (the total is then a lower bound).
function Measure-Window($a, $b) {
    $seconds = ($b.At - $a.At) / [Diagnostics.Stopwatch]::Frequency
    $before = @{}
    foreach ($t in $a.Threads) { $before[$t.IDThread] = $t.ContextSwitchesPersec }
    $seen = @{}
    $perThread = @(foreach ($t in $b.Threads) {
        $seen[$t.IDThread] = $true
        $d = if ($before.ContainsKey($t.IDThread)) { $t.ContextSwitchesPersec - $before[$t.IDThread] } else { $t.ContextSwitchesPersec }
        if ($d -lt 0) { $d = $t.ContextSwitchesPersec } # thread id reused
        [pscustomobject]@{ Id = [int]$t.IDThread; Wakeups = [long]$d }
    })
    $wakeups = ($perThread | Measure-Object Wakeups -Sum).Sum
    $exited = @($before.Keys | Where-Object { -not $seen.ContainsKey($_) }).Count
    [pscustomobject]@{
        Seconds         = [math]::Round($seconds, 1)
        CpuMsPer60s     = [math]::Round(($b.CpuMs - $a.CpuMs) * 60 / $seconds, 1)
        WakeupsPer60s   = [math]::Round($wakeups * 60 / $seconds, 1)
        ThreadsWoke     = @($perThread | Where-Object { $_.Wakeups -gt 0 }).Count
        Threads         = $b.Threads.Count
        ThreadsExited   = $exited
        PerThread       = @($perThread | Where-Object { $_.Wakeups -gt 0 } | Sort-Object Wakeups -Descending |
            ForEach-Object { '{0} (thread {1}, {2})' -f $_.Wakeups, $_.Id, (Get-StartName $b $_.Id) })
        WorkingSetMB    = [math]::Round($b.WorkingSetMB, 1)
        PrivateWsMB     = [math]::Round($b.PrivateWsMB, 1)
        PrivateBytesMB  = [math]::Round($b.PrivateBytesMB, 1)
        Handles         = $b.Handles
        CommitMB        = $b.CommitMB
        Modules         = $b.ModuleCount
        WorkingSetByModule = $b.WorkingSetByModule
    }
}

# Waits $settle seconds, sampling every 5 s so the results show when the process went quiet, then measures the idle
# window.
function Measure-Idle([int] $id, [int] $settle, [string] $etl) {
    $timeline = [System.Collections.Generic.List[object]]::new()
    $previous = Get-Sample $id
    $clock = [Diagnostics.Stopwatch]::StartNew()
    while ($clock.Elapsed.TotalSeconds -lt $settle - 0.5) {
        Start-Sleep -Seconds ([math]::Min(5, [math]::Max(1, $settle - $clock.Elapsed.TotalSeconds)))
        $next = Get-Sample $id
        $step = Measure-Window $previous $next
        $timeline.Add(('{0,3:N0} s: {1} wakeups/60s, CPU {2} ms/60s, top {3}' -f $clock.Elapsed.TotalSeconds, $step.WakeupsPer60s, $step.CpuMsPer60s, (@($step.PerThread) + 'none')[0]))
        $previous = $next
    }
    if ($etl) { & wpr -start CPU -filemode | Out-Host }
    $a = Get-Sample $id
    Start-Sleep -Seconds $IdleSeconds
    $b = Get-Sample $id
    $commit = [TentsIdleNative]::CommitByType($id)
    $b.CommitMB = [pscustomobject]@{ Private = [math]::Round($commit[0] / 1MB, 1); Image = [math]::Round($commit[1] / 1MB, 1); Mapped = [math]::Round($commit[2] / 1MB, 1) }
    $modules = @(Get-Modules $id | Sort-Object Base)
    $b.ModuleCount = $modules.Count
    $pages = [TentsIdleNative]::WorkingSetPages($id, [long[]]@($modules | ForEach-Object Base), [long[]]@($modules | ForEach-Object Size))
    $byModule = @(for ($i = 0; $i -lt $modules.Count; $i++) { [pscustomobject]@{ Name = $modules[$i].Name; MB = [math]::Round($pages[$i] * 4 / 1024, 1) } })
    $b.WorkingSetByModule = [pscustomobject]@{
        OutsideModulesPrivateMB = [math]::Round($pages[$modules.Count] * 4 / 1024, 1)
        OutsideModulesSharedMB  = [math]::Round($pages[$modules.Count + 1] * 4 / 1024, 1)
        ModulesMB               = [math]::Round((($pages | Select-Object -First $modules.Count) | Measure-Object -Sum).Sum * 4 / 1024, 1)
        Top                     = @($byModule | Sort-Object MB -Descending | Select-Object -First 25 | ForEach-Object { '{0} {1}' -f $_.MB, $_.Name })
    }
    if ($etl) { & wpr -stop $etl 'Tents idle check' | Out-Host }
    $window = Measure-Window $a $b
    $window | Add-Member -NotePropertyName SettleTimeline -NotePropertyValue $timeline.ToArray()
    $window
}

# GCs, timers and thread-pool work items over -CounterSeconds, from dotnet-counters' CSV. Returns $null when the tool is
# missing or the run produced nothing usable.
function Measure-Runtime([int] $id, [string] $name) {
    if ($SkipCounters) { return $null }
    $tool = Get-Command dotnet-counters -ErrorAction SilentlyContinue
    if (-not $tool) { Write-Warning 'dotnet-counters is not on PATH; GCs, timers and work items are not measured.'; return $null }
    $csv = Join-Path $OutDir "$name-counters.csv"
    $duration = [TimeSpan]::FromSeconds($CounterSeconds).ToString('dd\:hh\:mm\:ss')
    & $tool.Source collect --process-id $id --counters System.Runtime --refresh-interval 1 --duration $duration --format csv -o $csv 2>&1 | Out-Null
    if (-not (Test-Path $csv)) { Write-Warning "dotnet-counters wrote no file for $name."; return $null }
    $rows = @(Import-Csv $csv)
    if ($rows.Count -eq 0) { return $null }
    $valueColumn = @($rows[0].PSObject.Properties.Name | Where-Object { $_ -like 'Mean*' -or $_ -like '*Increment*' })[0]
    $col = 'Counter Name'

    # Monotonic counts arrive as per-interval increments (rates); summing them over the window gives the total.
    $gcs = ($rows | Where-Object { $_.$col -like 'dotnet.gc.collections*' -or $_.$col -like 'GC Count*' -or $_.$col -like 'Gen * GC Count*' } |
        ForEach-Object { [double]$_.$valueColumn } | Measure-Object -Sum).Sum
    $items = ($rows | Where-Object { $_.$col -like 'dotnet.thread_pool.work_item.count*' -or $_.$col -like 'ThreadPool Completed Work Item Count*' } |
        ForEach-Object { [double]$_.$valueColumn } | Measure-Object -Sum).Sum
    # The timer count is a gauge; the largest value seen counts.
    $timers = ($rows | Where-Object { $_.$col -like 'dotnet.timer.count*' -or $_.$col -like 'Number of Active Timers*' } |
        ForEach-Object { [double]$_.$valueColumn } | Measure-Object -Maximum).Maximum
    [pscustomobject]@{ GcCount = [double]$gcs; TimerCount = [double]$timers; WorkItems = [double]$items; Rows = $rows.Count }
}

function Test-Budget([string] $case, [string] $metric, $value, $budget, [string] $op = '<=') {
    $pass = if ($null -eq $value) { $null } elseif ($op -eq '<') { $value -lt $budget } else { $value -le $budget }
    $results.Add([pscustomobject]@{
        Case   = $case
        Metric = $metric
        Value  = if ($null -eq $value) { 'not measured' } else { $value }
        Budget = "$op $budget"
        Result = if ($null -eq $pass) { 'NOT MEASURED' } elseif ($pass) { 'pass' } else { 'FAIL' }
    })
}

function Add-IdleResults([string] $case, $window, $runtime) {
    Test-Budget $case 'CPU ms per 60 s' $window.CpuMsPer60s $Budget.CpuMsPer60s '<'
    Test-Budget $case 'Wakeups per 60 s' $window.WakeupsPer60s $Budget.WakeupsPer60s
    Test-Budget $case 'Working set MB' $window.WorkingSetMB $Budget.WorkingSetMB
    Test-Budget $case 'Private working set MB' $window.PrivateWsMB $Budget.PrivateWsMB
    Test-Budget $case 'Private bytes MB' $window.PrivateBytesMB $Budget.PrivateBytesMB
    $gc = if ($runtime) { $runtime.GcCount } else { $null }
    $timers = if ($runtime) { $runtime.TimerCount } else { $null }
    $work = if ($runtime) { $runtime.WorkItems } else { $null }
    Test-Budget $case "GCs in $CounterSeconds s" $gc $Budget.GcCount
    Test-Budget $case 'Timers (most seen)' $timers $Budget.TimerCount
    Test-Budget $case "Thread-pool work items in $CounterSeconds s" $work $Budget.WorkItems
}

# ---------------------------------------------------------------- run

if ($WprTrace) {
    $principal = New-Object Security.Principal.WindowsPrincipal ([Security.Principal.WindowsIdentity]::GetCurrent())
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw '-WprTrace needs an elevated PowerShell (WPR cannot record without one). This script does not elevate itself.'
    }
}

$running = @(Get-Process -Name Tents -ErrorAction SilentlyContinue)
if ($running.Count -gt 0) {
    throw "Tents is already running ($($running.Count) process(es)). Exit it from the tray first: one instance per session."
}

New-Item -ItemType Directory -Force $OutDir | Out-Null
$repo = Resolve-Path (Join-Path $PSScriptRoot '..\..')
if (-not $Exe) {
    $publishDir = Join-Path $PSScriptRoot 'artifacts\app'
    Write-Host "Publishing Tents (Release, framework-dependent, ReadyToRun) to $publishDir"
    & dotnet publish (Join-Path $repo 'src\Tents.App\Tents.App.csproj') -c Release -r win-x64 --self-contained false `
        -p:PublishReadyToRun=true -o $publishDir --nologo -v quiet | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }
    $Exe = Join-Path $publishDir 'Tents.exe'
}
$Exe = (Resolve-Path $Exe).Path

$work = [TentsIdleNative]::LongPath((New-Item -ItemType Directory (Join-Path ([IO.Path]::GetTempPath()) ("tents-idle-{0}" -f [guid]::NewGuid().ToString('N').Substring(0, 8)))).FullName)
$results = [System.Collections.Generic.List[object]]::new()
$details = [ordered]@{ Exe = $Exe; Started = (Get-Date).ToString('s') }
$process = $null
try {
    $setup = New-TypicalSetup $work
    Write-Host ("Typical setup: {0} items, {1} tents (synthetic, in a temporary folder)" -f $setup.Count, $Tents)

    # ---- startup: warm-up (also adopts and saves the layout), then the measured launches
    Write-Host 'Startup: warm-up launch'
    $run = Start-Tents 'warmup'
    Start-Sleep -Seconds 2 # let the debounced save of the adopted layout finish
    Stop-Tents $run.Process
    if ($run.Read.Items -ne $setup.Count -or $run.Read.Tents -ne $Tents) {
        throw "The setup did not load as built: Tents read $($run.Read.Items) item(s) in $($run.Read.Tents) tent(s)."
    }
    $starts = @(foreach ($i in 1..$StartupRuns) {
        Start-Sleep -Milliseconds 500
        $run = Start-Tents "startup-$i"
        Stop-Tents $run.Process
        Write-Host ("Startup {0}/{1}: {2} ms" -f $i, $StartupRuns, $run.FirstFrame.Ms)
        $run.FirstFrame.Ms
    })
    $details.StartupMs = $starts
    Test-Budget 'startup' 'Warm startup ms (median)' (Get-Median $starts) $Budget.StartupMs

    # ---- idle
    Write-Host ("Idle: settle {0} s, measure {1} s" -f $SettleSeconds, $IdleSeconds)
    $run = Start-Tents 'idle'
    $process = $run.Process
    $etl = if ($WprTrace) { Join-Path $OutDir 'idle.etl' } else { $null }
    $idle = Measure-Idle $process.Id $SettleSeconds $etl
    if ($etl) { $details.WprTrace = $etl; $details.ProcessId = $process.Id }
    $details.Idle = $idle
    Write-Host ("Idle: runtime counters for {0} s" -f $CounterSeconds)
    $idleRuntime = Measure-Runtime $process.Id 'idle'
    $details.IdleRuntime = $idleRuntime
    Add-IdleResults 'idle' $idle $idleRuntime

    # ---- resize, then idle again
    if (-not $SkipResize) {
        $windows = @([TentsIdleNative]::Windows($process.Id, 'Tents', $true))
        if ($windows.Count -eq 0) { throw 'No visible tent window found to resize.' }
        $hwnd = $windows[0]
        $from = [TentsIdleNative]::Rect($hwnd)
        $screen = [TentsIdleNative]::PrimarySize()
        $toW = [int][math]::Min($from[2] * 2, $screen[0] * 0.6)
        $toH = [int][math]::Min($from[3] * 2.6, $screen[1] * 0.85)
        function Step-Size([int[]] $start, [int] $width, [int] $height) {
            foreach ($s in 1..$ResizeSteps) {
                [TentsIdleNative]::Resize($hwnd, [int]($start[2] + ($width - $start[2]) * $s / $ResizeSteps), [int]($start[3] + ($height - $start[3]) * $s / $ResizeSteps))
                Start-Sleep -Milliseconds 16
            }
        }
        function Get-Memory($sample) {
            [pscustomobject]@{ WorkingSetMB = [math]::Round($sample.WorkingSetMB, 1); PrivateWsMB = [math]::Round($sample.PrivateWsMB, 1); PrivateBytesMB = [math]::Round($sample.PrivateBytesMB, 1) }
        }

        # Grow, then back to the typical size: the budgets then apply to the same setup as before, so whatever is
        # still above the idle numbers is memory the resize left behind. The grown tent's own cost is reported apart.
        Write-Host ("Resize: one tent from {0}x{1} to {2}x{3} px in {4} steps" -f $from[2], $from[3], $toW, $toH, $ResizeSteps)
        Step-Size $from $toW $toH
        $details.ResizeJustAfter = Get-Memory (Get-Sample $process.Id)
        Start-Sleep -Seconds 10 # the app gives memory back 5 s after the last activity
        $grown = Get-Memory (Get-Sample $process.Id)
        $grown | Add-Member -NotePropertyName Size -NotePropertyValue ('{0}x{1}' -f $toW, $toH)
        $details.Grown = $grown
        Write-Host ("Resize: grown and settled {0} s: working set {1} MB, private WS {2} MB, private bytes {3} MB (not a budget case: a bigger tent costs more)" -f 10, $grown.WorkingSetMB, $grown.PrivateWsMB, $grown.PrivateBytesMB)
        Step-Size ([TentsIdleNative]::Rect($hwnd)) $from[2] $from[3]
        Write-Host ("Resize: back to {0}x{1} px; settle {2} s, measure {3} s" -f $from[2], $from[3], $SettleSeconds, $IdleSeconds)
        $after = Measure-Idle $process.Id $SettleSeconds $null
        $details.AfterResize = $after
        $afterRuntime = Measure-Runtime $process.Id 'after-resize'
        $details.AfterResizeRuntime = $afterRuntime
        Add-IdleResults 'after resize' $after $afterRuntime
    }
}
finally {
    if ($process) { Stop-Tents $process }
    if (Test-Path $work) {
        Get-ChildItem $work -Filter *.log | Copy-Item -Destination $OutDir
        Remove-Item $work -Recurse -Force
    }
}

# ---------------------------------------------------------------- report

$details.Results = $results
$details | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $OutDir 'idle-check.json') -Encoding utf8
$md = @('| Case | Metric | Value | Budget | Result |', '|---|---|---|---|---|') +
    ($results | ForEach-Object { "| $($_.Case) | $($_.Metric) | $($_.Value) | $($_.Budget) | $($_.Result) |" })
$md | Set-Content (Join-Path $OutDir 'idle-check.md') -Encoding utf8
$results | Format-Table -AutoSize | Out-String -Width 200 | Out-Host
if ($details.Contains('Idle')) {
    Write-Host ("Committed at the end of the idle window (MB): private {0}, image {1}, mapped {2}; {3} modules" -f $details.Idle.CommitMB.Private, $details.Idle.CommitMB.Image, $details.Idle.CommitMB.Mapped, $details.Idle.Modules)
    $ws = $details.Idle.WorkingSetByModule
    Write-Host ("Working set (MB): modules {0}, outside modules private {1}, shared {2}; largest: {3}" -f $ws.ModulesMB, $ws.OutsideModulesPrivateMB, $ws.OutsideModulesSharedMB, (@($ws.Top | Select-Object -First 8) -join '; '))
    Write-Host ("Idle details: {0} of {1} threads woke, {2} exited, {3} handles; per thread: {4}" -f $details.Idle.ThreadsWoke, $details.Idle.Threads, $details.Idle.ThreadsExited, $details.Idle.Handles, (@($details.Idle.PerThread) -join '; '))
}
Write-Host "Results: $OutDir"

$failed = @($results | Where-Object { $_.Result -eq 'FAIL' -or ($_.Result -eq 'NOT MEASURED' -and -not $AllowUnmeasured) })
if ($failed.Count -gt 0) {
    Write-Host ("IDLE CHECK FAILED: {0} value(s) over budget or not measured." -f $failed.Count) -ForegroundColor Red
    exit 1
}
Write-Host 'Idle check passed.' -ForegroundColor Green
exit 0
