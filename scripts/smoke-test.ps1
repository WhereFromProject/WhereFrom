param(
    [Parameter(Mandatory = $true)]
    [string]$Executable
)
$ErrorActionPreference = 'Stop'
$Executable = (Resolve-Path -LiteralPath $Executable).Path
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('wherefrom-smoke-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
$file = Join-Path $fixture ('download-' + [char]0x4e2d + [char]0x6587 + '.zip')
$stderr = Join-Path $fixture 'stderr.txt'
# stderr stays outside the scanned directory.
$scan = Join-Path $fixture 'scan'
New-Item -ItemType Directory -Path $scan | Out-Null
$file = Join-Path $scan ([IO.Path]::GetFileName($file))
$local = Join-Path $scan 'local.txt'
$nested = Join-Path $scan 'nested'
$inner = Join-Path $nested 'inside.txt'
$utf8 = [Text.UTF8Encoding]::new($false)

function Get-ZoneBytes {
    if ($PSVersionTable.PSVersion.Major -ge 6) {
        [byte[]](Get-Content -LiteralPath $file -Stream Zone.Identifier -AsByteStream -ReadCount 0)
    } else {
        [byte[]](Get-Content -LiteralPath $file -Stream Zone.Identifier -Encoding Byte -ReadCount 0)
    }
}

function Invoke-WhereFrom {
    param([string[]]$CliArgs, [int]$ExpectedCode)
    $saved = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $lines = & $Executable @CliArgs 2> $stderr
        $code = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $saved
    }
    if ($code -ne $ExpectedCode) { throw "Expected exit $ExpectedCode, got $code for $CliArgs" }
    [pscustomobject]@{
        Output = $lines -join [Environment]::NewLine
        Error = [IO.File]::ReadAllText($stderr)
    }
}
try {
    [IO.File]::WriteAllText($file, 'fixture body', $utf8)
    [IO.File]::WriteAllText($local, 'local', $utf8)
    New-Item -ItemType Directory -Path $nested | Out-Null
    [IO.File]::WriteAllText($inner, 'nested', $utf8)
    $source = 'https://example.com/file?a=%22value%22&token=a%2Fb#part'
    $referrer = 'https://example.com/download'
    Set-Content -LiteralPath $file -Stream Zone.Identifier -Encoding UTF8 -NoNewline -Value (
        [string]::Join([Environment]::NewLine, @('[ZoneTransfer]', 'ZoneId=3', "HostUrl=$source", "ReferrerUrl=$referrer")) + [char]0)
    $body = [Convert]::ToBase64String([IO.File]::ReadAllBytes($file))
    $ads = [Convert]::ToBase64String([byte[]](Get-ZoneBytes))

    $version = Invoke-WhereFrom -CliArgs @('--version') -ExpectedCode 0
    if ($version.Output -ne 'WhereFrom 0.1.0' -or $version.Error) { throw 'Version mismatch.' }
    $help = Invoke-WhereFrom -CliArgs @('--help') -ExpectedCode 0
    foreach ($command in @('wherefrom <file>', 'wherefrom scan <directory>', 'wherefrom open <file>', '--json')) {
        if (-not $help.Output.Contains($command)) { throw "Missing help: $command" }
    }
    $text = Invoke-WhereFrom -CliArgs @($file) -ExpectedCode 0
    if (-not $text.Output.Contains($source) -or -not $text.Output.Contains([IO.Path]::GetFileName($file)) -or $text.Error) { throw 'Text mismatch.' }
    $json = Invoke-WhereFrom -CliArgs @($file, '--json') -ExpectedCode 0
    $result = $json.Output | ConvertFrom-Json
    if ($json.Error -or $result.schemaVersion -ne 1 -or $result.path -cne $file -or $result.sourceUrl -cne $source -or $result.referrerUrl -cne $referrer -or $result.zone.id -ne 3) { throw 'JSON mismatch.' }
    $unknown = Invoke-WhereFrom -CliArgs @($local, '--json') -ExpectedCode 1
    $result = $unknown.Output | ConvertFrom-Json
    if ($result.hasProvenance -or $null -ne $result.zone -or $null -ne $result.sourceUrl -or $null -ne $result.referrerUrl -or $unknown.Error) { throw 'No-metadata mismatch.' }
    $report = Invoke-WhereFrom -CliArgs @('scan', $scan) -ExpectedCode 0
    foreach ($summary in @('Scanned: 2', 'Known provenance: 1', 'Unknown: 1', 'Errors: 0')) {
        if (-not $report.Output.Contains($summary)) { throw "Scan mismatch: $summary" }
    }
    if ($report.Error -or $report.Output.Contains('inside.txt')) { throw 'Scan crossed a directory boundary.' }
    $none = Invoke-WhereFrom -CliArgs @('open', $local) -ExpectedCode 1
    if ($none.Output -ne 'No source URL is available for this file.') { throw 'No URL mismatch.' }
    $missing = Invoke-WhereFrom -CliArgs @($file + '.missing', '--json') -ExpectedCode 3
    if ($missing.Output -or -not $missing.Error.Contains('File not found.')) { throw 'Missing file mismatch.' }
    $directory = Invoke-WhereFrom -CliArgs @($scan) -ExpectedCode 2
    if ($directory.Output -or -not $directory.Error.Contains('Path is a directory')) { throw 'Directory input mismatch.' }

    if ($body -cne [Convert]::ToBase64String([IO.File]::ReadAllBytes($file)) -or $ads -cne [Convert]::ToBase64String([byte[]](Get-ZoneBytes))) { throw 'Evidence changed.' }

    Set-Content -LiteralPath $file -Stream Zone.Identifier -Encoding UTF8 -NoNewline -Value (
        [string]::Join([Environment]::NewLine, @('[ZoneTransfer]', 'ReferrerUrl=file:///C:/Windows/notepad.exe', "HostUrl=$source")))
    $blocked = Invoke-WhereFrom -CliArgs @('open', $file) -ExpectedCode 2
    if ($blocked.Output -or -not $blocked.Error.Contains('Only valid HTTP or HTTPS')) { throw 'Protocol rejection mismatch.' }
    Write-Output 'PASS: published EXE version/help, text, JSON, Unicode, read-only ADS, scan, errors and open refusal. Browser navigation is a separate manual check.'
} finally {
    # Delete only our exact fixture files; never recursively remove a supplied path.
    foreach ($path in @($file, $local, $inner, $stderr)) { [IO.File]::Delete($path) }
    [IO.Directory]::Delete($nested)
    [IO.Directory]::Delete($scan)
    [IO.Directory]::Delete($fixture)
}
exit 0
