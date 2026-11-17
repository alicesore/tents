# THROWAWAY SPIKE SCRIPT for M0 spike 1 (desktop layer). See docs/spikes/01-desktop-layer.md.
#
# !! This script KILLS explorer.exe (the shell process that owns the desktop) and lets Windows restart it. !!
# Open File Explorer windows close, and a file copy running in Explorer is aborted. The taskbar and desktop
# disappear for a few seconds. Do not run it without reading this.
#
# It starts one prototype per mode (each with its own marker color), kills the shell, waits for the new shell,
# and reports whether each tent came back attached to the NEW desktop window and still survives Show Desktop.
[CmdletBinding()]
param(
    [string[]] $Modes = @('owned-pin', 'bottom-react2', 'child-defview'),
    [string] $LogDir = (Join-Path $env:TEMP 'tents-spike1-restart'),
    [int] $X = 1500, [int] $Y = 600,
    [switch] $IUnderstandThisKillsExplorer
)
$ErrorActionPreference = 'Stop'
if (-not $IUnderstandThisKillsExplorer) { throw 'Pass -IUnderstandThisKillsExplorer to confirm. This script kills explorer.exe.' }
$root = Split-Path -Parent $PSScriptRoot
$ctl = Join-Path $root 'common\SpikeCtl\bin\Release\net10.0-windows\spikectl.exe'
$exe = Join-Path $PSScriptRoot 'DesktopLayerSpike\bin\Release\net10.0-windows\DesktopLayerSpike.exe'
New-Item -ItemType Directory -Force $LogDir | Out-Null
$colors = @('FF00C8', '00C8FF', 'C8FF00', 'FF8000')
$snapshot = Join-Path $LogDir 'windows-before.txt'; & $ctl snapshot $snapshot | Out-Null

function ShellPid { if (((& $ctl tree) -join "`n") -match 'shellPid=(\d+)') { [int]$Matches[1] } else { 0 } }
function Brief($title, $color) {
    ((& $ctl state $title $color) | Where-Object { $_ -match '^(found|parent|z:|hitTest|centerPixel)' } | ForEach-Object { ($_ -replace ' style=\S+ ex=\S+', '') -replace 'markerPixelsOnScreen.*coverage', 'coverage' -replace ' markerBounds.*', '' }) -join "`n    "
}

$procs = @()
for ($i = 0; $i -lt $Modes.Count; $i++) {
    $m = $Modes[$i]; $log = Join-Path $LogDir "$m.log"; Remove-Item $log -ErrorAction SilentlyContinue
    $rect = "$($X + $i * 340),$Y,300,220"
    $procs += Start-Process $exe -ArgumentList "--mode=$m", "--title=TentsSpike-$m", "--rect=$rect", "--color=$($colors[$i])", "--log=$log" -PassThru
}
Start-Sleep -Milliseconds 2500
try {
    $oldPid = ShellPid
    "shell before: pid=$oldPid"
    for ($i = 0; $i -lt $Modes.Count; $i++) { "  [$($Modes[$i])] before:`n    $(Brief "TentsSpike-$($Modes[$i])" $colors[$i])" }

    "--- killing explorer.exe pid $oldPid at $(Get-Date -Format HH:mm:ss.fff) ---"
    # Process.Kill ends the process with a non-zero exit code other than 1, which Winlogon treats as a crash and
    # restarts the shell itself (AutoRestartShell), with the user's normal environment.
    Stop-Process -Id $oldPid -Force
    Start-Sleep -Milliseconds 700
    for ($i = 0; $i -lt $Modes.Count; $i++) {
        $alive = -not $procs[$i].HasExited
        "  [$($Modes[$i])] while shell is gone: processAlive=$alive state: $(((& $ctl state "TentsSpike-$($Modes[$i])") | Select-Object -First 1) -replace ' style=.*','')"
    }
    $newPid = 0; $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt 20) { Start-Sleep -Milliseconds 500; $newPid = ShellPid; if ($newPid -ne 0 -and $newPid -ne $oldPid) { break } }
    if ($newPid -eq 0 -or $newPid -eq $oldPid) {
        Write-Warning 'The shell did not restart by itself within 20 s. Starting explorer.exe.'
        Start-Process explorer.exe
        Start-Sleep -Seconds 6; $newPid = ShellPid
    }
    "shell after: pid=$newPid (back after $([int]$sw.Elapsed.TotalMilliseconds) ms)"
    Start-Sleep -Seconds 8   # let the new shell finish creating the taskbar and desktop

    for ($i = 0; $i -lt $Modes.Count; $i++) {
        $m = $Modes[$i]; $log = Join-Path $LogDir "$m.log"
        "  [$m] after restart: processAlive=$(-not $procs[$i].HasExited)`n    $(Brief "TentsSpike-$m" $colors[$i])"
        "    log: " + ((Get-Content $log | Where-Object { $_ -cmatch 'TaskbarCreated|WM_DESTROY|WM_NCDESTROY|Window.Closed|recreating|gen=|reparent|owner set|UNHANDLED' } | ForEach-Object { $_ -replace '^(\S+) ', '$1 ' }) -join "`n         ")
    }
    "--- Show Desktop after the restart ---"
    & $ctl show-desktop
    for ($i = 0; $i -lt $Modes.Count; $i++) { "  [$($Modes[$i])] desktop shown:`n    $(Brief "TentsSpike-$($Modes[$i])" $colors[$i])" }
    & $ctl unshow-desktop | Out-Null
    for ($i = 0; $i -lt $Modes.Count; $i++) { "  [$($Modes[$i])] desktop un-shown:`n    $(Brief "TentsSpike-$($Modes[$i])" $colors[$i])" }
}
finally {
    foreach ($m in $Modes) { & $ctl close "TentsSpike-$m-controller" | Out-Null }
    Start-Sleep -Milliseconds 1000
    foreach ($p in $procs) { if (-not $p.HasExited) { $p.Kill() } }
    & $ctl restore $snapshot | Out-Null
}
