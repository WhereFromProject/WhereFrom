param([string]$Compiler)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if (-not $Compiler) {
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command) { $Compiler = $command.Source }
    else { $Compiler = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" }
}
if (-not (Test-Path -LiteralPath $Compiler)) { throw 'Install Inno Setup 6, or pass -Compiler with the path to ISCC.exe.' }
$artifacts = Join-Path $repo 'artifacts'
$stage = Join-Path $artifacts ('installer-' + [guid]::NewGuid().ToString('N'))
Expand-Archive -LiteralPath (Join-Path $artifacts 'WhereFrom-0.1.1-win-x64.zip') -DestinationPath $stage
$package = Join-Path $stage 'WhereFrom-win-x64'
& $Compiler "/DPackageDir=$package" "/DOutputDir=$artifacts" (Join-Path $repo 'installer/WhereFrom.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
$lines = foreach ($name in @('WhereFrom-0.1.1-win-x64.zip', 'WhereFrom-0.1.1-win-x64-Setup.exe')) {
    $hash = (Get-FileHash -LiteralPath (Join-Path $artifacts $name) -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $name"
}
[IO.File]::WriteAllLines((Join-Path $artifacts 'SHA256SUMS.txt'), $lines, [Text.UTF8Encoding]::new($false))
