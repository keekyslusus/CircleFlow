[CmdletBinding()]
param()

# Installs a test build that reads updates from a local feed, then publishes a newer build into that feed,
# so the whole offer-download-restart flow can be tried without a GitHub release.
$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$feed = Join-Path $PSScriptRoot 'temp\update-feed'
$installRoot = Join-Path $env:LOCALAPPDATA 'CircleFlow'
if (Get-Process -Name CircleFlow -ErrorAction SilentlyContinue) { throw 'Exit CircleFlow before testing updates.' }
# Installing over a real copy would mix the test with it; uninstalling it would delete its Data.
if (Test-Path -LiteralPath (Join-Path $installRoot 'Update.exe')) {
    throw "CircleFlow is installed in $installRoot. Uninstall it in Windows Settings first; this also removes its Data."
}

$baseVersion = ([xml](Get-Content -LiteralPath (Join-Path $workspace 'CircleFlow.csproj'))).Project.PropertyGroup.Version |
    Where-Object { $_ } | Select-Object -First 1
$installed = "$baseVersion-update.1"
$offered = "$baseVersion-update.2"
if (Test-Path -LiteralPath $feed) { Remove-Item -LiteralPath $feed -Recurse -Force }

& (Join-Path $workspace 'build_release.ps1') -NoPause -UpdateFeed $feed -Version $installed
if ($LASTEXITCODE -ne 0) { throw "Building $installed failed." }
$setup = Start-Process -FilePath (Join-Path $feed 'CircleFlow-win-Setup.exe') -ArgumentList '--silent' -Wait -PassThru
if ($setup.ExitCode -ne 0) { throw "Installing $installed failed with exit code $($setup.ExitCode)." }

& (Join-Path $workspace 'build_release.ps1') -NoPause -UpdateFeed $feed -Version $offered
if ($LASTEXITCODE -ne 0) { throw "Building $offered failed." }
Start-Process -FilePath (Join-Path $installRoot 'current\CircleFlow.exe')

Write-Host ''
Write-Host "Installed $installed and started it; $offered is waiting in $feed."
Write-Host 'About a minute after start, CircleFlow shows the update offer. Click Update: CircleFlow downloads the delta,'
Write-Host "exits, installs $offered and starts again. Its version is in $installRoot\current\sq.version."
