# Milestone 4 validation

Date: 2026-09-06

## Scope and prerequisites

The user confirmed M3 manual acceptance before M4. M2 domain types, the Windows reader/provider/parser, and M3 human-readable formatting remain unchanged. M4 adds only `wherefrom <file> --json`, its help entry, tests, and user documentation. No commit was created.

## JSON contract (schemaVersion 1)

A completed query writes exactly one JSON object and a newline to stdout. All six top-level properties are required and case-sensitive:

| Property | Type | Meaning |
| --- | --- | --- |
| schemaVersion | integer, fixed at 1 | Public contract version |
| path | string | Supplied file path, including relative spelling when supplied |
| hasProvenance | boolean | Whether the existing model contains usable evidence |
| zone | object or null | Available zone; null if missing or invalid |
| zone.id | integer | Nonnegative Int32 zone number, including unknown values |
| zone.name | string or null | Existing zone label; null for unknown numbers |
| sourceUrl | string or null | Existing parsed HostUrl, unchanged |
| referrerUrl | string or null | Existing parsed ReferrerUrl, unchanged |

When zone is an object, both id and name are required. Missing fields use explicit null; properties are not omitted. Unknown zone numbers do not receive guessed names. hasProvenance is not a trust assessment.

```json
{
  "schemaVersion": 1,
  "path": "C:\\Downloads\\example.zip",
  "hasProvenance": true,
  "zone": { "id": 3, "name": "Internet" },
  "sourceUrl": "https://example.com/download/example.zip",
  "referrerUrl": "https://example.com/download"
}
```

No-provenance queries have hasProvenance false and null zone/sourceUrl/referrerUrl. JSON string escaping is an encoding detail: decoded values preserve Unicode, percent escapes, query parameters, and fragments. Property ordering and whitespace are not a consumer requirement.

Exit codes remain 0 for usable provenance, 1 for no usable provenance, 2 for input errors, 3 for missing/denied files, 4 for read failures, and 5 for unexpected failures. Codes 0 and 1 produce JSON. Codes 2–5 leave stdout empty and report the error on stderr. Metadata warnings use stderr without contaminating JSON. Do not merge streams with `2>&1`.

Only the documented trailing `--json` form is supported. It does not combine with debug-zone/help/version. The dedicated CLI DTO explicitly names and maps fields; no domain object, provider name, issues collection, internal enum, or exception is serialized. The handler reuses the existing formatter with a null text writer for diagnostics and exit codes, preserving its implementation.

## Automated validation

Environment: local Windows 11 x64, G: NTFS, .NET SDK 10.0.400.

- `dotnet build`: PASS, 0 warnings, 0 errors.
- `dotnet test`: PASS, 166 passed, 0 failed, 0 skipped (11 Core, 155 Windows/CLI).
- 22 new JSON cases cover complete and partial metadata; absent, empty, and malformed streams; stable property names and version; explicit nulls; unknown zone names; Unicode paths/URLs; URL percent escapes; quotes, backslashes and controls; warning separation; error/exit-code parity; exception detail suppression; invalid syntax; and real missing files/directories.
- Real NTFS fixtures verify body and ADS byte equality before/after JSON inspection, including browser-style trailing U+0000.
- Existing M3 text-output tests still pass. Only the help expectation changes to include the added JSON command.
- `git diff --check`: PASS.

## Actual PowerShell process validation

The built executable was invoked from Windows PowerShell and piped directly to `ConvertFrom-Json`. Disposable fixtures under the test output directory were created and removed.

Verified: schemaVersion, Unicode/emoji path and URL round-trip, sourceUrl, zone, exit 0; local no-ADS file with null values and exit 1; malformed URL with valid zone and separate stderr warning; missing file with empty stdout and exit 3; unchanged file body and ADS after inspection.

These controlled fixtures do not replace the user's browser-download acceptance checks.

## Manual verification required

Build once, then run from the repository root:

```powershell
$exe = ".\\src\\WhereFrom.Cli\\bin\\Debug\\net10.0-windows\\wherefrom.exe"
$file = "C:\\path\\to\\downloaded.zip"
$result = & $exe $file --json | ConvertFrom-Json
$code = $LASTEXITCODE
$result
$result.sourceUrl
$result.referrerUrl
$result.zone
$code
& $exe $file
```

1. Use existing Chrome and Edge downloads (ZIP, PDF or EXE), including a Chinese/emoji filename if available. Expect version 1 and the same available fields as text mode, with distinct Source and Referrer preserved. URL query parameters and fragments should remain intact.
2. Use a locally created TXT without ADS. Expect hasProvenance false, three null evidence fields and exit 1.
3. Use a nonexistent path. Expect no stdout/JSON, a missing-file message on stderr and exit 3.
4. Compare plain text output with accepted M3 behavior. Existing file and MotW data must remain unchanged.

M4 user manual acceptance confirmed on 2026-09-06. User-reported PowerShell results: GitHub ZIP returned schemaVersion 1, zone 3/Internet, separate codeload Source and GitHub releases Referrer, exit 0; local TXT returned false/null evidence and exit 1; missing file reported File not found and exit 3 with no JSON; a Chinese-named PDF returned zone 3 and separate API download Source / human-facing iLovePDF Referrer, exit 0. Private download tokens are not reproduced here.

## Known limitations and exclusions

JSON reports existing M2 evidence only. It does not infer missing history or repair metadata. Errors have no JSON envelope; consumers must check the exit code. Parser and filesystem limitations from M2/M3 remain, including non-atomic reads, the 64 KiB metadata limit, encoding limits, and unverified filesystem/network-share combinations. Default JSON escaping can display Unicode as escapes until decoded.

No scan, open-source command, database, GUI, browser extension, service, tracking, AI, cloud, or telemetry was added.
