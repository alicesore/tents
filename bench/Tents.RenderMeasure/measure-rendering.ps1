<#
.SYNOPSIS
    M1 step 12: measures the interactive cost of tents in software and hardware rendering, with and without
    AllowsTransparency, at the current display scale.

.DESCRIPTION
    Builds Tents.RenderMeasure and runs it once per configuration and repetition. Each run shows two tent windows on the
    primary monitor for about 15 seconds, hosted like the app's (behind every other window, owned by the desktop), with
    synthetic items: empty files in a temporary folder, deleted afterwards. It does not read or change the desktop's
    items and does not touch the native desktop icons. Moving the mouse over the tents during a run disturbs the hover
    numbers; leave the mouse alone.

    Run it once at 100% scaling and once at 200% (Settings > Display > Scale), then compare. Results are JSON lines,
    one per scenario, plus a summary table of medians.

.EXAMPLE
    .\bench\Tents.RenderMeasure\measure-rendering.ps1 -Repetitions 3
#>
param(
    [int] $Repetitions = 3,
    [int] $Items = 200,
    [int] $Steps = 120,
    [string] $Output = (Join-Path $env:TEMP ("tents-render-{0:yyyyMMdd-HHmmss}.jsonl" -f (Get-Date)))
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
dotnet build (Join-Path $PSScriptRoot 'Tents.RenderMeasure.csproj') -c Release --nologo -v quiet | Out-Host
$exe = Join-Path $PSScriptRoot 'bin\Release\net10.0-windows\Tents.RenderMeasure.exe'

$configs = @(
    @{ Name = 'software'; Args = @() },
    @{ Name = 'software, transparent'; Args = @('--transparent') },
    @{ Name = 'hardware'; Args = @('--hardware') },
    @{ Name = 'hardware, transparent'; Args = @('--hardware', '--transparent') }
)

foreach ($rep in 1..$Repetitions) {
    foreach ($config in $configs) {
        Write-Host ("run {0}/{1}: {2}" -f $rep, $Repetitions, $config.Name)
        $arguments = $config.Args + @("--items=$Items", "--steps=$Steps", "--out=$Output")
        $process = Start-Process -FilePath $exe -ArgumentList $arguments -PassThru -Wait
        if ($process.ExitCode -ne 0) {
            Write-Warning "run failed (exit $($process.ExitCode)); see $Output.error.txt"
        }
    }
}

$rows = Get-Content $Output | ForEach-Object { $_ | ConvertFrom-Json }
function Median($values) {
    $sorted = @($values | Sort-Object)
    if ($sorted.Count -eq 0) { return $null }
    return $sorted[[int][Math]::Floor(($sorted.Count - 1) / 2)]
}

$rows | Group-Object Mode, Transparent, Dpi, Scenario | ForEach-Object {
    $first = $_.Group[0]
    [pscustomobject]@{
        Mode         = $first.Mode
        Transparent  = $first.Transparent
        Dpi          = $first.Dpi
        Scenario     = $first.Scenario
        Runs         = $_.Count
        WallMs       = Median ($_.Group | ForEach-Object WallMs)
        CpuMs        = Median ($_.Group | ForEach-Object CpuMs)
        Frames       = Median ($_.Group | ForEach-Object Frames)
        CpuPerFrame  = Median ($_.Group | ForEach-Object CpuMsPerFrame)
        PrivateMB    = Median ($_.Group | ForEach-Object PrivateBytesMB)
    }
} | Sort-Object Scenario, Mode, Transparent | Format-Table -AutoSize | Out-Host

Write-Host "Results: $Output"
