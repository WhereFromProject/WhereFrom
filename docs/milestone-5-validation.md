# Milestone 5 validation

Date: 2026-09-06

## Scope

M4 manual acceptance was confirmed by the user before this milestone. M5 adds only non-recursive, read-only text directory scanning. M2 domain types, the Windows provider/parser/reader, M3 single-file text formatter, and the M4 JSON DTO/contract are unchanged. No commit was created.

## Behavior and decisions

- Command: `wherefrom scan <directory>`.
- Sequential enumeration of the first level only. Hidden/system files are included; ordinary subdirectories are not entered, including inaccessible child directories.
- Child reparse points are skipped before invoking the provider. This includes file links, directory links/junctions and other reparse types. A root that is itself a reparse point is rejected (code 2); the user can supply its target directly.
- Referrer host, then Source host, then zone, then Unknown. Valid hostless URL evidence is labeled `URL recorded (no host)` rather than mislabeled Unknown. This is a display choice only; existing source/referrer values are not rewritten.
- Tab-separated FILE/SOURCE rows retain full filenames. Existing control escaping is used.
- Existing provider and formatter diagnostics/exit codes are reused internally. Errors get a filename prefix on stderr and an Error row, while subsequent files are still processed. Metadata warnings retain usable evidence.
- Counts: Scanned = Known provenance + Unknown + Errors. Skipped reparse points is separate; ordinary directories are excluded.
- Exit 0: completed scan, including empty/all-unknown results. Exit 2: invalid input, file instead of directory, or reparse-point root. Exit 3: root missing/denied. Exit 4: any expected per-file failure or directory enumeration failure; exit 5 takes precedence for unexpected failures.
- Before enumeration starts, root errors produce no stdout. If enumeration fails after reporting starts, stderr explicitly marks the scan incomplete; summary counts are partial.
- No sorting, concurrency, cache, extra dependencies or new domain abstractions.
- Per latest user instruction, no recursion option. Scan JSON is deferred: a batch report needs explicit failed-entry and summary semantics beyond M4's successful single-object contract. M4 single-file JSON remains unchanged.
- Existing bare filename `scan` and `scan --json` retain single-file meaning; `scan <directory>` is the new two-argument command.

## Validation

Local Windows 11 x64, G: NTFS, .NET SDK 10.0.400.

- `dotnet build`: PASS, 0 warnings, 0 errors.
- `dotnet test`: PASS, 188 passed, 0 failed, 0 skipped (11 Core, 177 Windows/CLI).
- 22 new tests cover source preference/fallbacks, mixed real ADS/no-ADS files, byte-for-byte read-only evidence, non-recursion, empty/missing/invalid directories, file input, rejected options, per-file failures with continuation, unexpected failure suppression, hidden/system long Unicode paths, locked ADS, real denied-file ACLs, denied root enumeration, and ignoring denied subdirectories.
- Original M3/M4 tests pass; only the help assertion is updated to list scan.
- Native executable test: a real junction into an outside fixture directory was skipped, the outside file was not reported or modified, and passing that junction as root returned 2.
- File symlink fixture creation failed with “Administrator privilege required for this operation.” No system settings were changed. File/directory symlink real-world verification remains manual; no claim is made that it passed.
- `git diff --check`: PASS.

## Performance observation

A disposable directory contained 1000 files: 500 with Zone.Identifier (zone, source and referrer), 500 without ADS, plus one junction pointing outside the directory. File creation and cleanup were excluded from timing.

The built Debug executable scanned serially in **284 ms**, including process startup and PowerShell output capture. Output: Scanned 1000, Known provenance 500, Unknown 500, Errors 0, Skipped reparse points 1; exit 0.

This is one local warm-filesystem observation, not a benchmark guarantee. All fixture writes and cleanup stayed under the test output directory.

## Manual verification required

From the repository root:

```powershell
$exe = ".\\src\\WhereFrom.Cli\\bin\\Debug\\net10.0-windows\\wherefrom.exe"
& $exe scan "$HOME\\Downloads"
$LASTEXITCODE
```

1. Downloads: compare a few rows against accepted single-file output. GitHub files should prefer github.com from Referrer over codeload.github.com from Source; an iLovePDF download should prefer www.ilovepdf.com when recorded as Referrer.
2. A directory with downloaded files, local TXT and a nested subdirectory: only first-level regular files appear; sum known + unknown + errors equals Scanned.
3. Empty directory: all counts zero, exit 0.
4. Nonexistent directory: no report, directory-not-found stderr, exit 3. File path as directory: exit 2.
5. If an already available inaccessible file is present, expect Error, filename-specific stderr, continued scanning and exit 4. Do not change permissions on personal files solely for this test.
6. If a directory containing existing file/directory symlinks or junctions is available, confirm they are skipped and their targets are not scanned. Symlink creation is not required. A link/junction used as root should return 2.
7. Repeat a single-file text query and a single-file `--json | ConvertFrom-Json` query; outputs should remain as accepted for M3/M4.
8. `scan <directory> --recursive` and `scan <directory> --json` should return usage on stderr and exit 2.

## User manual acceptance

The user confirmed M5 acceptance on 2026-09-06:

- Actual Downloads directory: 80 scanned, 64 known, 16 unknown, 0 errors, 0 skipped reparse points, exit 0.
- Local TXT plus a nested subdirectory: only the top-level TXT was reported; scanned 1, unknown 1, exit 0.
- Empty directory: all counts zero, exit 0.
- Nonexistent directory: Directory not found, exit 3.

The personal file inventory is intentionally omitted. These results do not add symlink or access-denied manual coverage.

## Known limitations

- No atomic filesystem snapshot. Entries may change between enumeration, attribute checks and inspection; reparse checks are not a security boundary against concurrent replacement or links in ancestor paths.
- All child reparse types are skipped, including cloud placeholders. Hard links are normal file entries and are not deduplicated.
- File/directory symlinks remain unverified on this host due fixture-creation privileges; junctions were verified.
- Enumeration order is unspecified; long filenames/Unicode display widths can cause tabular misalignment or wrapping.
- Existing ADS size/encoding/filesystem limitations remain. Network shares and non-NTFS combinations have not been broadly validated.
- No scan JSON, recursive scan, open, database/history, watcher, GUI, extensions or future features.
