param(
    [Parameter(Mandatory = $true)][string]$Executable,
    [Parameter(Mandatory = $true)][string]$ExtensionId
)

$ErrorActionPreference = 'Stop'
if ($ExtensionId -notmatch '^[a-p]{32}$') { throw 'Invalid extension ID.' }
$path = (Resolve-Path -LiteralPath $Executable).Path
$start = [Diagnostics.ProcessStartInfo]::new($path)
$start.ArgumentList.Add("chrome-extension://$ExtensionId/")
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardInput = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$process = [Diagnostics.Process]::Start($start)
try {
    $payload = [Text.Encoding]::UTF8.GetBytes('{"schemaVersion":0,"event":"invalid"}')
    $prefix = [BitConverter]::GetBytes([uint32]$payload.Length)
    $process.StandardInput.BaseStream.Write($prefix)
    $process.StandardInput.BaseStream.Write($payload)
    $process.StandardInput.Close()
    if (-not $process.WaitForExit(10000)) { throw 'Native Host did not exit after input closed.' }
    $stderr = $process.StandardError.ReadToEnd()
    if ($process.ExitCode -ne 0 -or $stderr) { throw "Native Host failed (code $($process.ExitCode)): $stderr" }
    $output = [IO.MemoryStream]::new()
    $process.StandardOutput.BaseStream.CopyTo($output)
    $output.Position = 0
    $reader = [IO.BinaryReader]::new($output, [Text.Encoding]::UTF8, $true)
    $length = $reader.ReadUInt32()
    if ($length -gt 4096) { throw 'Native Host reply is too large.' }
    $reply = [Text.Encoding]::UTF8.GetString($reader.ReadBytes([int]$length)) | ConvertFrom-Json
    if ($reply.ok -ne $false -or $reply.code -ne 'invalid_message') {
        throw "Native Host did not use the packaged extension allowlist: $($reply.code)"
    }
    Write-Output 'Native Host launch, sidecar allowlist, and protocol framing passed.'
}
finally {
    if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
    $process.Dispose()
}
