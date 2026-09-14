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
    dotnet publish (Join-Path $workspace 'CircleFlow.csproj') -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=false -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false -o $appDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Publish of CircleFlow failed.' }

    $required = @('CircleFlow.exe', 'CircleFlow.dll', 'CircleFlow.deps.json', 'CircleFlow.runtimeconfig.json',
        'coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll', 'System.Private.CoreLib.dll', 'PresentationFramework.dll',
        'WinRT.Runtime.dll', 'Microsoft.Windows.SDK.NET.dll', 'Microsoft.Web.WebView2.Core.dll',
        'Microsoft.Web.WebView2.Wpf.dll', 'Languages/en.xaml', 'Images/app.png', 'Images/app.ico',
        'Extensions/uBlockOriginLite.zip', 'LICENSE', 'THIRD_PARTY_NOTICES.txt',
        'THIRD_PARTY_LICENSES/Microsoft.Web.WebView2.LICENSE.txt', 'THIRD_PARTY_LICENSES/Microsoft.Web.WebView2.NOTICE.txt',
        'THIRD_PARTY_LICENSES/System.Numerics.Tensors.NOTICE.txt')
    foreach ($asset in $required) {
        if (-not (Test-Path -LiteralPath (Join-Path $appDirectory $asset) -PathType Leaf)) { throw "Missing published asset: $asset" }
    }
    $loaders = @('WebView2Loader.dll', 'runtimes\win-x64\native\WebView2Loader.dll') |
        Where-Object { Test-Path -LiteralPath (Join-Path $appDirectory $_) -PathType Leaf }
    if (-not $loaders) { throw 'The published x64 WebView2 loader is missing.' }
    $files = @(Get-ChildItem -LiteralPath $appDirectory -File -Recurse -Force | Sort-Object FullName)
    foreach ($file in $files) {
        $relative = $file.FullName.Substring($appDirectory.Length + 1).Replace('\', '/')
        if ($relative -match '(^|/)(Data|tests|design|poc|Profiles|Logs|Temp)(/|$)|(^|/)(plugin\.json|settings[^/]*\.json|Flow\.Launcher[^/]*|CircleFlow\.PublishProbe[^/]*)$') {
            throw "Unexpected published file: $relative"
        }
    }
    $version = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $appDirectory 'CircleFlow.dll')).ProductVersion.Split('+')[0]
    if ($version -notmatch '^\d+\.\d+\.\d+(?:[.\-][0-9A-Za-z.\-]+)?$') { throw 'Unexpected product version.' }
    $archivePath = Join-Path $releases "CircleFlow-$version-win-x64.zip"
    $temporaryZip = Join-Path $staging 'package.zip'
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::Open($temporaryZip, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in $files) {
            $entryName = 'CircleFlow/' + $file.FullName.Substring($appDirectory.Length + 1).Replace('\', '/')
            $entry = $archive.CreateEntry($entryName, [IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = [DateTimeOffset]::new(1980, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
            $inputStream = [IO.File]::OpenRead($file.FullName)
            try {
                $outputStream = $entry.Open()
                try { $inputStream.CopyTo($outputStream) }
                finally { $outputStream.Dispose() }
            }
            finally { $inputStream.Dispose() }
        }
    }
    finally { $archive.Dispose() }
    Move-Item -LiteralPath $temporaryZip -Destination $archivePath -Force
    $publishedPath = Resolve-Path -Relative -LiteralPath $appDirectory
    $releaseZipPath = Resolve-Path -Relative -LiteralPath $archivePath
    Write-Host ''
    Write-Host 'Published application: ' -NoNewline
    Write-Host "${lavenderish}$publishedPath"
    Write-Host 'WebView2 loader: ' -NoNewline
    Write-Host "${lavenderish}$($loaders -join ', ')"
    Write-Host 'Release ZIP: ' -NoNewline
    Write-Host "${lavenderish}$releaseZipPath"
    Write-Host 'SHA256: ' -NoNewline
    Write-Host "${lavenderish}$((Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash)"
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
