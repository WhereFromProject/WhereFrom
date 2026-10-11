param(
    [Parameter(Mandatory = $true)]
    [string]$Executable,
    [string]$FilePath
)

$ErrorActionPreference = 'Stop'
$path = (Resolve-Path -LiteralPath $Executable).Path
$start = @{
    FilePath = $path
    WorkingDirectory = (Split-Path -Parent $path)
    WindowStyle = 'Hidden'
    PassThru = $true
}
if ($FilePath) {
    $file = (Resolve-Path -LiteralPath $FilePath).Path
    $start.ArgumentList = '"' + $file + '"'
}
$process = Start-Process @start
try {
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    do {
        Start-Sleep -Milliseconds 250
        $process.Refresh()
        if ($process.HasExited) { throw "GUI exited before creating a window (code $($process.ExitCode))." }
        if ($process.MainWindowHandle -ne 0) { break }
    } while ([DateTime]::UtcNow -lt $deadline)
    if ($process.MainWindowHandle -eq 0) { throw 'GUI did not create a window within 30 seconds.' }
    Start-Sleep -Seconds 2
    $process.Refresh()
    if ($process.HasExited) { throw "GUI exited after activation (code $($process.ExitCode))." }
    # WinUI may expose transient framework windows as MainWindowHandle before the app window.
    # Close only windows belonging to this launched process until its app window exits.
    for ($attempt = 0; $attempt -lt 5 -and -not $process.HasExited; $attempt++) {
        $process.Refresh()
        if ($process.MainWindowHandle -ne 0) { [void]$process.CloseMainWindow() }
        [void]$process.WaitForExit(2500)
    }
    if (-not $process.HasExited) { throw 'GUI did not exit after closing its windows.' }
    if ($process.ExitCode -ne 0) { throw "GUI closed with code $($process.ExitCode)." }
    Write-Output 'GUI window startup and shutdown passed.'
}
finally {
    if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
    $process.Dispose()
}
