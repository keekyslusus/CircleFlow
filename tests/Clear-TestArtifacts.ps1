[CmdletBinding(SupportsShouldProcess)]
param()

# Removes what test runs leave behind: tests\temp, the test project's build and result folders,
# and the WebView2 profiles and logs that older test runs and ad-hoc probes left in %TEMP%.
$ErrorActionPreference = 'Stop'

function Get-Size([string]$path) {
    [long](Get-ChildItem -LiteralPath $path -Recurse -Force -File -ErrorAction SilentlyContinue |
        Measure-Object -Property Length -Sum).Sum
}

function Remove-Target([string]$path) {
    Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction SilentlyContinue
    if (-not (Test-Path -LiteralPath $path)) { return }
    # Permission tests can leave deny rules behind; these folders are ours, so restore inherited access and retry.
    icacls $path /reset /T /C /Q *> $null
    Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction SilentlyContinue -ErrorVariable failures
    if (Test-Path -LiteralPath $path) { throw $failures[0] }
}

# "Run with PowerShell" closes the window as soon as the script ends, before the report can be read.
$parent = (Get-CimInstance Win32_Process -Filter "ProcessId = $PID").ParentProcessId
$startedFromExplorer = (Get-Process -Id $parent -ErrorAction SilentlyContinue).ProcessName -eq 'explorer'

try {
    $testProject = Join-Path $PSScriptRoot 'CircleToSearch.Tests'
    $systemTemp = [IO.Path]::GetTempPath()

    $targets = [Collections.Generic.List[string]]::new()
    $targets.Add((Join-Path $PSScriptRoot 'temp'))
    foreach ($name in 'bin', 'obj', 'TestResults') { $targets.Add((Join-Path $testProject $name)) }

    # The app itself never writes to %TEMP%, so these prefixes only match test and probe leftovers.
    Get-ChildItem -LiteralPath $systemTemp -Force -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match '^(CircleFlow|CircleToSearch|cts-)' } |
        ForEach-Object { $targets.Add($_.FullName) }

    # Older CosmeticFiltersTests runs logged straight into %TEMP%; the name is generic, so match the log line format too.
    foreach ($name in 'plugin.log', 'plugin.log.old') {
        $path = Join-Path $systemTemp $name
        if ((Test-Path -LiteralPath $path) -and
            (Get-Content -LiteralPath $path -TotalCount 1) -match '^\d{4}-\d\d-\d\d \d\d:\d\d:\d\d\.\d{3} \[\w+\] ') {
            $targets.Add($path)
        }
    }

    $freed = 0L
    $failed = [Collections.Generic.List[string]]::new()
    foreach ($target in $targets) {
        if (-not (Test-Path -LiteralPath $target)) { continue }
        if (-not $PSCmdlet.ShouldProcess($target, 'Remove')) { continue }
        $size = Get-Size $target
        try { Remove-Target $target }
        catch {
            # A running test or msedgewebview2 process keeps its profile locked; leave it for the next run.
            $failed.Add("$target - $($_.Exception.Message)")
            $size -= Get-Size $target
        }
        $freed += $size
        Write-Host ('{0,10:N1} MB  {1}' -f ($size / 1MB), $target)
    }

    Write-Host ('Freed {0:N1} MB.' -f ($freed / 1MB))
    if ($failed.Count -gt 0) {
        Write-Warning ("Could not remove $($failed.Count) item(s), probably in use:`n" + ($failed -join "`n"))
    }
}
catch {
    Write-Host $_ -ForegroundColor Red
}
finally {
    if ($startedFromExplorer) { Read-Host 'Press Enter to close' | Out-Null }
}
