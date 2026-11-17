# THROWAWAY SPIKE SCRIPT for M0 spike 3 (performance baseline). See docs/spikes/03-performance-baseline.md.
#
# NOT RUN during the spike: Windows Performance Recorder needs an elevated prompt, and the spike ran without one
# ("Failed to enable the policy to profile system performance", error 0xc5585011).
#
# Run this from an ELEVATED PowerShell to confirm the idle wakeup numbers with ETW:
#   .\wpr-idle.ps1 -Variant wpf-nonconc-r2r -AppArgs '--tents=10','--items=20'
# It records 60 s of context-switch and CPU-sampling events while the app idles, and saves an .etl file.
# Open the file in Windows Performance Analyzer, add "CPU Usage (Precise)", filter to the WpfTent.exe / Win32Tent.exe
# process, and read "Count" (context switches = wakeups) and "CPU Usage (ms)" per thread. The "Ready Thread Stack"
# column shows what woke each thread.
[CmdletBinding()]
param(
    [string] $Variant = 'wpf-nonconc-r2r',
    [string[]] $AppArgs = @(),
    [int] $SettleSeconds = 30,
    [int] $IdleSeconds = 60,
    [string] $OutDir = (Join-Path $PSScriptRoot 'artifacts\results')
)
$ErrorActionPreference = 'Stop'
$admin = (New-Object Security.Principal.WindowsPrincipal ([Security.Principal.WindowsIdentity]::GetCurrent())).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $admin) { throw 'Run this from an elevated PowerShell. WPR cannot record without elevation.' }
$dir = Join-Path $PSScriptRoot "artifacts\$Variant"
$exe = Get-ChildItem $dir -Filter '*Tent.exe' | Select-Object -First 1
if (-not $exe) { throw "Not published: $dir (run publish-variants.ps1)" }
New-Item -ItemType Directory -Force $OutDir | Out-Null
$etl = Join-Path $OutDir "idle-$Variant.etl"

$p = Start-Process $exe.FullName -ArgumentList $AppArgs -PassThru
try {
    Start-Sleep -Seconds $SettleSeconds
    wpr -start CPU -filemode
    Start-Sleep -Seconds $IdleSeconds
    wpr -stop $etl "Tents spike 3 idle trace: $Variant"
}
finally {
    if (-not $p.HasExited) { $p.Kill() }
}
"trace: $etl  (process id was $($p.Id))"
