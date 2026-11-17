# THROWAWAY SPIKE SCRIPT for M0 spike 2 (desktop items). See docs/spikes/02-desktop-items.md.
#
# !! This script changes the real desktop for a short time. Read this header before running it. !!
#
#   -Scenario hide-listview   hides the desktop's icon list window (ShowWindow), draws the items in a prototype tent
#   -Scenario hide-noicons    turns off "Show desktop icons" through IFolderView2 (FWF_NOICONS), draws the items itself
#   -Scenario park            moves the native icons of scratch files to -ParkAt, draws those items itself
#   -ExplorerRestart          additionally KILLS explorer.exe once and lets Windows restart it
#
# What it touches:
#   * Icon visibility and, for "park", the positions of its own scratch icons. Your own icons are never moved.
#   * It creates files named tents-spike-*.txt on your Desktop and deletes exactly those at the end.
#   * It crashes and hard-kills the prototype on purpose to test recovery.
# Before anything else it saves icon visibility, arrange flags and all icon positions to <LogDir>\desktop-backup.json,
# and it restores from that file at the end, whatever happened.
#
# If icons ever stay hidden: right-click the desktop > View > Show desktop icons (toggle it off and on if it is
# already ticked), or restart Explorer from Task Manager. To restore from the backup by hand:
#   DesktopItemsSpike\bin\Release\net10.0-windows\desktopitems.exe restore <LogDir>\desktop-backup.json
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [ValidateSet('hide-listview', 'hide-noicons', 'park')] [string] $Scenario,
    [switch] $ExplorerRestart,
    [string] $ParkAt = '-3000,-3000',            # icon list client coordinates; off-screen by default
    [int[]] $EmptyDesktopPoint = @(2470, 750),   # a screen point over empty desktop, for the right-click test
    [string] $LogDir = (Join-Path $env:TEMP 'tents-spike2'),
    [switch] $IUnderstandThisChangesMyDesktop
)
$ErrorActionPreference = 'Stop'
if (-not $IUnderstandThisChangesMyDesktop) { throw 'Pass -IUnderstandThisChangesMyDesktop to confirm. Read the script header first.' }
$root = Split-Path -Parent $PSScriptRoot
$ctl = Join-Path $root 'common\SpikeCtl\bin\Release\net10.0-windows\spikectl.exe'
$exe = Join-Path $PSScriptRoot 'ItemsTentSpike\bin\Release\net10.0-windows\ItemsTentSpike.exe'
$di = Join-Path $PSScriptRoot 'DesktopItemsSpike\bin\Release\net10.0-windows\desktopitems.exe'
foreach ($f in $ctl, $exe, $di) { if (-not (Test-Path $f)) { throw "Build first (dotnet build -c Release): $f" } }
New-Item -ItemType Directory -Force $LogDir | Out-Null
$desk = [Environment]::GetFolderPath('DesktopDirectory')
$backup = Join-Path $LogDir 'desktop-backup.json'
$controller = 'TentsSpike-items-controller'

$results = [System.Collections.Generic.List[object]]::new()
function Check($name, $pass, $detail) {
    $r = if ($null -eq $pass) { 'INFO' } elseif ($pass) { 'PASS' } else { 'FAIL' }
    $results.Add([pscustomobject]@{ Check = $name; Result = $r; Detail = $detail })
}
function Info { (& $di info) -join ' ' }
function Vis { if ((Info) -match '(noIcons=\w+).*(listViewVisible=\w+) (HideIconsRegistry=\S+)') { "$($Matches[1]) $($Matches[2]) $($Matches[3])" } }
function IconsVisible { (Info) -match 'noIcons=False.*listViewVisible=True' }
function Scratch { (& $di items 'tents-spike-') -replace '^total=\d+ matching=\d+ ?', '' }
function ShellPid { if (((& $ctl tree) -join "`n") -match 'shellPid=(\d+)') { [int]$Matches[1] } else { 0 } }
function Start-Proto([string[]]$extra, $log) {
    Remove-Item $log -ErrorAction SilentlyContinue
    $p = Start-Process $exe -ArgumentList (@('--source=desktop', "--log=$log") + $extra) -PassThru
    Start-Sleep -Milliseconds 3000
    $p
}
function Stop-Proto($p) {
    & $ctl close $controller | Out-Null
    Start-Sleep -Milliseconds 1800
    if (-not $p.HasExited) { $p.Kill(); Start-Sleep -Milliseconds 1500; return $false }
    $true
}
function LogHas($log, $pattern) { @(Get-Content $log | Where-Object { $_ -cmatch $pattern }).Count -gt 0 }
function Restart-Explorer {
    $old = ShellPid
    Stop-Process -Id $old -Force   # Winlogon restarts the shell itself (AutoRestartShell)
    $sw = [Diagnostics.Stopwatch]::StartNew(); $new = 0
    while ($sw.Elapsed.TotalSeconds -lt 20) { Start-Sleep -Milliseconds 150; $new = ShellPid; if ($new -ne 0 -and $new -ne $old) { break } }
    if ($new -eq 0 -or $new -eq $old) { Write-Warning 'Shell did not restart by itself; starting explorer.exe'; Start-Process explorer.exe }
    Start-Sleep -Seconds 7
}

& $di backup $backup | Out-Null
$initial = Info
$snapshot = Join-Path $LogDir 'windows-before.txt'; & $ctl snapshot $snapshot | Out-Null
$p = $null
try {
    if ($Scenario -like 'hide-*') {
        $hide = $Scenario.Substring(5)
        $log = Join-Path $LogDir "$Scenario.log"
        $p = Start-Proto @("--hide=$hide", '--watchdog') $log
        Check 'native icons hidden while the prototype runs' (-not (IconsVisible)) (Vis)
        Check 'native view still lists its items while hidden' ((Info) -match 'items=[1-9]') ((Info) -replace ' folderFlags.*', '')
        if (LogHas $log 'onlyNative=') { Check 'namespace enumeration vs native view' $null ((Get-Content $log | Where-Object { $_ -cmatch 'refresh #1' }) -replace '.*\(start\): ', '' -replace ' scratch=.*', '') }

        & $ctl show-desktop | Out-Null
        $base = (& $ctl menu-open) -join ''
        & $ctl click $EmptyDesktopPoint[0] $EmptyDesktopPoint[1] right | Out-Null; Start-Sleep -Milliseconds 1300
        $menu = (& $ctl menu-open) -join ''
        & $ctl keys esc | Out-Null; Start-Sleep -Milliseconds 500
        & $ctl unshow-desktop | Out-Null
        Check 'desktop right-click menu still opens while hidden' ($menu -ne $base) "before: $base  after right-click: $menu"

        'x' | Set-Content (Join-Path $desk 'tents-spike-a.txt'); Start-Sleep -Milliseconds 1500
        Check 'new file appears in the prototype (no native icon)' (LogHas $log 'scratch=\[tents-spike-a') (Vis)
        Rename-Item (Join-Path $desk 'tents-spike-a.txt') 'tents-spike-b.txt'; Start-Sleep -Milliseconds 1500
        Check 'rename is reflected' (LogHas $log 'scratch=\[tents-spike-b') ''
        Remove-Item (Join-Path $desk 'tents-spike-b.txt'); Start-Sleep -Milliseconds 1500
        Check 'delete is reflected' (LogHas $log 'refresh #\d+ \(file system event\).* -1 .*scratch=\[\]') ''

        if ($ExplorerRestart) {
            Restart-Explorer
            $line = (Get-Content $log | Where-Object { $_ -cmatch 'TaskbarCreated: new shell state' }) -replace '.*new shell state: ', ''
            Check 'Explorer restart: state of the NEW shell before re-hiding' $null $line
            Check 'Explorer restart: native icons came back by themselves (duplicates until re-hidden)' $null ($line -match 'listViewVisible=True')
            Check 'Explorer restart: hidden again afterwards' (-not (IconsVisible)) (Vis)
        }
        $clean = Stop-Proto $p
        Check 'clean exit restores native icons' ($clean -and (IconsVisible)) (Vis)

        $log = Join-Path $LogDir "$Scenario-crash.log"
        $p = Start-Proto @("--hide=$hide") $log
        & $ctl post $controller 8001 | Out-Null; Start-Sleep -Milliseconds 3500
        Check 'unhandled exception: icons restored in-process' ($p.HasExited -and (IconsVisible)) (Vis)

        $p = Start-Proto @("--hide=$hide", '--watchdog') (Join-Path $LogDir "$Scenario-kill.log")
        $p.Kill(); Start-Sleep -Milliseconds 1500
        Check 'hard kill WITH watchdog: icons restored' (IconsVisible) (Vis)

        $p = Start-Proto @("--hide=$hide") (Join-Path $LogDir "$Scenario-kill-nowd.log")
        $p.Kill(); Start-Sleep -Milliseconds 1500
        Check 'hard kill WITHOUT watchdog: icons stay hidden (expected, shows why a watchdog is needed)' $null (Vis)
        $state = Join-Path $env:TEMP "tents-spike2-state-$($p.Id).json"
        & $di restore $state | Out-Null; Remove-Item $state -ErrorAction SilentlyContinue
        Check 'restore from the saved state file works' (IconsVisible) (Vis)
    }
    else {
        foreach ($n in 'a', 'b', 'c') { 'x' | Set-Content (Join-Path $desk "tents-spike-$n.txt") }
        Start-Sleep -Milliseconds 2500
        $placed = Scratch
        Check 'where Explorer puts new icons' $null $placed
        $log = Join-Path $LogDir 'park.log'
        $p = Start-Proto @('--filter=tents-spike-', "--park-at=$ParkAt", '--watchdog') $log
        $after = Scratch
        $want = "($ParkAt)"
        Check 'native icons stay at the requested park position' (@([regex]::Matches($after, '\(-?\d+,-?\d+\)') | Where-Object { $_.Value -ne $want }).Count -eq 0) "requested $want, 3 s later: $after"

        'x' | Set-Content (Join-Path $desk 'tents-spike-d.txt'); Start-Sleep -Milliseconds 3000
        $fs = Get-Content $log | Where-Object { $_ -cmatch '\[fsw\] created tents-spike-d' } | Select-Object -First 1
        $lv = Get-Content $log | Where-Object { $_ -cmatch 'tents-spike-d.txt: native icon' } | Select-Object -First 1
        $lag = if ($fs -and $lv) { [int]([datetime]::ParseExact($lv.Substring(0, 12), 'HH:mm:ss.fff', $null) - [datetime]::ParseExact($fs.Substring(0, 12), 'HH:mm:ss.fff', $null)).TotalMilliseconds } else { -1 }
        Check 'new file: time until its native icon could be moved' $null "$lag ms after the file system event (the native icon is visible at Explorer's default slot until then)"
        Rename-Item (Join-Path $desk 'tents-spike-a.txt') 'tents-spike-e.txt'; Start-Sleep -Milliseconds 2500
        Check 'rename: native icon after rename' $null (Scratch)
        Remove-Item (Join-Path $desk 'tents-spike-b.txt'); Start-Sleep -Milliseconds 2500

        if ($ExplorerRestart) {
            Restart-Explorer
            Check 'Explorer restart: native positions in the new shell' $null ((Get-Content $log | Where-Object { $_ -cmatch 'TaskbarCreated: native positions' }) -replace '.*filtered items: ', '')
        }
        $clean = Stop-Proto $p
        $back = Scratch
        Check 'clean exit puts surviving icons back where Explorer had them' ($clean -and $back -match [regex]::Escape(($placed -split ' ' | Where-Object { $_ -match 'spike-c' }))) "before: $placed  after exit: $back"

        $p = Start-Proto @('--filter=tents-spike-c', '--park-at=1663,1211') (Join-Path $LogDir 'park-crash.log')
        $before = Scratch
        & $ctl post $controller 8001 | Out-Null; Start-Sleep -Milliseconds 3500
        Check 'unhandled exception: moved icon put back in-process' ((Scratch) -eq $back) "moved: $before  after crash: $(Scratch)"

        $p = Start-Proto @('--filter=tents-spike-c', '--park-at=1663,1211', '--watchdog') (Join-Path $LogDir 'park-kill.log')
        $p.Kill(); Start-Sleep -Milliseconds 1500
        Check 'hard kill WITH watchdog: moved icon put back' ((Scratch) -eq $back) (Scratch)
    }
}
finally {
    if ($p -and -not $p.HasExited) { $p.Kill(); Start-Sleep -Milliseconds 1500 }
    Get-ChildItem $env:TEMP -Filter 'tents-spike2-state-*.json' | ForEach-Object { & $di restore $_.FullName | Out-Null; Remove-Item $_.FullName }
    Get-ChildItem $desk -Filter 'tents-spike-*.txt' | Remove-Item -Force
    Start-Sleep -Milliseconds 2000
    $restore = (& $di restore $backup) -join ' '
    & $ctl unshow-desktop | Out-Null
    & $ctl restore $snapshot | Out-Null
}
$final = Info
Check 'desktop is back to its starting state' ($final -eq $initial) $(if ($final -eq $initial) { $restore } else { "before: $initial`nafter:  $final" })
"=== spike 2: $Scenario$(if ($ExplorerRestart) { ' + Explorer restart' }) ==="
$results | Format-Table -AutoSize -Wrap | Out-String -Width 230
"logs and backup: $LogDir"
