[CmdletBinding()]
param(
    [switch]$NoPause
)

$ErrorActionPreference = 'Stop'

$e = [char]27
$lavenderish = "$e[38;2;208;188;255m"
$reset = "$e[0m"

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
Push-Location -LiteralPath $PSScriptRoot
try {
    $workspace = [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\')
    $bin = Join-Path $workspace 'bin'
    $publishRoot = Join-Path $bin 'publish'
    foreach ($directory in @($bin, $publishRoot)) {
        if ((Test-Path -LiteralPath $directory) -and
            ((Get-Item -LiteralPath $directory -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Build output must not be a link: $directory"
        }
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    $staging = [IO.Path]::GetFullPath((Join-Path $publishRoot ('stage-' + [Guid]::NewGuid().ToString('N'))))
    if (-not $staging.StartsWith($workspace + '\bin\publish\', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Staging path escaped the workspace.'
    }
    New-Item -ItemType Directory -Path $staging | Out-Null

    $appDirectory = Join-Path $staging 'CircleFlow'
    dotnet publish (Join-Path $workspace 'CircleFlow.csproj') -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=false -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false -o $appDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Publish of CircleFlow failed.' }

    $required = @('CircleFlow.exe', 'deps/CircleFlow.dll', 'deps/CircleFlow.deps.json', 'deps/CircleFlow.runtimeconfig.json',
        'deps/coreclr.dll', 'deps/hostfxr.dll', 'deps/hostpolicy.dll', 'deps/System.Private.CoreLib.dll', 'deps/PresentationFramework.dll',
        'deps/WinRT.Runtime.dll', 'deps/Microsoft.Windows.SDK.NET.dll', 'deps/Microsoft.Web.WebView2.Core.dll',
        'deps/Microsoft.Web.WebView2.Wpf.dll', 'Languages/en.xaml', 'Images/app.ico',
        'Extensions/uBlockOriginLite.zip', 'Extensions/CircleFlowFilters.txt', 'Emoji/NotoColorEmoji.zip', 'LICENSE', 'THIRD_PARTY_NOTICES.txt',
        'THIRD_PARTY_LICENSES/Microsoft.Web.WebView2.LICENSE.txt', 'THIRD_PARTY_LICENSES/Microsoft.Web.WebView2.NOTICE.txt',
        'THIRD_PARTY_LICENSES/System.Numerics.Tensors.NOTICE.txt')
    foreach ($asset in $required) {
        if (-not (Test-Path -LiteralPath (Join-Path $appDirectory $asset) -PathType Leaf)) { throw "Missing published asset: $asset" }
    }

    $loaders = @('deps/WebView2Loader.dll', 'deps/runtimes/win-x64/native/WebView2Loader.dll') |
        Where-Object { Test-Path -LiteralPath (Join-Path $appDirectory $_) -PathType Leaf }
    if (-not $loaders) { throw 'The published x64 WebView2 loader is missing.' }

    $files = @(Get-ChildItem -LiteralPath $appDirectory -File -Recurse -Force)
    foreach ($file in $files) {
        $relative = $file.FullName.Substring($appDirectory.Length + 1).Replace('\', '/')
        if ($relative -match '(^|/)(Data|tests|design|poc|Profiles|Logs|Temp)(/|$)|(^|/)(plugin\.json|settings[^/]*\.json|Flow\.Launcher[^/]*|CircleFlow\.PublishProbe[^/]*)$') {
            throw "Unexpected published file: $relative"
        }
    }

    $stagingPath = Resolve-Path -Relative -LiteralPath $staging
    $publishedPath = Resolve-Path -Relative -LiteralPath $appDirectory
    Write-Host ''
    Write-Host 'Stage directory: ' -NoNewline
    Write-Host "${lavenderish}$stagingPath${reset}"
    Write-Host 'Published application: ' -NoNewline
    Write-Host "${lavenderish}$publishedPath${reset}"
    Write-Host 'WebView2 loader: ' -NoNewline
    Write-Host "${lavenderish}$($loaders -join ', ')${reset}"
}
catch {
    $exitCode = 1
    Write-Host ''
    Write-Host 'STAGE FAILED' -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
}
finally {
    Pop-Location
    Wait-BeforeClosing
}

exit $exitCode
