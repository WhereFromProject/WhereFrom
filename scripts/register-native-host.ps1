param(
    [Parameter(Mandatory = $true)][string]$HostExe,
    [string]$ChromeExtensionId,
    [string]$EdgeExtensionId
)

$ErrorActionPreference = 'Stop'
$hostName = 'org.wherefrom.provenance'
$ids = @{}
foreach ($browser in @('chrome', 'edge')) {
    $id = if ($browser -eq 'chrome') { $ChromeExtensionId } else { $EdgeExtensionId }
    if (-not [string]::IsNullOrWhiteSpace($id)) {
        if ($id -cnotmatch '^[a-p]{32}$') { throw "Invalid $browser extension ID." }
        $ids[$browser] = $id
    }
}
if ($ids.Count -eq 0) { throw 'Provide -ChromeExtensionId and/or -EdgeExtensionId.' }
$resolvedExe = (Resolve-Path -LiteralPath $HostExe).Path
if ([IO.Path]::GetExtension($resolvedExe) -ine '.exe') { throw 'HostExe must be a published .exe.' }
$sidecarPath = Join-Path (Split-Path -Parent $resolvedExe) 'native-host-allowlist.json'
if (Test-Path -LiteralPath $sidecarPath) {
    $sidecar = Get-Content -LiteralPath $sidecarPath -Raw | ConvertFrom-Json
    foreach ($browser in $ids.Keys) {
        if ($sidecar.$browser -cne $ids[$browser]) {
            throw "The packaged Native Host only accepts its bundled $browser extension ID."
        }
    }
}
$folder = Join-Path $env:LOCALAPPDATA 'WhereFrom'
New-Item -ItemType Directory -Force -Path $folder | Out-Null
$registrations = @{}
foreach ($browser in $ids.Keys) {
    $vendor = if ($browser -eq 'chrome') { 'Google\Chrome' } else { 'Microsoft\Edge' }
    $registryPath = "HKCU:\Software\$vendor\NativeMessagingHosts\$hostName"
    $manifestPath = Join-Path $folder "native-host-$browser.json"
    if (Test-Path -LiteralPath $registryPath) {
        $registered = (Get-Item -LiteralPath $registryPath).GetValue('')
        if ($registered -cne $manifestPath) {
            throw "$browser already has a different WhereFrom Native Host registration. Remove it before registering this package."
        }
    }
    if (Test-Path -LiteralPath $manifestPath) {
        $existingManifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        if ($existingManifest.path -cne $resolvedExe) {
            throw "$browser already has a different WhereFrom Native Host manifest. Remove it before registering this package."
        }
    }
    $registrations[$browser] = @{ registryPath = $registryPath; manifestPath = $manifestPath }
}
if (-not (Test-Path -LiteralPath $sidecarPath)) {
    $config = @{}
    $configPath = Join-Path $folder 'native-host-allowlist.json'
    if (Test-Path -LiteralPath $configPath) {
        $existing = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
        foreach ($browser in @('chrome', 'edge')) {
            if ($existing.$browser -cmatch '^[a-p]{32}$') { $config[$browser] = $existing.$browser }
        }
    }
    foreach ($browser in $ids.Keys) { $config[$browser] = $ids[$browser] }
    [IO.File]::WriteAllText($configPath, ($config | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
}
foreach ($browser in $ids.Keys) {
    $manifestPath = $registrations[$browser].manifestPath
    $manifest = @{
        name = $hostName
        description = 'WhereFrom browser download provenance'
        path = $resolvedExe
        type = 'stdio'
        allowed_origins = @("chrome-extension://$($ids[$browser])/")
    }
    [IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
    $registryPath = $registrations[$browser].registryPath
    New-Item -Path $registryPath -Force | Out-Null
    Set-Item -LiteralPath $registryPath -Value $manifestPath
    Write-Output "Registered $browser host for $($ids[$browser])."
}
