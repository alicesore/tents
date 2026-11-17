# THROWAWAY SPIKE SCRIPT for M0 spike 3 (performance baseline). See docs/spikes/03-performance-baseline.md.
#
# Publishes every measured variant of the two test apps into .\artifacts\<variant>\ (git-ignored).
# All variants are win-x64 and framework-dependent unless the name says otherwise.
[CmdletBinding()]
param([string[]] $Only)
$ErrorActionPreference = 'Stop'
$out = Join-Path $PSScriptRoot 'artifacts'
$wpf = Join-Path $PSScriptRoot 'WpfTent\WpfTent.csproj'
$w32 = Join-Path $PSScriptRoot 'Win32Tent\Win32Tent.csproj'
$nonconc = '-p:ConcurrentGarbageCollection=false'
$r2r = '-p:PublishReadyToRun=true'

$variants = [ordered]@{
    'wpf-default'         = @($wpf, '--self-contained', 'false')
    'wpf-nonconc'         = @($wpf, '--self-contained', 'false', $nonconc)
    'wpf-r2r'             = @($wpf, '--self-contained', 'false', $r2r)
    'wpf-nonconc-r2r'     = @($wpf, '--self-contained', 'false', $nonconc, $r2r)
    'wpf-nonconc-r2r-tc0' = @($wpf, '--self-contained', 'false', $nonconc, $r2r, '-p:TieredCompilation=false')
    'wpf-nonconc-r2r-pgo0' = @($wpf, '--self-contained', 'false', $nonconc, $r2r, '-p:TieredPGO=false')
    'wpf-nonconc-r2r-sc'  = @($wpf, '--self-contained', 'true', $nonconc, $r2r)
    'wpf-trimmed'         = @($wpf, '--self-contained', 'true', $nonconc, $r2r, '-p:PublishTrimmed=true')   # expected to be refused by the SDK
    'win32-clr'           = @($w32, '--self-contained', 'false', $nonconc, $r2r)
    'win32-aot'           = @($w32, '-p:PublishAot=true')
}
foreach ($name in $variants.Keys) {
    if ($Only -and $Only -notcontains $name) { continue }
    $dir = Join-Path $out $name
    Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue
    $a = $variants[$name]
    $log = & dotnet publish $a[0] -c Release -r win-x64 -o $dir --nologo -v q @($a[1..($a.Count - 1)]) 2>&1
    if ($LASTEXITCODE -ne 0) {
        $err = ($log | Where-Object { $_ -match 'error' } | Select-Object -First 1) -replace '\s*\[.*$', ''
        "{0,-22} PUBLISH FAILED: {1}" -f $name, $err
        continue
    }
    $files = Get-ChildItem $dir -Recurse -File
    "{0,-22} ok   {1,4} files  {2,7:N1} MB" -f $name, $files.Count, (($files | Measure-Object Length -Sum).Sum / 1MB)
}
