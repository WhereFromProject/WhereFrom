# Validate the Windows x64 release

This guide applies to the portable ZIP produced by `scripts/publish.ps1`. Use Windows 11 x64 on NTFS, in Windows Sandbox or another Windows machine without a .NET SDK/runtime installed. The package includes the runtime; installing .NET is not required.

## Prepare the package

1. On the development machine, run `./scripts/publish.ps1` from PowerShell in the repository. It creates `artifacts/WhereFrom-0.1.0-win-x64.zip` and `artifacts/SHA256SUMS.txt`.
2. Copy the ZIP and checksum into the clean machine. Compare `Get-FileHash .\WhereFrom-0.1.0-win-x64.zip -Algorithm SHA256` with SHA256SUMS.txt.
3. Extract the ZIP to a local writable NTFS directory, for example `C:\WhereFromTest`. Open PowerShell in the extracted `WhereFrom-win-x64` directory. No PATH change, installer or administrator privileges are required.
4. Record Windows version/architecture and whether .NET was installed before the test. Running on a development machine with DOTNET_ROOT changed is not a substitute for this check.
5. The EXE is unsigned. Record any SmartScreen or application-control block; do not remove MotW or change security settings as part of this verification.

Single-file .NET deployment extracts native runtime files under the user's temporary directory. The runtime needs that directory to be writable. This does not change the inspected files or their metadata.

## Automated package smoke check

The included script supports Windows PowerShell 5.1 and PowerShell 7:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\smoke-test.ps1 -Executable .\wherefrom.exe
$LASTEXITCODE
```

The execution-policy setting applies only to that PowerShell process; it does not unblock or modify any file. The script creates and deletes only its own temporary NTFS fixtures. It checks version/help, human-readable output, JSON/ConvertFrom-Json, Unicode paths, no metadata, non-recursive scanning, errors, rejected open schemes, and byte-for-byte file/ADS preservation. Expected final result: PASS and exit 0. It never opens a browser.

Run this check with networking disconnected to verify local functionality. You do not need to uninstall software or alter firewall rules; use a clean machine or Sandbox configuration appropriate to your environment.

## Manual command check

First confirm startup:

```powershell
.\wherefrom.exe --version
.\wherefrom.exe --help
```

Expect WhereFrom 0.1.0 and the single-file, JSON, scan, open and diagnostic commands.

Download a benign ZIP/PDF directly with the clean machine's browser. Using an existing executable as evidence is also valid: WhereFrom reads its metadata and never executes that file. Choose a path with Chinese characters if possible:

```powershell
$file = "$HOME\Downloads\downloaded.zip"
.\wherefrom.exe $file
$LASTEXITCODE
$result = .\wherefrom.exe $file --json | ConvertFrom-Json
$code = $LASTEXITCODE
$result
$code
.\wherefrom.exe scan "$HOME\Downloads"
$LASTEXITCODE
.\wherefrom.exe open $file
$LASTEXITCODE
```

Expected:
- Text and JSON agree on the available zone/source/referrer. JSON schemaVersion is 1; absent values are null.
- Scan includes first-level files only; counts reconcile. No read errors means exit 0 even when all sources are unknown.
- Open prints and opens Referrer, or Source if no parsed Referrer exists. Only HTTP/HTTPS is allowed. Confirm the actual page in the browser, not just exit 0.
- Open requires a configured default browser and connectivity to reach a website; a URL endpoint may redirect or download a file.

A copied/downloaded ZIP may lose evidence through transport or extraction. If the chosen evidence file has no URLs, open should return 1. To test browser dispatch without relying on browser metadata, create a fresh fixture:

```powershell
$fixture = Join-Path $env:TEMP ("wherefrom-page-" + [guid]::NewGuid() + ".txt")
try {
    Set-Content -LiteralPath $fixture -Value "Temporary validation file"
    Set-Content -LiteralPath $fixture -Stream Zone.Identifier -Value "[ZoneTransfer]`nZoneId=3`nHostUrl=https://example.com/file`nReferrerUrl=https://example.com/"
    .\wherefrom.exe open $fixture
    $LASTEXITCODE
} finally {
    Remove-Item -LiteralPath $fixture
}
```

Expect the example.com page and exit 0. These are synthetic test values, not browser observations. Never add or edit ADS on existing personal/downloaded files for testing.

Also check a local TXT without ADS (query exit 1), nonexistent path (exit 3), a directory passed as a file (exit 2), and existing symlink/junction entries if available. Report access errors separately from missing provenance.

## Record acceptance

Record package SHA256, Windows version, .NET installation state, shell version, each command's output/exit code, actual browser destination, and any security or runtime-extraction block. Redact private URLs/tokens and personal filenames before sharing results.

Clean-environment validation and the first GitHub Actions run remain pending until actual results are supplied. The workflow builds/tests/publishes and validates the extracted ZIP; it uploads workflow artifacts but does not create a GitHub Release.
