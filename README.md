# WhereFrom

**Find the source information Windows and your browser recorded for a file.**

[简体中文](README.zh-CN.md) · [Engineering notes](ENGINEERING.md) · [Documentation](docs/README.md)

WhereFrom shows the available download URL, referring page, and Windows zone. It reads the file's `Zone.Identifier` stream and, in the current source-built preview, can also consult a local record of new Chrome/Edge downloads. It does not reconstruct missing history or decide whether a file is safe. Queries do not alter the inspected file or remove Mark of the Web.

The published **v0.1.1** package is CLI-only. The **source-built preview** adds a GUI, browser capture, and an Explorer file menu; it is not a new public release.

```powershell
wherefrom "C:\Users\YourName\Downloads\example.zip"
```

```text
example.zip

Source
  https://example.com/download/example.zip

Referrer
  https://example.com/download

Windows Zone
  Internet (3)
```

## Current capabilities

| Capability | Published v0.1.1 | Source-built preview |
| --- | --- | --- |
| Inspect `Zone.Identifier` | CLI text and schema v1 JSON | CLI and GUI |
| Scan a directory | First-level `scan` | `scan` plus bounded `audit` |
| Open a recorded page | Explicit `open` command | CLI or explicit GUI click |
| Browser evidence | — | New completed Chrome/Edge downloads via extension and Native Host |
| Local records | — | SQLite under `%LOCALAPPDATA%\WhereFrom` |
| File copies and moves | — | Same-content historical records shown only as possible associations |
| Installation | Per-user installer or self-contained ZIP | Preview installer or ZIP; GUI and removable Explorer menu |

Chrome has not been manually tested on the current development machine. The [release checklist](docs/release-validation.md) lists the remaining clean-machine checks.

## Install the preview

On Windows x64, build the current preview from the repository root:

```powershell
./scripts/publish.ps1
./scripts/build-installer.ps1
```

The second command requires [Inno Setup 6](https://jrsoftware.org/isinfo.php). Outputs are `artifacts/WhereFrom-preview-win-x64.zip`, `artifacts/WhereFrom-preview-win-x64-Setup.exe`, and `artifacts/SHA256SUMS.txt`. These packages are unsigned. Their names do not imply a GitHub Release has been published.

The installer runs for the current user without elevation, adds the CLI to the user PATH, registers the Native Host, and installs the GUI and the single-file Explorer menu. Open a new terminal after installation. Reinstalling keeps the existing installation directory; to change it, uninstall first. Uninstall removes installer-owned PATH, Host, and menu registrations while leaving `%LOCALAPPDATA%\WhereFrom\wherefrom.db` in place. The ZIP changes no system registrations by itself and includes registration/removal scripts. Both formats bundle .NET and need a writable user temporary directory.

Load the extension manually in Chrome or Edge Developer mode using [the browser setup guide](docs/browser-setup.txt), also included in the package as `BROWSER-SETUP.txt`. It records only new completed downloads, not past history; failed, canceled, and incognito downloads are not saved. Browser extension removal is manual.

The published v0.1.1 installer and ZIP remain valid for users who only need the original CLI.

## Use WhereFrom

```powershell
wherefrom "C:\path\to\file.zip"
wherefrom "C:\path\to\file.zip" --json
wherefrom scan "$HOME\Downloads"
wherefrom audit "$HOME\Downloads" --limit 100
```

The GUI can select or accept a single dropped file, or start with one file path as an argument. It shows Windows and browser evidence separately. Copying a URL or opening a webpage requires a click. The Explorer entry opens the selected file in this GUI.

| Command | Result |
| --- | --- |
| `wherefrom <file>` | Show available Windows and direct browser evidence |
| `wherefrom <file> --json` | Emit one schema v1 JSON object |
| `wherefrom scan <directory>` | Read first-level Windows metadata only |
| `wherefrom audit <directory> [--limit 1..1000]` | Classify first-level files as Known, Possible, Unknown, or Error |
| `wherefrom open <file>` | Explicitly open a validated Referrer or Source URL |
| `wherefrom debug-zone <file>` | Display raw `Zone.Identifier` metadata |
| `wherefrom data path` / `data clear --yes` | Show the local database path / explicitly clear browser records |
| `wherefrom --help` / `--version` | Show help / version |

Quote paths containing spaces. For a filename beginning with `-` or named `debug-zone`, use a full path or a `.\` prefix. `scan` and `audit` do not recurse and skip reparse points. `audit` checks at most 100 files by default, or 1–1000 with `--limit`; the table shows a source host rather than the full URL.

### Evidence and JSON

`Source` is the recorded download address, sometimes a CDN or API endpoint. `Referrer` may be the page you visited. Windows metadata URLs are shown as recorded and may contain private query parameters. Browser URLs are stored without query strings or fragments, and no request headers or cookies are saved.

For new browser records on files up to 64 MiB, WhereFrom stores SHA-256. A record at the same path is direct browser evidence only when path, size, modification time, and hash agree. Older records without a hash, larger files, and matching content found at a different path are labeled **possible associations**. Equal content does not prove that two paths identify the same file. Possible URLs stay in the evidence list; they are not promoted to the top-level source URL or opened by `open` or the GUI button. Conflicting Windows and browser evidence is displayed side by side.

JSON keeps `schemaVersion: 1` and the existing `path`, `hasProvenance`, `zone`, `sourceUrl`, and `referrerUrl` fields. Additive fields describe completeness, conflict, historical and possible browser records, and structured evidence. Missing URLs and unknown zone names are `null`. A completed no-evidence query still returns JSON with exit code 1; argument and read errors return no JSON. Keep standard error separate from a JSON pipeline.

`open` uses the parsed Referrer first, then Source when no parsed Referrer exists. Only valid HTTP/HTTPS URLs are accepted. A rejected Referrer is not silently replaced by Source. Exit 0 means Windows accepted the launch request, not that the page loaded. Full Windows metadata URLs, including query parameters, are passed through only after the explicit action.

| Exit code | Meaning for a file query |
| --- | --- |
| 0 | Usable evidence found |
| 1 | Query completed without usable evidence |
| 2 | Invalid argument or path; directory given as file |
| 3 | File not found or access denied |
| 4 | Read or storage failure |
| 5 | Unexpected internal error |

Other commands document their specific edge cases in `wherefrom --help` and the [release checklist](docs/release-validation.md). `debug-zone` returns 0 for a readable stream, even if empty, and 1 when the stream is absent.

## Privacy and limitations

- Inspection is local and does not change file contents or Mark of the Web. Only explicit page opening makes a browser request.
- There are no accounts, telemetry, cloud sync, background service, or old-download import. Building from source may download development dependencies.
- A URL is provenance evidence, not a safety rating. Unknown provenance does not mean a file is dangerous.
- Share command output only after redacting private filenames, URLs, query parameters, and tokens.
- Windows 11 x64 on NTFS has been tested. Other file systems and network shares may behave differently. `Zone.Identifier` reads are capped at 64 KiB; damaged fields are reported without inventing missing data.
- Query results are not atomic snapshots if another process changes a file during inspection.

## Build and contribute

Install the [.NET 10 SDK](https://learn.microsoft.com/dotnet/core/install/windows) on Windows and run:

```powershell
dotnet restore WhereFrom.sln
dotnet test WhereFrom.sln -c Release -p:Platform=x64
node --test extension/test/worker.test.js
./scripts/publish.ps1
./scripts/check-repository-hygiene.ps1 -Package artifacts/WhereFrom-preview-win-x64.zip
```

The [Windows workflow](.github/workflows/windows.yml) also builds and tests the installer. See [ENGINEERING.md](ENGINEERING.md) for architecture and maintenance rules, and [docs/README.md](docs/README.md) for user and release documentation. Open an [issue](https://github.com/opsorart/WhereFrom/issues) with a reproducible example and redacted output.

Licensed under [MIT](LICENSE).