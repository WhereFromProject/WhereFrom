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

$gui = Join-Path $package 'GUI'
dotnet publish (Join-Path $repo 'src/WhereFrom.App/WhereFrom.App.csproj') `
    -c Release -p:Platform=x64 -r win-x64 --self-contained true `
    -p:PublishSingleFile=false -p:PublishTrimmed=false `
    -p:DebugType=none -p:DebugSymbols=false -o $gui
if ($LASTEXITCODE -ne 0) { throw 'GUI publish failed.' }

$hostFolder = Join-Path $package 'NativeHost'
dotnet publish (Join-Path $repo 'src/WhereFrom.NativeHost/WhereFrom.NativeHost.csproj') `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false -p:PublishTrimmed=false `
    -p:DebugType=none -p:DebugSymbols=false -o $hostFolder
if ($LASTEXITCODE -ne 0) { throw 'Native Host publish failed.' }

$extensionId = & (Join-Path $repo 'scripts/get-extension-id.ps1')
if ($extensionId -notmatch '^[a-p]{32}$') { throw 'Invalid extension ID.' }
if (-not ([IO.File]::ReadAllText((Join-Path $repo 'docs/browser-setup.txt')).Contains($extensionId))) {
    throw 'Browser setup guide does not match the extension ID.'
}
$allowlist = @{ chrome = $extensionId; edge = $extensionId } | ConvertTo-Json
[IO.File]::WriteAllText((Join-Path $hostFolder 'native-host-allowlist.json'),
    $allowlist, [Text.UTF8Encoding]::new($false))
$extensionFolder = Join-Path $package 'extension'
New-Item -ItemType Directory -Path $extensionFolder | Out-Null
foreach ($file in @('manifest.json', 'worker.js', 'popup.html', 'popup.js', 'README.md')) {
    Copy-Item -LiteralPath (Join-Path $repo "extension/$file") -Destination $extensionFolder
}
foreach ($file in @('wherefrom.exe', 'GUI/WhereFrom.App.exe',
    'NativeHost/WhereFrom.NativeHost.exe', 'extension/manifest.json')) {
    if (-not (Test-Path -LiteralPath (Join-Path $package $file))) {
        throw "Package is incomplete: $file"
    }
}

foreach ($name in @('LICENSE', 'README.md', 'README.zh-CN.md')) {
    Copy-Item -LiteralPath (Join-Path $repo $name) -Destination $package
}
Copy-Item -LiteralPath (Join-Path $repo 'docs/browser-setup.txt') -Destination (Join-Path $package 'BROWSER-SETUP.txt')
foreach ($script in @('register-native-host.ps1', 'unregister-native-host.ps1')) {
    Copy-Item -LiteralPath (Join-Path $repo "scripts/$script") -Destination $package
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
Copy-Item -LiteralPath (Join-Path $repo 'scripts/smoke-test.ps1') -Destination $package

$zip = Join-Path $artifacts 'WhereFrom-0.2.0-win-x64.zip'
Compress-Archive -LiteralPath $package -DestinationPath $zip -Force
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $artifacts 'SHA256SUMS.txt'),
    $hash + '  ' + [IO.Path]::GetFileName($zip) + [Environment]::NewLine,
    [Text.UTF8Encoding]::new($false))
Write-Output "Package: $zip"
Write-Output "Staging: $package"
