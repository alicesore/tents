<#
.SYNOPSIS
    Builds the portable release zip of Tents (M1 step 24).

.DESCRIPTION
    Publishes src/Tents.App framework-dependent for win-x64 with ReadyToRun; Workstation non-concurrent GC comes from the
    project (ConcurrentGarbageCollection=false). Zips the result with the license and a short read-me, and prints the
    zip's size and SHA-256. It builds and packs only: it does not run Tents, push a tag or publish a release.

    The zip needs the .NET 10 Desktop Runtime (x64) on the machine that runs it.

.EXAMPLE
    .\scripts\publish.ps1 -Version 0.1.0
#>
param(
    [Parameter(Mandatory)] [ValidatePattern('^\d+\.\d+\.\d+$')] [string] $Version,
    [string] $OutDir
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $OutDir) { $OutDir = Join-Path $repo 'artifacts\release' }
$name = "Tents-$Version-win-x64"
$stage = Join-Path $OutDir $name
$zip = Join-Path $OutDir "$name.zip"

if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
if (Test-Path $zip) { Remove-Item $zip -Force }
New-Item -ItemType Directory -Force $stage | Out-Null

& dotnet publish (Join-Path $repo 'src\Tents.App\Tents.App.csproj') -c Release -r win-x64 --self-contained false `
    -p:PublishReadyToRun=true -p:Version=$Version -p:DebugType=none -o $stage --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

$config = Get-Content (Join-Path $stage 'Tents.runtimeconfig.json') -Raw | ConvertFrom-Json
if ($config.runtimeOptions.configProperties.'System.GC.Concurrent' -ne $false) { throw 'Concurrent GC is not off in Tents.runtimeconfig.json.' }
if ($config.runtimeOptions.configProperties.'System.GC.Server' -eq $true) { throw 'Server GC is on in Tents.runtimeconfig.json.' }

Copy-Item (Join-Path $repo 'LICENSE') (Join-Path $stage 'LICENSE.txt')
@"
Tents $Version (portable, Windows x64)

Requires the .NET 10 Desktop Runtime (x64): https://dotnet.microsoft.com/download/dotnet/10.0
Run Tents.exe. It turns the native desktop icons off while it shows your items in tents, and back on
when you pause or exit (tray icon > Show Windows desktop icons, or Exit Tents). Start it with
--keep-native-icons to leave the native icons on.

Settings and layout: %APPDATA%\Tents\layout.json. Log: %LOCALAPPDATA%\Tents\tents.log.
To remove Tents: untick Start with Windows in its tray menu, exit it, and delete this folder.

Source, issues and release notes: https://github.com/alicesore/tents
"@ | Set-Content (Join-Path $stage 'README.txt') -Encoding utf8

Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
$size = (Get-Item $zip).Length
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Get-ChildItem $stage | Sort-Object Name | Format-Table Name, Length -AutoSize | Out-Host
"{0}  {1:N0} bytes ({2:N2} MB)" -f $zip, $size, ($size / 1MB)
"SHA-256 $hash"
