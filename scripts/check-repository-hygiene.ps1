param([string]$Package)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$tracked = @(git -C $repo ls-files)
if ($LASTEXITCODE -ne 0) { throw 'Unable to enumerate tracked files.' }

$forbiddenSource = @($tracked | Where-Object {
    $_ -match '(^|/)(bin|obj|TestResults|artifacts|\.vs|\.idea)/' -or
    $_ -match '^docs/(phase-|milestone-|installer-validation\.md$|gui-mvp-|gui-build-troubleshooting\.md$)' -or
    $_ -match '^scripts/browser-(close-test|test-extension)\.mjs$' -or
    $_ -match '(^|/)(\.env(?:\.|$)|MILESTONE v0\.1\.md$)' -or
    $_ -match '\.(db(?:-.+)?|sqlite(?:3)?(?:-.+)?|log|tmp|temp|bak|trx|coverage|coveragexml|lcov|exe|dll|pdb|zip|7z|msi|msix|pfx|p12|pem|key)$'
})
if ($forbiddenSource.Count -gt 0) {
    $forbiddenSource | ForEach-Object { Write-Error "Tracked local/intermediate file: $_" }
    throw 'Repository hygiene check failed.'
}
Write-Output "Tracked-file hygiene passed ($($tracked.Count) source files)."

if ($Package) {
    $resolved = (Resolve-Path -LiteralPath $Package).Path
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($resolved)
    try {
        $entries = @($archive.Entries | ForEach-Object FullName)
        $forbiddenPackage = @($entries | Where-Object {
            $_ -match '(^|/)(docs|bin|obj|TestResults|artifacts)/' -or
            $_ -match '(^|/)(ENGINEERING\.md|AGENTS\.md|\.env(?:\.|$))' -or
            $_ -match '(phase-|milestone-|gui-mvp-|installer-validation)' -or
            $_ -match '\.(db(?:-.+)?|sqlite(?:3)?(?:-.+)?|log|trx|pfx|p12|pem|key)$'
        })
        if ($forbiddenPackage.Count -gt 0) {
            $forbiddenPackage | ForEach-Object { Write-Error "Packaged internal file: $_" }
            throw 'Package hygiene check failed.'
        }
        foreach ($required in @(
            'WhereFrom-win-x64/wherefrom.exe',
            'WhereFrom-win-x64/GUI/WhereFrom.App.exe',
            'WhereFrom-win-x64/NativeHost/WhereFrom.NativeHost.exe',
            'WhereFrom-win-x64/extension/manifest.json',
            'WhereFrom-win-x64/BROWSER-SETUP.txt',
            'WhereFrom-win-x64/LICENSE')) {
            if ($required -notin $entries) { throw "Package missing required file: $required" }
        }
        Write-Output "Package hygiene passed ($($entries.Count) entries)."
    }
    finally { $archive.Dispose() }
}