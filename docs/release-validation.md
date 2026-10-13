# Windows x64 release validation

This is a repeatable release checklist for v0.2.0. It is not a record of a completed release. Keep test transcripts, personal paths, download URLs, and browser profiles outside Git.

## Build and inspect

Run on Windows x64 with .NET 10 SDK, Node.js, and Inno Setup 6 available:

```powershell
dotnet restore WhereFrom.sln
dotnet test WhereFrom.sln -c Release -p:Platform=x64
node --test extension/test/*.test.js
./scripts/publish.ps1
./scripts/build-installer.ps1
```

The outputs are `artifacts/WhereFrom-0.2.0-win-x64.zip`, `artifacts/WhereFrom-0.2.0-win-x64-Setup.exe`, and `artifacts/SHA256SUMS.txt`. Compare both SHA-256 values with `Get-FileHash`. Inspect the ZIP before release: it should contain the CLI, GUI, Native Host, browser extension, license and notices, two READMEs, browser setup guide, and portable Host registration scripts. It must not contain build directories, test results, local SQLite databases, internal milestone journals, credentials, or personal files.

Run the packaged CLI smoke test and the isolated installer tests:

```powershell
Expand-Archive -LiteralPath artifacts/WhereFrom-0.2.0-win-x64.zip -DestinationPath artifacts/verify
./scripts/smoke-test.ps1 -Executable ./artifacts/verify/WhereFrom-win-x64/wherefrom.exe
./scripts/test-installer.ps1 -Compiler "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
./scripts/test-upgrade-installer.ps1 -Compiler "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
```

The installer tests use their own registry namespace. They do not replace the normal installation or delete the user's provenance database.

## Clean-machine acceptance

Use a clean Windows x64 account or Windows Sandbox without a .NET SDK. Verify the portable ZIP and installer separately. For the portable ZIP, follow `BROWSER-SETUP.txt` to register the Native Host. For the installer, confirm the CLI is available from a new terminal, the GUI launches, and the Explorer menu opens the selected file. Confirm the extension can connect to the Host in Edge and Chrome after loading it in Developer mode.

For each browser, download a new small file, query it through CLI and GUI, and check that Windows and browser evidence remain separate. Copy the file to another path and check that the browser record is labeled a possible association. Check an unknown local file, a Zone-only file, a failed or canceled download, and an incognito download. Restart the browser and repeat one completed download. `open` must navigate only after the user explicitly requests it.

Uninstall the app. Confirm its PATH and Native Host registrations and Explorer menu are removed while `%LOCALAPPDATA%\WhereFrom\wherefrom.db` remains. Browser extensions loaded in Developer mode must be removed manually from each browser. Validate an in-place upgrade from the published v0.1.1 CLI installer separately.

Record pass/fail results outside the source tree and redact private URLs, tokens, filenames, and Windows account paths before sharing them. Do not mark a platform or browser as validated without a real run. The v0.2.0 package is unsigned; record any SmartScreen or application-control block rather than changing system policy during verification.
