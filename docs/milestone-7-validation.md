# Milestone 7 release readiness

Work performed: 2026-09-06–2026-09-07.

## Status

Local M7 preparation is complete. The portable release candidate and Windows workflow are ready for review. **The full v0.1 Definition of Done is not yet satisfied:** clean Windows validation and an actual GitHub Actions build/test run remain pending. No commit, push, tag, GitHub Release, or public upload was performed.

The user confirmed M6 real-browser acceptance; see milestone-6-validation.md. M5 manual results also remain recorded in milestone-5-validation.md.

## Delivered capabilities

- Single-file text query and raw Zone.Identifier diagnostic.
- Stable schemaVersion 1 single-file JSON.
- Read-only, non-recursive first-level directory scanning.
- Explicit opening of a validated HTTP/HTTPS Referrer, or Source when no parsed Referrer is available.
- Help/version, existing exit codes and safe diagnostics.
- Windows x64 self-contained single-file package, checksum, repeatable packaging and package smoke script.
- Windows-only CI workflow with restore, Release build/test, publish, extracted-package smoke validation and artifact upload.

M7 does not change any existing C# implementation or domain/JSON contract. Validation additions exercise the packaged executable, not only a development build.

## Review findings and decisions

- No TODO/FIXME entries, production third-party package references, unused application frameworks, or Windows APIs in Core were found.
- Existing direct URL character validation in the parser and opener serves different boundaries: evidence parsing accepts other schemes, while opening permits only HTTP/HTTPS. It was kept intact instead of merging or generalizing the stable behavior.
- Shared human formatter diagnostics are still reused by JSON/scan/open; no new error framework was introduced.
- File streams, text readers, native SafeFileHandle, directory enumerator, and launch Process objects use scoped disposal. Reads use open/read access with no file/MotW writes.
- Unexpected-exception boundaries suppress private exception text. Per-file scan errors continue independently and do not become Unknown.
- Operations are synchronous and non-recursive. There is no cancellation-token infrastructure; Ctrl+C terminates the CLI, with OS handle cleanup. Blocking filesystem operations and concurrent changes remain limitations.
- Single-file publishing uses no trimming or AOT, preserving the existing reflection-based JSON DTO serialization. Native runtime extraction is documented.
- Tests remain xUnit; no extra test framework or snapshot infrastructure was added.
- Publish stages use fresh directories and never recursively delete user-supplied locations. Generated outputs are ignored by Git.
- Package smoke fixtures write ADS through the PowerShell provider for compatibility with both Windows PowerShell 5.1 and PowerShell 7. The .NET Framework file API in PowerShell 5.1 rejected direct ADS paths during script testing; this was a script issue, not a change to application Windows behavior.

## Source structure

```text
src/
  WhereFrom.Core/
    WhereFrom.Core.csproj
    IProvenanceProvider.cs
    ProvenanceResult.cs
    ProvenanceIssue.cs
  WhereFrom.Platform.Windows/
    WhereFrom.Platform.Windows.csproj
    ZoneIdentifierReader.cs
    ZoneIdentifierParser.cs
    WindowsZoneProvider.cs
    NamedStreamSupport.cs
    BrowserLauncher.cs
  WhereFrom.Cli/
    WhereFrom.Cli.csproj
    Program.cs
    CommandLine.cs
    ProvenanceFormatter.cs
    ProvenanceJson.cs
    TerminalText.cs
    ScanCommand.cs
    OpenCommand.cs
    Properties/PublishProfiles/win-x64.pubxml
tests/
  WhereFrom.Core.Tests/
    ProjectBoundaryTests.cs
    ProvenanceResultTests.cs
  WhereFrom.Platform.Windows.Tests/
    TemporaryZoneFile.cs
    ProjectBoundaryTests.cs
    ZoneIdentifierReaderTests.cs
    ZoneIdentifierParserTests.cs
    WindowsZoneProviderTests.cs
    NamedStreamSupportTests.cs
    DebugZoneCommandTests.cs
    FileCommandTests.cs
    ProvenanceFormatterTests.cs
    JsonCommandTests.cs
    ScanCommandTests.cs
    OpenCommandTests.cs
scripts/
  publish.ps1
  smoke-test.ps1
.github/workflows/windows.yml
```

Core owns platform-independent evidence types. Platform.Windows owns ADS/provider/native capability and browser dispatch settings. CLI owns commands and presentation. Test projects retain their existing project files/dependency directions.

## Dependencies

Runtime:
- Microsoft.NETCore.App 10.0.11 bundled for win-x64 in this local candidate.
- Windows system APIs and registered HTTP/HTTPS handler.
- No additional production PackageReference, database or service.
- System.Text.Json and process/file APIs come from .NET.
- .NET runtime license and third-party notices are copied from the exact downloaded runtime package.

Build:
- .NET SDK 10.0.400 locally. global.json selects stable .NET 10.0 feature bands starting at 10.0.100; CI requests 10.0.x.
- Microsoft.NET.ILLink.Tasks 10.0.11 is SDK-added publishing tooling; PublishTrimmed is false.
- PowerShell 5.1 or 7 for the package smoke script; packaging was run with PowerShell 7.

Tests (direct):
- Microsoft.NET.Test.Sdk 17.14.1: test discovery/execution infrastructure.
- xunit 2.9.3: assertions and tests.
- xunit.runner.visualstudio 3.1.4: VSTest adapter.

Resolved test-only transitive packages:
- Microsoft.CodeCoverage 17.14.1
- Microsoft.TestPlatform.ObjectModel 17.14.1
- Microsoft.TestPlatform.TestHost 17.14.1
- Newtonsoft.Json 13.0.3
- xunit.abstractions 2.0.3
- xunit.analyzers 1.18.0
- xunit.assert / xunit.core / xunit.extensibility.core / xunit.extensibility.execution 2.9.3

CI uses actions/checkout v6, actions/setup-dotnet v5 and actions/upload-artifact v4. These are build services, not application runtime dependencies. Workflow permission is contents: read.

## Validation completed locally

Environment: Windows 11 x64 build 26200, local NTFS, .NET SDK 10.0.400.

| Check | Result |
| --- | --- |
| dotnet build | PASS, 0 warnings, 0 errors |
| dotnet test | PASS, 229 passed, 0 failed, 0 skipped |
| dotnet build -c Release | PASS, 0 warnings, 0 errors |
| dotnet test -c Release --no-build | PASS, 229 passed, 0 failed, 0 skipped |
| Single-file publish | PASS, no warnings observed |
| Extracted ZIP smoke, PowerShell 7 | PASS |
| Extracted ZIP smoke, Windows PowerShell 5.1 | PASS |
| PowerShell scripts parse | PASS |
| Publish-profile XML parses | PASS |
| Workflow YAML parses; triggers/runner/permissions checked | PASS (not a remote CI run) |
| git diff --check | PASS |

The existing 229 tests comprise 11 Core and 218 Windows/CLI cases. The package smoke script adds executable-level checks for version/help, text, JSON/ConvertFrom-Json, Unicode, file/ADS byte preservation, no metadata, non-recursive scan/counts, missing/directory errors and unsafe open refusal. It creates only temporary fixtures and never launches a browser.

The previously accepted M5 performance observation remains 284 ms for 1000 local files with 500 known/500 unknown; M7 does not alter scan code.

## Release artifact

- ZIP: artifacts/WhereFrom-0.1.0-win-x64.zip
- Checksum: artifacts/SHA256SUMS.txt (authoritative for the accompanying ZIP).
- Extracted root: WhereFrom-win-x64.
- Program: wherefrom.exe, single-file, self-contained win-x64; no .NET installation required.
- EXE size in this candidate: 73,568,083 bytes, approximately 70.16 MiB.
- Package includes English/Chinese README, MIT license, .NET license/notices, engineering/validation documents and the smoke script.
- Unsigned; no installer, PATH modifications, registry changes or auto-update.
- Runtime native files extract under the user's temporary directory. This is not a zero-write executable, but inspected evidence remains read-only.
- SDK/runtime patches may change a future rebuild's bytes and checksum. Package hashes are generated after ZIP creation rather than embedded in that same ZIP.

## Definition of Done

| Item | State / evidence |
| --- | --- |
| Windows 11 x64 | PASS locally |
| Single-file provenance query | PASS automated + user acceptance |
| Zone.Identifier / ZoneId / Host / Referrer | PASS automated + browser-sample acceptance |
| No MotW | PASS; absent evidence remains absent |
| JSON | PASS automated + user acceptance |
| Directory scan | PASS; non-recursive as requested |
| Open source page | PASS unit/fixture + M6 user browser acceptance; clean-package browser check pending |
| Unicode / invalid path / access failure | PASS automated; Unicode also user-verified |
| Release artifact | CREATED and locally smoke-tested |
| Clean Windows without development tools | PENDING user verification |
| CI build | CONFIGURED; remote run PENDING |
| CI tests | CONFIGURED; remote run PENDING |
| README | COMPLETE in English and Chinese |
| No telemetry/cloud/accounts | PASS source review; none implemented |
| No network dependency for local inspection | PASS source review/local execution; disconnected clean-machine check pending; open intentionally uses browser |
| Read-only MotW | PASS byte-preservation tests; no unblock/delete/write in inspection code |

## Remaining manual work

Follow release-validation.md with the exact ZIP and checksum:
1. Test on clean Windows 11 x64 with no .NET runtime/SDK installed, preferably also disconnected for local queries.
2. Run the included smoke script and manual text/JSON/scan/open commands; verify actual browser navigation and any runtime-extraction or SmartScreen block.
3. After the user commits/pushes the reviewed changes, confirm the Windows workflow's build, tests, package smoke and artifact upload complete successfully.
4. Existing file-symlink coverage is still pending due local creation privileges; junctions were validated in M5. Use existing links if available.

Do not mark v0.1 fully accepted until the clean-environment and remote CI results are recorded.

## Limitations and deferred scope

Windows only; native package targets x64. Non-NTFS/network-share combinations are not broadly verified. Reads are not atomic snapshots; metadata is limited to 64 KiB and supported decoding. Reparse checks are not a security boundary against concurrent replacement or ancestor links. Scan output may wrap long names.

Scan JSON and recursion remain deliberately excluded. Browser capture/extensions and persistent storage are future v0.2 work, not shipped. GUI, Explorer integration, file identity/move tracking, AI, cloud, telemetry, accounts and background services are not implemented.

## References

- [.NET single-file deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview): self-contained bundling and native extraction.
- [actions/checkout](https://github.com/actions/checkout), [setup-dotnet](https://github.com/actions/setup-dotnet), [upload-artifact](https://github.com/actions/upload-artifact): workflow usage.
