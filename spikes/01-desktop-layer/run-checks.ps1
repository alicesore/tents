# THROWAWAY SPIKE SCRIPT for M0 spike 1 (desktop layer). See docs/spikes/01-desktop-layer.md.
#
# Runs one hosting mode of DesktopLayerSpike through the scriptable part of the checklist and prints a
# pass/fail line per check. It drives the real desktop: windows minimize and restore, Task View opens,
# and a temporary virtual desktop is created and removed. Keep hands off the keyboard and mouse while it runs.
#
# It does NOT restart Explorer, lock the session or change display settings. See the write-up for those steps.
#
#   .\run-checks.ps1 -Mode owned-bottom
#   .\run-checks.ps1 -Mode bottom-react -Rect 300,300,420,300
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Mode,
    [string] $Layered = 'none',
    [string] $Exe = '',                     # defaults to DesktopLayerSpike; pass Win32Tent.exe to test the bare Win32 window
    [int[]] $Rect = @(1700, 200, 420, 300), # x,y,w,h in physical pixels; pick a spot with no desktop icons
    [string] $LogDir = (Join-Path $env:TEMP 'tents-spike1'),
    [switch] $SkipVirtualDesktop,
    [switch] $KeepRunning
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$ctl = Join-Path $root 'common\SpikeCtl\bin\Release\net10.0-windows\spikectl.exe'
if (-not $Exe) { $Exe = Join-Path $PSScriptRoot 'DesktopLayerSpike\bin\Release\net10.0-windows\DesktopLayerSpike.exe' }
foreach ($f in $ctl, $Exe) { if (-not (Test-Path $f)) { throw "Build first (dotnet build -c Release): $f" } }
New-Item -ItemType Directory -Force $LogDir | Out-Null

$title = "TentsSpike-$Mode"
$log = Join-Path $LogDir "$Mode-$Layered.log"
Remove-Item $log -ErrorAction SilentlyContinue
$cx = $Rect[0] + [int]($Rect[2] / 2); $cy = $Rect[1] + [int]($Rect[3] / 2)
$marker = 'FF00C8'

function Get-State {
    $s = @{}
    foreach ($line in (& $ctl state $title $marker)) {
        foreach ($m in [regex]::Matches($line, '(\w+)=("[^"]*"|\([^)]*\)[^ ]*|\S+)')) { $s[$m.Groups[1].Value] = $m.Groups[2].Value }
        if ($line -match '^hitTest\([^)]*\)=(.*)$') { $s['hit'] = $Matches[1] }
        if ($line -match 'coverage=([\d.]+)%') { $s['coverage'] = [double]$Matches[1] }
    }
    $s
}
$results = [System.Collections.Generic.List[object]]::new()
function Check($name, [bool]$pass, $detail) {
    $results.Add([pscustomobject]@{ Check = $name; Result = $(if ($pass) { 'PASS' } else { 'FAIL' }); Detail = $detail })
}
function Settle($ms = 1500) { Start-Sleep -Milliseconds $ms }
function LogLines($pattern) { @(Get-Content $log | Where-Object { $_ -cmatch $pattern }) }

# Safety net: remember which windows are shown now, and un-minimize exactly those at the end if a
# Show Desktop toggle got out of sync during the run.
$snapshot = Join-Path $LogDir 'windows-before.txt'
& $ctl snapshot $snapshot | Out-Null

$p = Start-Process $Exe -ArgumentList "--mode=$Mode", "--layered=$Layered", "--title=$title", "--rect=$($Rect -join ',')", "--log=$log" -PassThru
Settle 2000
try {
    # 1. Initial placement: is the tent behind every normal application window?
    $s = Get-State
    Check 'created' ($s.found -eq '1' -and $s.visible -eq '1') "parent=$($s.parent) owner=$($s.owner)"
    Check 'behind normal windows at start' ($s.otherAppWindowsBelow -eq '0') "appWindowsAbove=$($s.otherAppWindowsAbove) appWindowsBelow=$($s.otherAppWindowsBelow)"

    # 2. Show Desktop (Shell.ToggleDesktop, the documented automation equivalent of the Show Desktop button).
    #    Show Desktop is a toggle, so spikectl toggles and then verifies the outcome (see spikectl show-desktop).
    $phase = (& $ctl show-desktop) -join ' '
    $s = Get-State
    Check 'Show Desktop: tent visible' ($s.coverage -gt 90) "coverage=$($s.coverage)% hit=$($s.hit) ($phase)"
    Check 'Show Desktop: tent receives hit-test' ($s.hit -eq 'TENT') "hit=$($s.hit)"
    $before = (LogLines 'WM_LBUTTONDOWN').Count
    & $ctl click $cx $cy | Out-Null; Settle 600
    $s2 = Get-State
    Check 'Show Desktop: click reaches tent' ((LogLines 'WM_LBUTTONDOWN').Count -gt $before) "foregroundAfter=$($s2.foreground)"
    Check 'Show Desktop: click does not activate tent' ((LogLines 'WM_ACTIVATE [12]').Count -eq 0) ''
    $phase = (& $ctl unshow-desktop) -join ' '
    $s = Get-State
    Check 'after restore: desktop state left cleanly' ($phase -match 'desktopShown=0 togglesUsed=1') $phase
    Check 'after restore: behind normal windows again' ($s.otherAppWindowsBelow -eq '0') "appWindowsAbove=$($s.otherAppWindowsAbove) appWindowsBelow=$($s.otherAppWindowsBelow)"

    # 3. Win+D as real keyboard input (should match step 2).
    $phase = (& $ctl show-desktop keys) -join ' '
    $s = Get-State
    Check 'Win+D (keyboard): tent visible' ($s.coverage -gt 90) "coverage=$($s.coverage)% ($phase)"
    & $ctl unshow-desktop keys | Out-Null
    $s = Get-State
    Check 'Win+D again: behind normal windows again' ($s.otherAppWindowsBelow -eq '0') "appWindowsAbove=$($s.otherAppWindowsAbove) appWindowsBelow=$($s.otherAppWindowsBelow)"

    # 4. Minimize All (Win+M) and undo.
    & $ctl minimize-all | Out-Null; Settle
    $s = Get-State
    Check 'Minimize All: tent visible' ($s.coverage -gt 90) "coverage=$($s.coverage)% iconic=$($s.iconic)"
    & $ctl undo-minimize-all | Out-Null; Settle

    # 5. Task View (Win+Tab). The tent must not be listed as a window and must be unchanged afterwards.
    & $ctl keys win+tab | Out-Null; Settle 2000
    $scan = (& $ctl colorscan $marker) -join ' '
    & $ctl keys esc | Out-Null; Settle
    $s = Get-State
    Check 'Task View: tent state unchanged afterwards' ($s.found -eq '1' -and $s.visible -eq '1' -and $s.iconic -eq '0') "during Task View: $scan"

    # 6. Virtual desktops: create a new one (Ctrl+Win+D), look, close it again (Ctrl+Win+F4).
    if (-not $SkipVirtualDesktop) {
        & $ctl keys ctrl+win+d | Out-Null; Settle 2000
        $s = Get-State
        Check 'new virtual desktop: tent visible there' ($s.coverage -gt 90) "coverage=$($s.coverage)% cloaked=$($s.cloaked) onCurrent=$($s.onCurrent) desktopId=$($s.desktopId)"
        & $ctl keys ctrl+win+f4 | Out-Null; Settle 2000
        $s = Get-State
        Check 'back on first virtual desktop: tent intact' ($s.found -eq '1' -and $s.visible -eq '1' -and $s.cloaked -eq '0') "cloaked=$($s.cloaked)"
    }

    # 7. Clicking the tent must not raise it above a normal window that overlaps it.
    #    Clear the screen with Show Desktop, open a second (cyan) top-level window over the right half of the tent,
    #    click the exposed left part of the tent, and check the cyan window still covers the right half.
    $coverExe = Join-Path $PSScriptRoot 'Win32Tent\bin\Release\net10.0-windows\Win32Tent.exe'
    if (Test-Path $coverExe) {
        & $ctl show-desktop | Out-Null
        $half = [int]($Rect[2] / 2)
        $cover = Start-Process $coverExe -ArgumentList '--mode=plain', '--title=TentsSpike-cover', '--color=00C8FF', "--rect=$($Rect[0] + $half),$($Rect[1]),$($Rect[2]),$($Rect[3])" -PassThru
        Settle 1200
        $a = Get-State
        $before = (LogLines 'WM_LBUTTONDOWN').Count
        & $ctl click ($Rect[0] + 40) $cy | Out-Null; Settle 800
        $b = Get-State
        $clicked = (LogLines 'WM_LBUTTONDOWN').Count -gt $before
        Check 'click does not raise tent over a normal window' ($clicked -and $a.coverage -lt 60 -and $b.coverage -lt 60 -and $b.coverage -gt 30) "tent coverage before click=$($a.coverage)% after=$($b.coverage)% clickReached=$clicked"
        & $ctl close 'TentsSpike-cover-controller' | Out-Null; Settle 500
        if (-not $cover.HasExited) { $cover.Kill() }
        & $ctl unshow-desktop | Out-Null
    }
}
finally {
    if (-not $KeepRunning) {
        & $ctl close "$title-controller" | Out-Null; Settle 800
        if (-not $p.HasExited) { $p.Kill(); Write-Warning 'prototype did not exit on WM_CLOSE; killed' }
    }
    $restored = (& $ctl restore $snapshot) -join ' '
    if ($restored -notmatch ' 0 window') { Write-Warning "window arrangement was out of sync after the run; $restored" }
}
"=== $Mode / layered=$Layered ($(Split-Path -Leaf $Exe)) ==="
$results | Format-Table -AutoSize -Wrap | Out-String -Width 220
"log: $log"
