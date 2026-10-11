param([Parameter(Mandatory = $true)][string]$Compiler)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$work = Join-Path $repo ('artifacts/installer-test-' + [guid]::NewGuid().ToString('N'))
Expand-Archive -LiteralPath (Join-Path $repo 'artifacts/WhereFrom-preview-win-x64.zip') -DestinationPath $work
$package = Join-Path $work 'WhereFrom-win-x64'
$extensionId = & (Join-Path $repo 'scripts/get-extension-id.ps1') -Manifest (Join-Path $package 'extension/manifest.json')
& $Compiler "/DInstallerTest" "/DPackageDir=$package" "/DOutputDir=$work" "/DExtensionId=$extensionId" (Join-Path $repo 'installer/WhereFrom.iss')
if ($LASTEXITCODE -ne 0) { throw 'Test installer compilation failed.' }
$setup = Join-Path $work 'WhereFrom-InstallerTest.exe'
$install = Join-Path $work ('Installed ' + [char]0x4e2d + [char]0x6587)
$testRoot = 'Software\WhereFrom\InstallerTest'
if ([Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($testRoot)) { throw 'Test registry key already exists; refusing to overwrite it.' }
$userEnvironment = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Environment')
$rawOption = [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames
$realPath = $userEnvironment.GetValue('Path', $null, $rawOption)
$realKind = if ($null -ne $realPath) { $userEnvironment.GetValueKind('Path') } else { $null }
$userEnvironment.Dispose()
$database = Join-Path $env:LOCALAPPDATA 'WhereFrom\wherefrom.db'
$databaseHash = if (Test-Path -LiteralPath $database) { (Get-FileHash -LiteralPath $database -Algorithm SHA256).Hash } else { $null }
function Run-Setup {
    param([switch]$UsePreviousDirectory)
    $arguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART')
    if (-not $UsePreviousDirectory) { $arguments += ('/DIR="' + $install + '"') }
    $p = Start-Process -FilePath $setup -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
    if ($p.ExitCode -ne 0) { throw "Setup failed: $($p.ExitCode)" }
    foreach ($browser in @('Chrome', 'Edge')) {
        $lower = $browser.ToLowerInvariant()
        $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey("$testRoot\${browser}NativeMessagingHosts\org.wherefrom.provenance")
        try {
            $manifestPath = Join-Path $install "NativeHost\native-host-$lower.json"
            if ($null -eq $key -or $key.GetValue('') -cne $manifestPath) { throw "$browser Native Host registration is wrong." }
            $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
            if ($manifest.path -cne (Join-Path $install 'NativeHost\WhereFrom.NativeHost.exe') -or
                $manifest.allowed_origins[0] -cne "chrome-extension://$extensionId/") {
                throw "$browser Native Host manifest is wrong."
            }
        } finally { if ($null -ne $key) { $key.Dispose() } }
    }
    $menu = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey("$testRoot\Classes\*\shell\WhereFrom")
    $menuCommand = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey("$testRoot\Classes\*\shell\WhereFrom\command")
    try {
        $expectedCommand = '"' + (Join-Path $install 'GUI\WhereFrom.App.exe') + '" "%1"'
        if ($null -eq $menu -or $null -eq $menuCommand -or
            $menu.GetValue('') -cne '查看文件来源 (WhereFrom)' -or
            $menu.GetValue('MultiSelectModel') -cne 'Single' -or
            $menuCommand.GetValue('') -cne $expectedCommand) {
            throw 'Explorer file-menu registration or quoted file parameter is wrong.'
        }
    } finally {
        if ($null -ne $menu) { $menu.Dispose() }
        if ($null -ne $menuCommand) { $menuCommand.Dispose() }
    }
    foreach ($relative in @('GUI\WhereFrom.App.exe', 'NativeHost\native-host-allowlist.json',
        'extension\manifest.json', 'BROWSER-SETUP.txt')) {
        if (-not (Test-Path -LiteralPath (Join-Path $install $relative))) { throw "Missing installed $relative" }
    }
}
function Run-Uninstall {
    $p = Start-Process -FilePath (Join-Path $install 'unins000.exe') -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART') -WindowStyle Hidden -Wait -PassThru
    if ($p.ExitCode -ne 0) { throw "Uninstall failed: $($p.ExitCode)" }
    $menu = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey("$testRoot\Classes\*\shell\WhereFrom")
    if ($null -ne $menu) { $menu.Dispose(); throw 'Explorer file-menu registration remained after uninstall.' }
    $edge = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey("$testRoot\EdgeNativeMessagingHosts\org.wherefrom.provenance")
    if ($null -ne $edge) { $edge.Dispose(); throw 'Edge Native Host registration remained after uninstall.' }
    $chrome = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey("$testRoot\ChromeNativeMessagingHosts\org.wherefrom.provenance")
    if ($null -ne $chrome) {
        try {
            if ($chrome.GetValue('') -cne 'C:\PreviouslyRegistered\host.json') {
                throw 'Chrome Native Host registration was not restored.'
            }
        } finally { $chrome.Dispose() }
    }
}
try {
    foreach ($kind in @([Microsoft.Win32.RegistryValueKind]::String, [Microsoft.Win32.RegistryValueKind]::ExpandString)) {
        $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey("$testRoot\Environment")
        try {
            $oldHost = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey("$testRoot\ChromeNativeMessagingHosts\org.wherefrom.provenance")
            $oldHost.SetValue('', 'C:\PreviouslyRegistered\host.json')
            $oldHost.Dispose()
            $original = '%USERPROFILE%\existing;;C:\Keep This\;'
            $key.SetValue('Path', $original, $kind)
            Run-Setup
            $sample = Join-Path $work '右键 样本.txt'
            [IO.File]::WriteAllText($sample, 'Explorer argument smoke test')
            & (Join-Path $repo 'scripts/smoke-test-gui.ps1') -Executable (Join-Path $install 'GUI\WhereFrom.App.exe') -FilePath $sample
            $expected = $original + ';' + $install
            if ($key.GetValue('Path', '', $rawOption) -cne $expected -or $key.GetValueKind('Path') -ne $kind) { throw 'PATH text/type not preserved.' }
            Run-Setup -UsePreviousDirectory
            if ($key.GetValue('Path', '', $rawOption) -cne $expected) { throw 'Duplicate PATH after reinstall.' }
            $uninstallKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Uninstall\WhereFrom-InstallerTest_is1')
            try {
                if ($null -eq $uninstallKey -or $uninstallKey.GetValue('InstallLocation').TrimEnd('\') -cne $install) { throw 'Reinstall did not retain the custom installation directory.' }
            } finally { if ($null -ne $uninstallKey) { $uninstallKey.Dispose() } }
            & (Join-Path $install 'wherefrom.exe') --version
            if ($LASTEXITCODE -ne 0) { throw 'Installed executable failed.' }
            $start = [Diagnostics.ProcessStartInfo]::new("$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe", '-NoProfile -Command "wherefrom --version"')
            $start.UseShellExecute = $false
            $start.CreateNoWindow = $true
            $start.RedirectStandardOutput = $true
            $start.WorkingDirectory = $work
            $start.Environment['PATH'] = [Environment]::ExpandEnvironmentVariables($expected) + ';' + [Environment]::GetEnvironmentVariable('PATH', 'Machine')
            $child = [Diagnostics.Process]::Start($start)
            try {
                $version = $child.StandardOutput.ReadToEnd().Trim()
                $child.WaitForExit()
                if ($child.ExitCode -ne 0 -or $version -ne 'WhereFrom 0.1.1') { throw 'Bare command resolution from another directory failed.' }
            } finally { $child.Dispose() }
            [IO.File]::WriteAllText((Join-Path $install 'keep-user-file.txt'), 'preserve me')
            $key.SetValue('Path', $expected + ';C:\LaterAddition', $kind)
            Run-Uninstall
            $oldHost = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey(
                "$testRoot\ChromeNativeMessagingHosts\org.wherefrom.provenance", $true)
            $oldHost.DeleteValue('')
            $oldHost.Dispose()
            [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKey(
                "$testRoot\ChromeNativeMessagingHosts\org.wherefrom.provenance")
            if ($key.GetValue('Path', '', $rawOption) -cne ($original + ';C:\LaterAddition') -or $key.GetValueKind('Path') -ne $kind) { throw 'Uninstall damaged PATH.' }
            if (-not (Test-Path (Join-Path $install 'keep-user-file.txt'))) { throw 'Uninstall deleted an unrelated file.' }
            [IO.File]::Delete((Join-Path $install 'keep-user-file.txt'))
            Write-Output "PASS: $kind install/reinstall/uninstall, PATH preservation, unrelated-file preservation."
        } finally { $key.Dispose() }
    }
    $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey("$testRoot\Environment")
    try {
        $original = 'C:\Keep;"' + $install.ToUpperInvariant() + '\";C:\Keep2'
        $key.SetValue('Path', $original, [Microsoft.Win32.RegistryValueKind]::String)
        Run-Setup
        Run-Uninstall
        if ($key.GetValue('Path', '', $rawOption) -cne $original) { throw 'Pre-existing PATH entry changed.' }
        Write-Output 'PASS: pre-existing case/quote/trailing-slash entry is neither duplicated nor removed.'
        $key.DeleteValue('Path')
        Run-Setup
        if ($key.GetValue('Path') -cne $install) { throw 'Absent PATH install failed.' }
        Run-Uninstall
        if ($key.GetValue('Path') -ne '') { throw 'Absent PATH uninstall failed.' }
        Write-Output 'PASS: previously absent user PATH.'
    } finally { $key.Dispose() }
} finally {
    # Only remove our isolated test registry subtree after uninstall has removed test application files.
    if (Test-Path (Join-Path $install 'unins000.exe')) { Run-Uninstall }
    [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree($testRoot, $false)
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Environment')
    try {
        if ($key.GetValue('Path', $null, $rawOption) -cne $realPath) { throw 'Real user PATH changed!' }
        if ($null -ne $realKind -and $key.GetValueKind('Path') -ne $realKind) { throw 'Real user PATH type changed!' }
    } finally { $key.Dispose() }
    $currentHash = if (Test-Path -LiteralPath $database) { (Get-FileHash -LiteralPath $database -Algorithm SHA256).Hash } else { $null }
    if ($currentHash -cne $databaseHash) { throw 'User provenance database changed during installer test.' }
}
Write-Output 'PASS: all tests used isolated registry keys; real user PATH and provenance database unchanged.'
