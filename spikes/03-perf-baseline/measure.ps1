# THROWAWAY SPIKE SCRIPT for M0 spike 3 (performance baseline). See docs/spikes/03-performance-baseline.md.
#
# Measures every published variant (run publish-variants.ps1 first) and writes results.csv and results.md.
# Needs no elevation. Close heavy applications first and do not use the machine while it runs (about 35 minutes).
#
# Per row:
#   Startup    1 discarded warm-up launch, then -StartupRuns launches. The app itself reports the time from process
#              creation to its first rendered frame (WPF ContentRendered / first WM_PAINT). Warm starts only.
#              -StartupRuns 0 skips the startup part (idle only).
#   Idle       launch, wait -SettleSeconds, sample, wait -IdleSeconds, sample again. Reported:
#                CPU ms          process CPU time used in the idle window (kernel + user)
#                Ctx switches    sum over all threads of the context-switch count in the idle window. A thread that is
#                                asleep does not switch, so this is the number of thread wakeups.
#                Working set, private working set, private bytes (commit), threads, handles, loaded modules
#   Counters   for selected rows, 12 s of dotnet-counters (System.Runtime) after the idle window, saved as CSV.
[CmdletBinding()]
param(
    [string] $OutDir = (Join-Path $PSScriptRoot 'artifacts\results'),
    [int] $StartupRuns = 7,
    [int] $SettleSeconds = 30,
    [int] $IdleSeconds = 60,
    [string[]] $Only
)
$ErrorActionPreference = 'Stop'
$art = Join-Path $PSScriptRoot 'artifacts'
New-Item -ItemType Directory -Force $OutDir | Out-Null
$marker = Join-Path $OutDir 'startup-marker.txt'
$ctl = Join-Path (Split-Path -Parent $PSScriptRoot) 'common\SpikeCtl\bin\Release\net10.0-windows\spikectl.exe'

# name, variant folder, exe, arguments, collect dotnet-counters?
$typical = @('--tents=10', '--items=20')
$rows = @(
    @('wpf default',                              'wpf-default',          'WpfTent.exe',   @(),                        $true),
    @('wpf non-concurrent GC',                    'wpf-nonconc',          'WpfTent.exe',   @(),                        $false),
    @('wpf ReadyToRun',                           'wpf-r2r',              'WpfTent.exe',   @(),                        $false),
    @('wpf non-concurrent GC + R2R',              'wpf-nonconc-r2r',      'WpfTent.exe',   @(),                        $true),
    @('wpf nc+R2R, TieredCompilation off',        'wpf-nonconc-r2r-tc0',  'WpfTent.exe',   @(),                        $false),
    @('wpf nc+R2R, TieredPGO off',                'wpf-nonconc-r2r-pgo0', 'WpfTent.exe',   @(),                        $false),
    @('wpf nc+R2R, self-contained',               'wpf-nonconc-r2r-sc',   'WpfTent.exe',   @(),                        $false),
    @('wpf nc+R2R, software rendering',           'wpf-nonconc-r2r',      'WpfTent.exe',   @('--software'),            $false),
    @('wpf nc+R2R, AllowsTransparency',           'wpf-nonconc-r2r',      'WpfTent.exe',   @('--transparent'),         $false),
    @('wpf nc+R2R, working set trimmed once',     'wpf-nonconc-r2r',      'WpfTent.exe',   @('--trim'),                $false),
    @('win32 on CoreCLR (nc+R2R)',                'win32-clr',            'Win32Tent.exe', @(),                        $false),
    @('win32 on CoreCLR, working set trimmed',    'win32-clr',            'Win32Tent.exe', @('--trim'),                $false),
    @('win32 NativeAOT',                          'win32-aot',            'Win32Tent.exe', @(),                        $false),
    @('TYPICAL wpf default',                      'wpf-default',          'WpfTent.exe',   $typical,                   $false),
    @('TYPICAL wpf nc+R2R',                       'wpf-nonconc-r2r',      'WpfTent.exe',   $typical,                   $true),
    @('TYPICAL wpf nc+R2R, software rendering',   'wpf-nonconc-r2r',      'WpfTent.exe',   ($typical + '--software'),  $false),
    @('TYPICAL wpf nc+R2R, trimmed once',         'wpf-nonconc-r2r',      'WpfTent.exe',   ($typical + '--trim'),      $false),
    @('TYPICAL win32 on CoreCLR',                 'win32-clr',            'Win32Tent.exe', $typical,                   $false),
    @('TYPICAL win32 NativeAOT',                  'win32-aot',            'Win32Tent.exe', $typical,                   $false)
)

function Launch($exe, $appArgs) {
    Remove-Item $marker -ErrorAction SilentlyContinue
    $p = Start-Process $exe -ArgumentList (@("--marker=$marker") + $appArgs) -PassThru
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt 30) {
        if ((Test-Path $marker) -and (Get-Item $marker).Length -gt 0) { break }
        Start-Sleep -Milliseconds 10
    }
    $ms = if (Test-Path $marker) { [int](Get-Content $marker -Raw) } else { -1 }
    @{ Process = $p; StartupMs = $ms }
}
function Stop-App($p) {
    if (Test-Path $ctl) { & $ctl close 'TentsSpike-perf-controller' | Out-Null; if ($p.WaitForExit(3000)) { return } }
    if (-not $p.HasExited) { $p.Kill(); $p.WaitForExit(3000) | Out-Null }
}
function Sample($id) {
    $p = Get-Process -Id $id
    $threads = @(Get-CimInstance Win32_PerfRawData_PerfProc_Thread -Filter "IDProcess=$id")
    $proc = Get-CimInstance Win32_PerfRawData_PerfProc_Process -Filter "IDProcess=$id"
    [pscustomobject]@{
        CpuMs = $p.TotalProcessorTime.TotalMilliseconds
        Ctx = ($threads | Measure-Object ContextSwitchesPersec -Sum).Sum
        PerThread = @{}; ThreadList = $threads
        WsMB = $p.WorkingSet64 / 1MB; PrivWsMB = $proc.WorkingSetPrivate / 1MB; PrivMB = $p.PrivateMemorySize64 / 1MB
        Threads = $p.Threads.Count; Handles = $p.HandleCount; Modules = $p.Modules.Count
    }
}
function Median($v) { $s = @($v | Sort-Object); $s[[int][math]::Floor(($s.Count - 1) / 2)] }

$results = [System.Collections.Generic.List[object]]::new()
foreach ($r in $rows) {
    $name, $dir, $exeName, $appArgs, $counters = $r
    if ($Only -and -not ($Only | Where-Object { $name -like $_ })) { continue }
    $exe = Join-Path (Join-Path $art $dir) $exeName
    if (-not (Test-Path $exe)) { Write-Warning "skipped (not published): $name"; continue }
    Write-Host "measuring: $name"

    # --- startup
    $starts = @()
    if ($StartupRuns -gt 0) {
        $l = Launch $exe $appArgs; Stop-App $l.Process; Start-Sleep -Milliseconds 300   # warm-up, discarded
        $starts = @(foreach ($i in 1..$StartupRuns) { $l = Launch $exe $appArgs; $l.StartupMs; Stop-App $l.Process; Start-Sleep -Milliseconds 300 })
    }

    # --- idle
    $l = Launch $exe $appArgs; $p = $l.Process
    Start-Sleep -Seconds $SettleSeconds
    $a = Sample $p.Id
    Start-Sleep -Seconds $IdleSeconds
    $b = Sample $p.Id
    # which threads woke up, and how often
    # Counted per thread, not as a difference of the two sums: a thread that exits inside the window would otherwise
    # take its whole lifetime count out of the second sum and make the difference negative.
    #   thread in both samples   -> difference of its counter
    #   thread only in sample B  -> its whole counter (it was created inside the window)
    #   thread only in sample A  -> not countable (it exited); reported as "Threads exited". Its wakeups before
    #                               exiting are missed, so the total is a lower bound by at least one per exited thread.
    $before = @{}; foreach ($t in $a.ThreadList) { $before[$t.IDThread] = $t.ContextSwitchesPersec }
    $seen = @{}
    $perThread = @(foreach ($t in $b.ThreadList) {
        $seen[$t.IDThread] = $true
        $d = if ($before.ContainsKey($t.IDThread)) { $t.ContextSwitchesPersec - $before[$t.IDThread] } else { $t.ContextSwitchesPersec }
        if ($d -lt 0) { $d = $t.ContextSwitchesPersec }   # thread id reused
        [long]$d
    })
    $ctx = ($perThread | Measure-Object -Sum).Sum
    $exited = @($before.Keys | Where-Object { -not $seen.ContainsKey($_) }).Count
    $awake = @($perThread | Where-Object { $_ -gt 0 })
    "{0}: per-thread wakeups {1}; exited {2}" -f $name, (($perThread | Sort-Object -Descending) -join ','), $exited |
        Add-Content (Join-Path $OutDir 'per-thread.log')

    if ($counters) {
        $csv = Join-Path $OutDir (($name -replace '[^\w]+', '-') + '-counters.csv')
        Remove-Item $csv -ErrorAction SilentlyContinue
        & dotnet-counters collect --process-id $p.Id --counters System.Runtime --duration 00:00:00:12 --format csv -o $csv 2>&1 | Out-Null
    }
    Stop-App $p

    $results.Add([pscustomobject]@{
        Variant = $name
        'Startup ms (median)' = if ($starts.Count) { Median $starts } else { 'not run' }
        'Startup min-max' = if ($starts.Count) { "$(($starts | Measure-Object -Minimum).Minimum)-$(($starts | Measure-Object -Maximum).Maximum)" } else { '' }
        "CPU ms / $IdleSeconds s" = [math]::Round($b.CpuMs - $a.CpuMs, 1)
        "Ctx switches / $IdleSeconds s" = [int]$ctx
        'Threads that woke' = "$($awake.Count) of $($b.Threads)"
        'Threads exited' = $exited
        'Working set MB' = [math]::Round($b.WsMB, 1)
        'Private WS MB' = [math]::Round($b.PrivWsMB, 1)
        'Private bytes MB' = [math]::Round($b.PrivMB, 1)
        Handles = $b.Handles
        Modules = $b.Modules
    })
    $results | Export-Csv (Join-Path $OutDir 'results.csv') -NoTypeInformation
}

$cols = $results[0].PSObject.Properties.Name
$md = @("| $($cols -join ' | ') |", "|$(($cols | ForEach-Object { '---' }) -join '|')|") + ($results | ForEach-Object { $row = $_; "| $(($cols | ForEach-Object { $row.$_ }) -join ' | ') |" })
$md | Set-Content (Join-Path $OutDir 'results.md') -Encoding utf8
$results | Format-Table -AutoSize | Out-String -Width 260
"results: $OutDir"
