param([Parameter(Mandatory = $true)][string]$Compiler)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$work = Join-Path $repo ('artifacts/upgrade-test-' + [guid]::NewGuid().ToString('N'))
$source = Join-Path $work 'source'
$legacyPackage = Join-Path $work 'legacy-package'
$legacyOutput = Join-Path $work 'legacy-output'
$newOutput = Join-Path $work 'new-output'
$install = Join-Path $work ('Installed ' + [char]0x4e2d + [char]0x6587)
$testRoot = 'Software\WhereFrom\InstallerTest'
$uninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\WhereFrom-InstallerTest_is1'
$database = Join-Path $env:LOCALAPPDATA 'WhereFrom\wherefrom.db'
$databaseHash = if (Test-Path -LiteralPath $database) {
    (Get-FileHash -LiteralPath $database -Algorithm SHA256).Hash
} else { $null }

if ([Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($testRoot)) {
    throw 'Test registry key already exists; refusing to overwrite it.'
}
New-Item -ItemType Directory -Path $legacyPackage, $legacyOutput, $newOutput | Out-Null
Expand-Archive -LiteralPath (Join-Path $repo 'artifacts/WhereFrom-0.2.0-win-x64.zip') -DestinationPath $source
$currentPackage = Join-Path $source 'WhereFrom-win-x64'
Get-ChildItem -LiteralPath $currentPackage -File |
    Where-Object { $_.Name -notin @('BROWSER-SETUP.txt', 'register-native-host.ps1', 'unregister-native-host.ps1') } |
    Copy-Item -Destination $legacyPackage

# The published v0.1.1 tag contains the CLI-only installer used as the upgrade baseline.
$legacyScript = & git -C $repo show 'v0.1.1:installer/WhereFrom.iss'
if ($LASTEXITCODE -ne 0 -or -not $legacyScript) { throw 'v0.1.1 installer source is unavailable.' }
$legacyPath = Join-Path $work 'WhereFrom-v0.1.1.iss'
[IO.File]::WriteAllLines($legacyPath, [string[]]$legacyScript, [Text.UTF8Encoding]::new($false))
& $Compiler '/DInstallerTest' "/DPackageDir=$legacyPackage" "/DOutputDir=$legacyOutput" $legacyPath | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Legacy installer compilation failed.' }
$extensionId = & (Join-Path $repo 'scripts/get-extension-id.ps1') -Manifest (Join-Path $currentPackage 'extension/manifest.json')
& $Compiler '/DInstallerTest' "/DPackageDir=$currentPackage" "/DOutputDir=$newOutput" "/DExtensionId=$extensionId" (Join-Path $repo 'installer/WhereFrom.iss') | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'v0.2.0 installer compilation failed.' }

function Run-Setup([string]$Path, [bool]$First) {
    $arguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART')
    if ($First) { $arguments += ('/DIR="' + $install + '"') }
    $process = Start-Process -FilePath $Path -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "Setup failed: $Path ($($process.ExitCode))" }
}

try {
    $envKey = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey("$testRoot\Environment")
    try { $envKey.SetValue('Path', 'C:\Keep', [Microsoft.Win32.RegistryValueKind]::String) }
    finally { $envKey.Dispose() }
    Run-Setup (Join-Path $legacyOutput 'WhereFrom-InstallerTest.exe') $true
    if (-not (Test-Path -LiteralPath (Join-Path $install 'wherefrom.exe')) -or
        (Test-Path -LiteralPath (Join-Path $install 'GUI\WhereFrom.App.exe'))) {
        throw 'Legacy CLI-only state was not established.'
    }
    Run-Setup (Join-Path $newOutput 'WhereFrom-InstallerTest.exe') $false
    $uninstall = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($uninstallKey)
    try {
        if ($null -eq $uninstall -or $uninstall.GetValue('InstallLocation').TrimEnd('\') -cne $install) {
            throw 'Upgrade did not retain the installation directory.'
        }
    } finally { if ($null -ne $uninstall) { $uninstall.Dispose() } }
    foreach ($relative in @('GUI\WhereFrom.App.exe', 'NativeHost\WhereFrom.NativeHost.exe',
        'NativeHost\native-host-allowlist.json', 'extension\manifest.json')) {
        if (-not (Test-Path -LiteralPath (Join-Path $install $relative))) { throw "Upgrade missing $relative" }
    }
    $menuCommand = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey("$testRoot\Classes\*\shell\WhereFrom\command")
    try {
        $expectedCommand = '"' + (Join-Path $install 'GUI\WhereFrom.App.exe') + '" "%1"'
        if ($null -eq $menuCommand -or $menuCommand.GetValue('') -cne $expectedCommand) {
            throw 'Upgrade did not register the Explorer file menu.'
        }
    } finally { if ($null -ne $menuCommand) { $menuCommand.Dispose() } }
    $envKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey("$testRoot\Environment")
    try {
        if ($envKey.GetValue('Path') -cne ('C:\Keep;' + $install)) { throw 'Upgrade duplicated or lost PATH.' }
    } finally { $envKey.Dispose() }
    & (Join-Path $install 'wherefrom.exe') --version
    if ($LASTEXITCODE -ne 0) { throw 'CLI failed after upgrade.' }
    Write-Output 'PASS: v0.1.1 CLI-only install upgraded in place with GUI, Native Host, and extension.'
}
finally {
    $uninstaller = Join-Path $install 'unins000.exe'
    if (Test-Path -LiteralPath $uninstaller) {
        $process = Start-Process -FilePath $uninstaller -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART') -WindowStyle Hidden -Wait -PassThru
        if ($process.ExitCode -ne 0) { throw "Upgrade test uninstall failed: $($process.ExitCode)" }
    }
    $envKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey("$testRoot\Environment")
    try {
        if ($null -ne $envKey -and $envKey.GetValue('Path') -cne 'C:\Keep') {
            throw 'Upgrade test uninstall damaged PATH.'
        }
    } finally { if ($null -ne $envKey) { $envKey.Dispose() } }
    $menu = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey("$testRoot\Classes\*\shell\WhereFrom")
    if ($null -ne $menu) { $menu.Dispose(); throw 'Upgrade uninstall left the Explorer file menu.' }
    [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree($testRoot, $false)
    $currentHash = if (Test-Path -LiteralPath $database) {
        (Get-FileHash -LiteralPath $database -Algorithm SHA256).Hash
    } else { $null }
    if ($currentHash -cne $databaseHash) { throw 'User provenance database changed during upgrade test.' }
}
Write-Output 'PASS: upgrade uninstall restored PATH and preserved the provenance database.'
