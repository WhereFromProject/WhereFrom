$ErrorActionPreference = 'Stop'
$hostName = 'org.wherefrom.provenance'
$folder = Join-Path $env:LOCALAPPDATA 'WhereFrom'
foreach ($browser in @('chrome', 'edge')) {
    $vendor = if ($browser -eq 'chrome') { 'Google\Chrome' } else { 'Microsoft\Edge' }
    $registryPath = "HKCU:\Software\$vendor\NativeMessagingHosts\$hostName"
    $manifestPath = Join-Path $folder "native-host-$browser.json"
    if (Test-Path -LiteralPath $registryPath) {
        $registered = (Get-Item -LiteralPath $registryPath).GetValue('')
        if ($registered -eq $manifestPath) {
            Remove-Item -LiteralPath $registryPath
            if (Test-Path -LiteralPath $manifestPath) { Remove-Item -LiteralPath $manifestPath }
            Write-Output "Unregistered $browser host."
        }
    }
}
