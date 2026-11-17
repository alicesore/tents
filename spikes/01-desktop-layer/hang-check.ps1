# THROWAWAY SPIKE SCRIPT for M0 spike 1 (desktop layer). See docs/spikes/01-desktop-layer.md.
#
# Failure-mode test: what happens to the desktop while the tent's UI thread is hung for 8 seconds?
# A cross-process owner or parent attaches our input queue to the shell's desktop thread (see the write-up),
# so a hung tent could freeze desktop input. The script:
#   1. shows the desktop, hangs the prototype's UI thread, and clicks the tent (that click is now stuck in its queue)
#   2. right-clicks empty desktop next to the tent and checks whether the desktop's context menu opens
#   3. closes the menu with Esc and restores the windows
# Pick -Rect so that the point 150 px to the right of the tent is empty desktop.
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Mode,
    [int[]] $Rect = @(1700, 200, 420, 300),
    [string] $LogDir = (Join-Path $env:TEMP 'tents-spike1')
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$ctl = Join-Path $root 'common\SpikeCtl\bin\Release\net10.0-windows\spikectl.exe'
$exe = Join-Path $PSScriptRoot 'DesktopLayerSpike\bin\Release\net10.0-windows\DesktopLayerSpike.exe'
New-Item -ItemType Directory -Force $LogDir | Out-Null
$title = "TentsSpike-$Mode"; $log = Join-Path $LogDir "hang-$Mode.log"; Remove-Item $log -ErrorAction SilentlyContinue
$snapshot = Join-Path $LogDir 'windows-before-hang.txt'; & $ctl snapshot $snapshot | Out-Null
$cx = $Rect[0] + [int]($Rect[2] / 2); $cy = $Rect[1] + [int]($Rect[3] / 2); $dx = $Rect[0] + $Rect[2] + 150

$p = Start-Process $exe -ArgumentList "--mode=$Mode", "--title=$title", "--rect=$($Rect -join ',')", "--log=$log" -PassThru
Start-Sleep -Milliseconds 2000
try {
    & $ctl show-desktop | Out-Null
    $baseline = (& $ctl menu-open) -join ''
    # Control measurement: the desktop menu opens normally while the tent is healthy.
    & $ctl click $dx $cy right | Out-Null; Start-Sleep -Milliseconds 1200
    $healthy = (& $ctl menu-open) -join ''
    & $ctl keys esc | Out-Null; Start-Sleep -Milliseconds 600

    & $ctl post "$title-controller" 8002 | Out-Null      # WM_APP+2: sleep 8 s on the UI thread
    Start-Sleep -Milliseconds 500
    & $ctl click $cx $cy | Out-Null                       # input for the hung thread
    Start-Sleep -Milliseconds 300
    & $ctl click $dx $cy right | Out-Null                 # input for the shell's desktop thread
    Start-Sleep -Milliseconds 1500
    $during = (& $ctl menu-open) -join ''
    Start-Sleep -Milliseconds 7000                        # let the hang end
    $after = (& $ctl menu-open) -join ''
    & $ctl keys esc | Out-Null; Start-Sleep -Milliseconds 600
    "=== hang test: $Mode ==="
    "shell popups before any click          : $baseline"
    "desktop context menu while tent healthy : $healthy"
    "desktop context menu while tent hung    : $during   (same as baseline = desktop input was blocked by the hung tent)"
    "desktop context menu after hang ended   : $after"
    "tent got its click after the hang       : $(@(Get-Content $log | Where-Object { $_ -cmatch 'WM_LBUTTONDOWN' }).Count -gt 0)"
}
finally {
    & $ctl unshow-desktop | Out-Null
    & $ctl close "$title-controller" | Out-Null; Start-Sleep -Milliseconds 800
    if (-not $p.HasExited) { $p.Kill() }
    & $ctl restore $snapshot | Out-Null
}
