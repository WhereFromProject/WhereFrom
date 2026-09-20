param(
    [Parameter(Mandatory = $true)]
    [string]$Executable
)

$ErrorActionPreference = 'Stop'
$path = (Resolve-Path -LiteralPath $Executable).Path
$process = Start-Process -FilePath $path -WorkingDirectory (Split-Path -Parent $path) -WindowStyle Hidden -PassThru
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
    if (-not $process.CloseMainWindow()) { throw 'Could not close the GUI window.' }
    if (-not $process.WaitForExit(5000)) { throw 'GUI did not exit after closing its window.' }
    if ($process.ExitCode -ne 0) { throw "GUI closed with code $($process.ExitCode)." }
    Write-Output 'GUI window startup and shutdown passed.'
}
finally {
    if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
    $process.Dispose()
}
