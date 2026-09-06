# Milestone 6 validation

Date: 2026-09-06

## Scope and prerequisites

The user confirmed M5 manual acceptance. Only the open-source-page command, its tests, and related documentation were added. M2 domain types, the parser/provider/reader, M3 formatter, M4 JSON contract, and M5 scan implementation remain unchanged. No commit was created.

## Behavior and design decisions

- `wherefrom open <file>` inspects the file through the existing provider.
- Select the parsed ReferrerUrl first; use SourceUrl only if ReferrerUrl is null. Parser-rejected fields remain null with the existing metadata warning; no parser behavior was changed.
- Validate the selected URL as absolute HTTP/HTTPS with a host and explicit scheme:// prefix. Reject controls, whitespace, backslashes, malformed percent escapes, unpaired surrogates and invalid raw URI characters rather than allowing normalization to hide them.
- A selected unsupported scheme is refused, even if another HTTP/HTTPS URL is available. No custom, file, javascript, data or shell protocol is dispatched.
- Windows launch settings use ProcessStartInfo with the exact validated URL as FileName, UseShellExecute true, Verb open, and no arguments. No cmd.exe, PowerShell or command string interpolation is used. Windows chooses the registered HTTP/HTTPS handler.
- Output: Opening: followed by the selected URL. A missing URL prints No source URL is available for this file.
- Exit codes: 0 successful dispatch; 1 no URL; 2 refused URL or invalid input; 3 missing/denied file; 4 read or expected browser-launch failure; 5 unexpected failure. Diagnostics use stderr and do not expose exception messages or stack traces.
- A launch failure may follow the Opening output, which announces an attempt, not successful page loading.
- An Action<ProcessStartInfo> test callback intercepts launches, allowing command routing, URL validation and exact launch settings to be exercised without opening browsers.
- Existing bare filename `open` and `open --json` retain single-file query meaning. The new command is `open <file>`. There are no new flags.
- Browser dispatch may contact websites. The application does not fetch URLs itself, alter evidence, remove MotW, or validate the safety/availability of remote content.

## Validation

Environment: local Windows 11 x64 / G: NTFS / .NET SDK 10.0.400.

- `dotnet build`: PASS, 0 warnings, 0 errors.
- `dotnet test`: PASS, 229 passed, 0 failed, 0 skipped (11 Core, 218 Windows/CLI).
- 41 new cases: HTTP/HTTPS, uppercase scheme, Unicode/emoji and escaped URLs, shell-like punctuation as URL data with no arguments, Referrer preference, Source fallback, unsupported schemes, invalid URIs/controls, no URL, read errors, launch failure, unexpected failure, invalid syntax, and preserved single-file behavior for a filename named open.
- Real NTFS fixtures pass through the provider and command with intercepted launches; body and ADS bytes remain identical, including browser-style trailing NUL data.
- The actual executable was run on disposable fixtures: no URL returned 1 and the expected message; a file-scheme Referrer plus HTTPS Source returned 2 with no fallback or stdout; evidence remained unchanged.
- Existing M3/M4/M5 regression tests pass.
- `git diff --check`: PASS.

Automated tests do not prove a default browser actually opens or navigates correctly. That real Windows integration remains a manual acceptance item; no browser was launched during automated verification.

## Manual verification required

From the repository root:

```powershell
$exe = ".\\src\\WhereFrom.Cli\\bin\\Debug\\net10.0-windows\\wherefrom.exe"
& $exe open "$HOME\\Downloads\\VeneraX-2.3.2.zip"
$LASTEXITCODE
```

1. The existing GitHub ZIP should print its recorded GitHub releases Referrer and open that page rather than the codeload Source. Expect exit 0.
2. Use a downloaded PDF with a human-facing Referrer, including a Chinese filename. Expect the printed Referrer and the browser destination to agree.
3. Use an existing file with only an HTTP/HTTPS Source. Expect that exact Source to be dispatched. A CDN/download endpoint may start a browser download.
4. Use a local TXT without ADS or a file containing only ZoneId. Expect no browser action, the no-URL message, and exit 1.
5. Missing file: expect stderr File not found, exit 3, no browser action.
6. For unsupported-protocol checking, use only a new disposable fixture, never rewrite ADS on an existing download:

```powershell
$fixture = Join-Path $env:TEMP ("wherefrom-open-" + [guid]::NewGuid() + ".txt")
try {
    Set-Content -LiteralPath $fixture -Value "test fixture"
    Set-Content -LiteralPath $fixture -Stream Zone.Identifier -Value "[ZoneTransfer]`nReferrerUrl=file:///C:/Windows/notepad.exe`nHostUrl=https://example.com"
    & $exe open $fixture
    $LASTEXITCODE
} finally {
    Remove-Item -LiteralPath $fixture
}
```

Expect refusal, exit 2, no Notepad/browser action and no fallback to example.com. The fixture itself is created and removed solely for this test.

## Known limitations and exclusions

- Windows protocol association and browser configuration determine actual handling. Dispatch success does not prove page load, connectivity, or page safety.
- Browser redirects, expired URLs and download endpoints are outside this command's control.
- Existing parsing/read-size/filesystem limitations remain; malformed fields discarded by the existing parser cannot be selected.
- Existing M5 symlink manual-verification limitation remains; this milestone does not alter scanning.
- No browser extension, URL probing, protocol override, open JSON mode, release packaging, CI or M7 work was added.
