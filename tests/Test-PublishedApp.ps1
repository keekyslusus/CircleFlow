[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$ArchivePath)

$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$archivePath = (Resolve-Path -LiteralPath $ArchivePath).Path
$sessionId = (Get-Process -Id $PID).SessionId
if (Get-Process -Name CircleFlow -ErrorAction SilentlyContinue | Where-Object SessionId -eq $sessionId) {
    throw 'Exit the running CircleFlow before checking the published copy.'
}
$testDirectory = Join-Path $PSScriptRoot ('temp\publish-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testDirectory -Force | Out-Null
$unpacked = Join-Path $testDirectory ('unpacked ' + [char]0x442 + [char]0x435 + [char]0x441 + [char]0x442)
# Velopack's portable layout: Update.exe and a launcher stub beside current\, where the application lives.
$appDirectory = Join-Path $unpacked 'current'
$velopackEntries = @('.portable', 'CircleFlow.exe', 'Update.exe', 'current/sq.version')
$probeOutput = Join-Path $testDirectory 'hook'

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($archivePath)
try {
    $entries = @($archive.Entries | ForEach-Object FullName)
    foreach ($name in $entries) {
        if (-not ($name.StartsWith('current/', [StringComparison]::Ordinal) -or $velopackEntries -ccontains $name) -or $name.Contains('\') -or
            $name -match '(^|/)(\.\.?|Data|tests|design|poc|Profiles|Logs|Temp)(/|$)|(^|/)(plugin\.json|settings[^/]*\.json|Flow\.Launcher[^/]*|CircleFlow\.PublishProbe[^/]*)$') {
            throw "Unexpected ZIP entry: $name"
        }
        $destination = [IO.Path]::GetFullPath((Join-Path $unpacked $name))
        if (-not $destination.StartsWith($unpacked + '\', [StringComparison]::OrdinalIgnoreCase)) {
            throw "ZIP entry escaped the application folder: $name"
        }
    }
    foreach ($name in $entries) {
        if ($velopackEntries -ccontains $name) { continue }
        if ($name -match '\.xml$' -or $name -notmatch '^current/(deps/|Images/|Languages/|Extensions/|THIRD_PARTY_LICENSES/|CircleFlow\.exe$|LICENSE$|THIRD_PARTY_NOTICES\.txt$)') {
            throw "Unexpected release layout: $name"
        }
    }
    $required = @('CircleFlow.exe', 'deps/CircleFlow.dll', 'deps/CircleFlow.runtimeconfig.json', 'deps/CircleFlow.deps.json',
        'deps/WinRT.Runtime.dll', 'deps/coreclr.dll', 'deps/System.Private.CoreLib.dll', 'deps/hostfxr.dll', 'deps/hostpolicy.dll',
        'Languages/en.xaml', 'Images/app.ico', 'Extensions/uBlockOriginLite.zip', 'Extensions/CircleFlowFilters.txt',
        'LICENSE', 'THIRD_PARTY_NOTICES.txt', 'THIRD_PARTY_LICENSES/Microsoft.Web.WebView2.LICENSE.txt',
        'THIRD_PARTY_LICENSES/Microsoft.Web.WebView2.NOTICE.txt', 'THIRD_PARTY_LICENSES/System.Numerics.Tensors.NOTICE.txt')
    foreach ($asset in $velopackEntries) {
        if ($entries -cnotcontains $asset) { throw "Missing Velopack entry: $asset" }
    }
    foreach ($asset in $required) {
        if ($entries -cnotcontains ('current/' + $asset)) { throw "Missing ZIP asset: $asset" }
    }
    $licenseNames = Get-ChildItem -LiteralPath (Join-Path $workspace 'THIRD_PARTY_LICENSES') -Filter '*.txt' -File
    foreach ($license in $licenseNames) {
        if ($entries -cnotcontains ('current/THIRD_PARTY_LICENSES/' + $license.Name)) { throw "Missing license: $($license.Name)" }
    }
}
finally { $archive.Dispose() }
[IO.Compression.ZipFile]::ExtractToDirectory($archivePath, $unpacked)
$assetSources = [ordered]@{
    'Languages/en.xaml' = 'Languages/en.xaml'; 'Images/app.ico' = 'CTS/app.ico'
    'Extensions/uBlockOriginLite.zip' = 'Extensions/uBlockOriginLite.zip'; 'LICENSE' = 'LICENSE'
    'Extensions/CircleFlowFilters.txt' = 'Extensions/CircleFlowFilters.txt'
    'THIRD_PARTY_NOTICES.txt' = 'THIRD_PARTY_NOTICES.txt'
}
foreach ($asset in $assetSources.Keys) {
    if ((Get-FileHash -LiteralPath (Join-Path $workspace $assetSources[$asset])).Hash -ne
        (Get-FileHash -LiteralPath (Join-Path $appDirectory $asset)).Hash) { throw "Published asset differs from source: $asset" }
}
$runtimeConfig = Get-Content -LiteralPath (Join-Path $appDirectory 'deps/CircleFlow.runtimeconfig.json') -Raw | ConvertFrom-Json
if (-not $runtimeConfig.runtimeOptions.includedFrameworks -or $runtimeConfig.runtimeOptions.framework -or $runtimeConfig.runtimeOptions.frameworks) {
    throw 'The archive is not self-contained.'
}

dotnet build (Join-Path $PSScriptRoot 'PublishedHostProbe\PublishedHostProbe.csproj') -c Release -o $probeOutput
if ($LASTEXITCODE -ne 0) { throw 'Published-process probe did not build.' }
$reportPath = Join-Path $unpacked 'Data\Temp\publish-probe.json'
$start = New-Object Diagnostics.ProcessStartInfo
$start.FileName = Join-Path $appDirectory 'CircleFlow.exe'
$start.WorkingDirectory = $testDirectory
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.EnvironmentVariables['DOTNET_STARTUP_HOOKS'] = Join-Path $probeOutput 'CircleFlow.PublishProbe.dll'
$start.EnvironmentVariables['DOTNET_ROOT'] = Join-Path $testDirectory 'no-installed-dotnet'
$start.EnvironmentVariables['DOTNET_ROOT_X64'] = $start.EnvironmentVariables['DOTNET_ROOT']
$start.EnvironmentVariables['DOTNET_MULTILEVEL_LOOKUP'] = '0'
$start.EnvironmentVariables.Remove('WEBVIEW2_USER_DATA_FOLDER')
$start.EnvironmentVariables.Remove('WEBVIEW2_BROWSER_EXECUTABLE_FOLDER')
$process = [Diagnostics.Process]::Start($start)
try {
    if (-not $process.WaitForExit(60000)) {
        $process.Kill()
        throw 'Published process did not finish its smoke check and normal shutdown within 60 seconds.'
    }
    if (-not (Test-Path -LiteralPath $reportPath)) { throw "Published process exited $($process.ExitCode) without a report." }
    $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
    if ($process.ExitCode -ne 0 -or -not $report.Success) { throw "Published-process check failed: $($report.Error) (exit $($process.ExitCode))" }
    Write-Host "ZIP verified: $archivePath ($($entries.Count) files)"
    Write-Host "Published-process report: $reportPath"
    $report | ConvertTo-Json -Depth 5
}
finally { $process.Dispose() }
