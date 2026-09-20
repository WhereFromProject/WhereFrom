$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $repoRoot 'artifacts'
# A fresh stage prevents packaging stale output after a failed build.
$stage = Join-Path $artifacts ('gui-stage-' + [guid]::NewGuid().ToString('N'))
$package = Join-Path $stage 'WhereFrom-GUI-win-x64'
New-Item -ItemType Directory -Path $package -Force | Out-Null

dotnet publish (Join-Path $repoRoot 'src/WhereFrom.App/WhereFrom.App.csproj') `
    -c Release -p:Platform=x64 -r win-x64 --self-contained true `
    -p:PublishSingleFile=false -p:PublishTrimmed=false -o $package
if ($LASTEXITCODE -ne 0) { throw 'GUI publish failed.' }
foreach ($file in @('WhereFrom.App.exe', 'WhereFrom.App.pri', 'Microsoft.UI.Xaml.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $package $file))) {
        throw "GUI package is incomplete: missing $file"
    }
}
foreach ($file in @('LICENSE', 'README.md', 'README.zh-CN.md')) {
    Copy-Item -LiteralPath (Join-Path $repoRoot $file) -Destination $package
}
$zip = Join-Path $artifacts 'WhereFrom-GUI-0.1.0-win-x64.zip'
Compress-Archive -LiteralPath $package -DestinationPath $zip -Force
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $artifacts 'WhereFrom-GUI-SHA256SUMS.txt'),
    $hash + '  ' + [IO.Path]::GetFileName($zip) + [Environment]::NewLine,
    [Text.UTF8Encoding]::new($false))
Write-Output "Package: $zip"
