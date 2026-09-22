# Per-user installer validation

Date: 2026-09-20

## Scope

Added an Inno Setup 6 installer alongside the portable ZIP. Existing CLI behavior, provenance models, file readers and MotW handling are unchanged. No commit or public release was created.

The normal setup defaults to %LOCALAPPDATA%\Programs\WhereFrom without elevation, registers an uninstall entry, adds the selected directory to HKCU\Environment\Path, and broadcasts the environment change. First-time installation offers a destination selection page. Reinstalling uses the previously installed directory; changing location requires uninstalling first. Existing terminal processes still need restarting. Machine PATH is never written.

PATH comparisons handle case, quotes, trailing separators, and expanded environment-variable references. Existing PATH text (including empty entries) and REG_SZ/REG_EXPAND_SZ type are preserved. Reinstallation does not duplicate entries. Ownership is stored separately: an equivalent entry present before installation is not removed on uninstall. Uninstall removes only the installer-owned entry, retains later unrelated PATH additions, and leaves unrelated files in the install directory alone. It does not restore an old full PATH snapshot.

## Files

- installer/WhereFrom.iss: setup, uninstall and PATH lifecycle.
- scripts/build-installer.ps1: packages the existing portable ZIP with Inno Setup; writes checksums for ZIP and setup EXE.
- scripts/test-installer.ps1: compiles a test variant with an isolated AppId/registry namespace and performs install/reinstall/uninstall checks.
- .github/workflows/windows.yml: downloads a pinned, signature-checked official Inno compiler, builds/tests installer, uploads both formats.
- README.md / README.zh-CN.md: user-facing installation and removal instructions.

Inno Setup is a build-time dependency only. The compiler installer was downloaded from the official release and its Authenticode signature validated as Pyrsys B.V. The generated WhereFrom installer remains unsigned.

## Validation

- dotnet build: PASS, 0 warnings / 0 errors.
- dotnet test: PASS, 229 passed / 0 failed / 0 skipped.
- Inno Setup 6.7.3 compilation: PASS.
- Real silent install/reinstall/uninstall using the isolated test variant:
  - Existing REG_SZ and REG_EXPAND_SZ PATH values retained text and type.
  - Reinstall did not add a duplicate.
  - Installed executable returned WhereFrom 0.1.0.
  - Later unrelated PATH additions survived uninstall.
  - An unrelated file in the install directory survived uninstall.
  - Pre-existing quoted, differently cased, trailing-slash entry was not duplicated or removed.
  - Previously missing PATH was handled.
  - Real user PATH was checked before/after and remained unchanged.
- Additional child-process check runs bare wherefrom --version from outside the install directory using the test PATH, without changing the actual process/user PATH.
- Test compilation changes only registry locations, AppId and output filename; production and test variants share the same PATH code.

Tests require permission to create their isolated HKCU\Software\WhereFrom\InstallerTest key and test uninstall entry. They never use the real user PATH as a test fixture. The normal production installer has not been installed on the development account. User-reported production installer checks in Windows Sandbox are recorded below; the new remote CI run remains unverified.

## User-reported manual results (2026-09-22)

Environment: Windows Sandbox, account `WDAGUtilityAccount`.

- Running `wherefrom --version` from `C:\Windows\system32` returned `WhereFrom 0.1.0`, confirming command discovery outside the installation directory.
- From the user's home directory, `Get-Command wherefrom` resolved to `C:\Users\WDAGUtilityAccount\AppData\Local\Programs\WhereFrom\wherefrom.exe`.
- Filtering the persisted user PATH for WhereFrom returned exactly one installation-directory entry. The transcript does not independently establish whether setup was run twice.
- At the uninstall verification step, `Get-Command wherefrom -ErrorAction SilentlyContinue` produced no output, as expected when the installed command is no longer discoverable.
- Provenance lookup against a real downloaded file was not performed because the sample archive was unavailable in Windows Sandbox. Opening its source URL was also not demonstrated. These checks remain unverified for this installed package; earlier milestone validation is separate evidence.

The supplied output confirms basic installation/PATH discovery and post-uninstall command disappearance. It does not explicitly confirm absence of UAC prompts, terminal restart steps, preservation of unrelated PATH entries after uninstall, or the portable ZIP check.

## Build and artifacts

### Custom installation directory (2026-09-22)

- Set `DisableDirPage=auto` and `UsePreviousAppDir=yes`: first-time setup offers a directory choice; existing installations retain their location. No file-migration feature was added.
- Updated both READMEs with folder selection, writable-folder requirements and uninstall-before-relocation guidance.
- Build passed with zero warnings/errors; all 229 .NET tests passed.
- Installer tests passed using a custom directory containing Chinese characters and spaces. Reinstallation now omits `/DIR` and verifies both the remembered InstallLocation and the absence of duplicate PATH entries. Uninstall, existing-entry ownership, PATH value type and unrelated-file preservation checks also passed; the real user PATH remained unchanged.
- Registry integration tests required execution outside the Codex sandbox because of its previously confirmed registry restriction. This is separate from the Windows Sandbox manual validation above.
- Manual follow-up: in a fresh Windows Sandbox (or after uninstalling), confirm the destination page appears, choose a writable custom folder, then verify `Get-Command wherefrom` in a reopened terminal. Repeat setup to confirm the folder is retained, and uninstall to check PATH cleanup. The interactive page and an additional drive still require manual validation.

Run scripts/publish.ps1, then scripts/build-installer.ps1 -Compiler <path-to-ISCC.exe>.
Run scripts/test-installer.ps1 -Compiler <path-to-ISCC.exe> to test the isolated variant.

Artifacts:
- artifacts/WhereFrom-0.1.0-win-x64-Setup.exe
- artifacts/WhereFrom-0.1.0-win-x64.zip
- artifacts/SHA256SUMS.txt

The ZIP remains portable and does not alter PATH. Downloading either artifact alone never changes settings: the user must run the installer once.

## Manual acceptance

On a standard Windows x64 account:
1. Run the normal Setup EXE. Confirm no administrator/UAC request, expected user install directory and successful finish.
2. Close all terminal windows and reopen one. From another directory, run Get-Command wherefrom and wherefrom --version; confirm resolution to the installed directory.
3. Run setup again; the user PATH must still contain only one equivalent installed entry.
4. Uninstall using Settings > Apps > Installed apps. Reopen the terminal; the installer-owned entry should be gone while unrelated PATH entries remain.
5. If PATH already included that directory before installing, uninstall should leave that pre-existing entry intact.
6. Verify the portable ZIP still works directly without installation.

Limitations: terminal hosts can retain old environments until restarted or signing out; another wherefrom earlier on PATH can shadow this one. Installation to a semicolon-containing path is rejected. Concurrent external edits to PATH are not transactionally coordinated. Unsigned setup may trigger Windows security prompts. This does not unblock inspected files or modify their ADS.
