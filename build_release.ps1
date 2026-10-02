[CmdletBinding()]
param(
    [switch]$NoPause,
    # A test build: the app looks for updates in this folder instead of GitHub, and the packages go there too.
    [string]$UpdateFeed,
    # A test build that looks for updates in the releases of this GitHub repository instead of the project's.
    [string]$UpdateRepository,
    [string]$Version
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
    $releases = Join-Path $bin 'releases'
    foreach ($directory in @($bin, $publishRoot, $releases)) {
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
    $publishProperties = @()
    if ($UpdateFeed) {
        $releases = [IO.Path]::GetFullPath($UpdateFeed)
        New-Item -ItemType Directory -Path $releases -Force | Out-Null
        $publishProperties += "-p:UpdateFeed=$releases"
    }
    if ($UpdateRepository) {
        if ($UpdateFeed) { throw 'Pass either -UpdateFeed or -UpdateRepository.' }
        if ($UpdateRepository -notmatch '^https://github\.com/[^/]+/[^/]+$') { throw 'Pass the repository as https://github.com/<owner>/<name>.' }
        $publishProperties += "-p:UpdateRepository=$UpdateRepository"
    }
    if ($Version) { $publishProperties += "-p:Version=$Version" }
    dotnet publish (Join-Path $workspace 'CircleFlow.csproj') -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=false -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false @publishProperties -o $appDirectory
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
    $files = @(Get-ChildItem -LiteralPath $appDirectory -File -Recurse -Force | Sort-Object FullName)
    foreach ($file in $files) {
        $relative = $file.FullName.Substring($appDirectory.Length + 1).Replace('\', '/')
        if ($relative -match '(^|/)(Data|tests|design|poc|Profiles|Logs|Temp)(/|$)|(^|/)(plugin\.json|settings[^/]*\.json|Flow\.Launcher[^/]*|CircleFlow\.PublishProbe[^/]*)$') {
            throw "Unexpected published file: $relative"
        }
    }
    $version = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $appDirectory 'deps/CircleFlow.dll')).ProductVersion.Split('+')[0]
    if ($version -notmatch '^\d+\.\d+\.\d+(?:[.\-][0-9A-Za-z.\-]+)?$') { throw 'Unexpected product version.' }
    $feedFile = Join-Path $releases 'releases.win.json'
    # vpk refuses to pack a version the feed already has, so a local rebuild starts from an empty output.
    if ((Test-Path -LiteralPath $feedFile) -and
        @((Get-Content -LiteralPath $feedFile -Raw | ConvertFrom-Json).Assets | Where-Object { $_.Version -eq $version }).Count -gt 0) {
        if ($UpdateFeed) { throw "The update feed already has $version; pass a newer -Version." }
        Get-ChildItem -LiteralPath $releases -Force | Remove-Item -Recurse -Force
    }
    dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw 'Restoring the Velopack CLI failed.' }
    # The Start menu shortcut also carries the AUMID that Windows needs to show update toasts.
    dotnet vpk pack --packId CircleFlow --packVersion $version --packDir $appDirectory --mainExe CircleFlow.exe `
        --packTitle CircleFlow --packAuthors keekys --icon (Join-Path $workspace 'CTS\app.ico') `
        --runtime win-x64 --shortcuts StartMenuRoot --outputDir $releases
    if ($LASTEXITCODE -ne 0) { throw 'Packing the Velopack release failed.' }
    $setupPath = Join-Path $releases 'CircleFlow-win-Setup.exe'
    $portablePath = Join-Path $releases 'CircleFlow-win-Portable.zip'
    foreach ($output in @($setupPath, $portablePath, (Join-Path $releases 'releases.win.json'),
        (Join-Path $releases "CircleFlow-$version-full.nupkg"))) {
        if (-not (Test-Path -LiteralPath $output -PathType Leaf)) { throw "Missing release output: $output" }
    }
    Write-Host ''
    Write-Host 'Published application: ' -NoNewline
    Write-Host "${lavenderish}$(Resolve-Path -Relative -LiteralPath $appDirectory)"
    Write-Host 'WebView2 loader: ' -NoNewline
    Write-Host "${lavenderish}$($loaders -join ', ')"
    Write-Host 'Installer: ' -NoNewline
    Write-Host "${lavenderish}$(Resolve-Path -Relative -LiteralPath $setupPath)"
    Write-Host 'Portable ZIP: ' -NoNewline
    Write-Host "${lavenderish}$(Resolve-Path -Relative -LiteralPath $portablePath)"
}
catch {
    $exitCode = 1
    Write-Host ''
    Write-Host 'RELEASE FAILED' -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
}
finally {
    Pop-Location
    Wait-BeforeClosing
}

exit $exitCode
