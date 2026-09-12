[CmdletBinding()]
param(
    [switch]$NoPause
)

$ErrorActionPreference = 'Stop'

$e = [char]27
$lavenderish = "$e[38;2;208;188;255m"
$reset = "$e[0m"

Set-Location $PSScriptRoot

function Wait-BeforeClosing {
    if ($NoPause) { return }
    if (-not [Environment]::UserInteractive) { return }
    if ([Console]::IsInputRedirected) { return }

    Write-Host ''
    Write-Host "${lavenderish}Done. " -NoNewline
    Write-Host 'Press any key to close this window...'

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
    dotnet build .\CircleFlow.csproj --configuration Release --output $output
    if ($LASTEXITCODE -ne 0) { throw 'Build of CircleFlow failed.' }

    Write-Host ''
    Write-Host 'CircleFlow built to: ' -NoNewline
    Write-Host "${lavenderish}$output"
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
