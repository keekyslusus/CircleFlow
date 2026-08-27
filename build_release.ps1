[CmdletBinding()]
param(
    [switch]$NoPause
)

$ErrorActionPreference = 'Stop'

Set-Location $PSScriptRoot

function Wait-BeforeClosing {
    if ($NoPause) { return }
    if (-not [Environment]::UserInteractive) { return }
    if ([Console]::IsInputRedirected) { return }

    Write-Host ''
    Write-Host 'Done. Press any key to close this window...' -ForegroundColor Cyan

    try {
        $null = $Host.UI.RawUI.ReadKey('NoEcho,IncludeKeyDown')
    }
    catch {
        $null = Read-Host 'Press Enter to close'
    }
}

$exitCode = 0
$output = Join-Path $PSScriptRoot 'bin\Release'

try {
    dotnet build .\CircleToSearch.csproj --configuration Release --output $output
    if ($LASTEXITCODE -ne 0) { throw 'Build of CircleToSearch failed.' }

    Write-Host ''
    Write-Host "CircleToSearch built to $output" -ForegroundColor Green
    Write-Host "Install: copy the contents of $output into %APPDATA%\FlowLauncher\Plugins\CircleToSearch\ then run 'Reload Plugin Data' in Flow Launcher." -ForegroundColor Green
}
catch {
    $exitCode = 1
    Write-Host ''
    Write-Host 'BUILD FAILED' -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
}
finally {
    Wait-BeforeClosing
}

exit $exitCode
