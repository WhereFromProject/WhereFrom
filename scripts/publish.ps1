$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$repo = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $repo 'artifacts'
# Every run gets a fresh staging directory, so stale files cannot enter the ZIP.
$stage = Join-Path $artifacts ('stage-' + [guid]::NewGuid().ToString('N'))
$package = Join-Path $stage 'WhereFrom-win-x64'
New-Item -ItemType Directory -Path $package -Force | Out-Null
dotnet publish (Join-Path $repo 'src/WhereFrom.Cli/WhereFrom.Cli.csproj') -c Release -p:PublishProfile=win-x64 -p:DebugType=none -p:DebugSymbols=false -o $package
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

foreach ($name in @('LICENSE', 'README.md', 'README.zh-CN.md')) {
    Copy-Item -LiteralPath (Join-Path $repo $name) -Destination $package
}
# Include the exact runtime package's license and third-party notices.
$assets = Get-Content -LiteralPath (Join-Path $repo 'src/WhereFrom.Cli/obj/project.assets.json') -Raw | ConvertFrom-Json
$runtime = $assets.project.frameworks.'net10.0-windows'.downloadDependencies | Where-Object { $_.name -eq 'Microsoft.NETCore.App.Runtime.win-x64' } | Select-Object -First 1
if ($null -eq $runtime) { throw 'Runtime package metadata not found.' }
$runtimeVersion = $runtime.version.Split(',')[0].TrimStart('[').Trim()
$runtimeRelativePath = $runtime.name.ToLowerInvariant() + '/' + $runtimeVersion
$runtimePath = $null
foreach ($folder in $assets.packageFolders.PSObject.Properties.Name) {
    $candidate = Join-Path $folder $runtimeRelativePath
    if (Test-Path -LiteralPath $candidate) { $runtimePath = $candidate; break }
}
if ($null -eq $runtimePath) { throw 'Runtime package directory not found.' }
Copy-Item -LiteralPath (Join-Path $runtimePath 'LICENSE.TXT') -Destination (Join-Path $package 'DOTNET-LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $runtimePath 'THIRD-PARTY-NOTICES.TXT') -Destination (Join-Path $package 'DOTNET-THIRD-PARTY-NOTICES.txt')
$docs = Join-Path $package 'docs'
New-Item -ItemType Directory -Path $docs | Out-Null
Copy-Item -Path (Join-Path $repo 'docs/*.md') -Destination $docs
Copy-Item -LiteralPath (Join-Path $repo 'ENGINEERING.md') -Destination $package
Copy-Item -LiteralPath (Join-Path $repo 'scripts/smoke-test.ps1') -Destination $package

$zip = Join-Path $artifacts 'WhereFrom-0.1.0-win-x64.zip'
Compress-Archive -LiteralPath $package -DestinationPath $zip -Force
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $artifacts 'SHA256SUMS.txt'),
    $hash + '  ' + [IO.Path]::GetFileName($zip) + [Environment]::NewLine,
    [Text.UTF8Encoding]::new($false))
Write-Output "Package: $zip"
Write-Output "Staging: $package"
