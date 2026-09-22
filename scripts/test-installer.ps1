param([Parameter(Mandatory = $true)][string]$Compiler)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$work = Join-Path $repo ('artifacts/installer-test-' + [guid]::NewGuid().ToString('N'))
Expand-Archive -LiteralPath (Join-Path $repo 'artifacts/WhereFrom-0.1.0-win-x64.zip') -DestinationPath $work
$package = Join-Path $work 'WhereFrom-win-x64'
& $Compiler "/DInstallerTest" "/DPackageDir=$package" "/DOutputDir=$work" (Join-Path $repo 'installer/WhereFrom.iss')
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
function Run-Setup {
    param([switch]$UsePreviousDirectory)
    $arguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART')
    if (-not $UsePreviousDirectory) { $arguments += ('/DIR="' + $install + '"') }
    $p = Start-Process -FilePath $setup -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
    if ($p.ExitCode -ne 0) { throw "Setup failed: $($p.ExitCode)" }
}
function Run-Uninstall {
    $p = Start-Process -FilePath (Join-Path $install 'unins000.exe') -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART') -WindowStyle Hidden -Wait -PassThru
    if ($p.ExitCode -ne 0) { throw "Uninstall failed: $($p.ExitCode)" }
}
try {
    foreach ($kind in @([Microsoft.Win32.RegistryValueKind]::String, [Microsoft.Win32.RegistryValueKind]::ExpandString)) {
        $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey("$testRoot\Environment")
        try {
            $original = '%USERPROFILE%\existing;;C:\Keep This\;'
            $key.SetValue('Path', $original, $kind)
            Run-Setup
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
                if ($child.ExitCode -ne 0 -or $version -ne 'WhereFrom 0.1.0') { throw 'Bare command resolution from another directory failed.' }
            } finally { $child.Dispose() }
            [IO.File]::WriteAllText((Join-Path $install 'keep-user-file.txt'), 'preserve me')
            $key.SetValue('Path', $expected + ';C:\LaterAddition', $kind)
            Run-Uninstall
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
}
Write-Output 'PASS: all tests used isolated registry keys; real user PATH unchanged.'
