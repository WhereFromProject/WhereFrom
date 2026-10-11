ENGLISH | [简体中文](README.zh-CN.md)

# WhereFrom

Find the source information recorded on a Windows file.

Downloaded a ZIP, installer, or PDF and forgotten where it came from? WhereFrom reads the source metadata already attached to the file and shows the available download URL, referring page, and Windows zone.

WhereFrom is in early development. The published v0.1.1 package provides a command-line tool; the current source tree also has a single-file GUI. Local queries do not modify your files or remove Mark of the Web.

## Installation

For the full-feature source preview, build and run `WhereFrom-preview-win-x64-Setup.exe` as described below. The published v0.1.1 installer remains CLI-only. On first installation, choose a folder or keep the default `%LOCALAPPDATA%\Programs\WhereFrom`. The installer adds the selected folder to your user PATH and requires no administrator privileges or separate .NET installation. Choose a folder your Windows account can write to; paths containing semicolons are not supported.

Reinstalling uses the existing installation folder. To change its location, uninstall WhereFrom first, then run setup again and select the new folder.

Close all terminal windows and open a new terminal after installation. You can then run `wherefrom --version` or `wherefrom "C:\path\to\file.zip"` from any directory. If a terminal host still has the old environment, restart that host or sign out and back in.

Uninstall **WhereFrom** through Windows Settings → Apps → Installed apps. The uninstaller removes its own PATH entry while preserving other entries. Reinstalling does not duplicate the entry. An equivalent entry that existed before installation is left untouched.

### Full preview package from source

Run `scripts/publish.ps1` from the repository root to create `artifacts/WhereFrom-preview-win-x64.zip`. With [Inno Setup 6](https://jrsoftware.org/isinfo.php) installed, run `scripts/build-installer.ps1` to create `artifacts/WhereFrom-preview-win-x64-Setup.exe`. The preview includes the existing CLI, basic GUI, Native Host, and Chrome/Edge extension.

The preview installer registers the Native Host for the current user and adds a GUI Start menu shortcut. It also adds a file context-menu command, “查看文件来源 (WhereFrom)”, which opens the selected file in the GUI. On Windows 11, it may appear under **Show more options**. Re-run the preview installer to add it to an existing installation; uninstall removes the command. The portable ZIP does not change Explorer menus. Load the extension manually using `BROWSER-SETUP.txt` in the installation directory; the installer does not change browser settings. Uninstall removes this installer's Host registration and program files but preserves `%LOCALAPPDATA%\WhereFrom\wherefrom.db`. The portable ZIP requires the registration and removal steps in the same guide; re-register after moving its directory.

### Portable ZIP

The Windows x64 portable ZIP bundles .NET; **you do not need to install a .NET runtime**. Extract `WhereFrom-preview-win-x64.zip`, open PowerShell in its `WhereFrom-win-x64` folder, and run:

```powershell
.\wherefrom.exe --version
.\wherefrom.exe "C:\Users\YourName\Downloads\example.zip"
```

A package can be built from source using the steps below. This repository does not imply that a public GitHub Release has already been published. Packages are unsigned. The bundled runtime extracts native components into the user's temporary directory, which must be writable.

### Try the GUI alone

Run `scripts/publish-gui.ps1` from the repository root, extract `artifacts/WhereFrom-GUI-preview-win-x64.zip`, and open `WhereFrom.App.exe` normally. Select one file, drag one file from File Explorer, or pass one file path as a launch argument. The window shows Windows metadata and matching local browser download records. Copying a URL and opening a source page require an explicit click; only valid HTTP/HTTPS URLs can be opened. The full source-built preview installer also includes this GUI.

## Quick start

Replace the example path with your file. Keep the quotes if the path contains spaces.

A file with complete metadata might produce:

```text
example.zip

Source
  https://example.com/download/example.zip

Referrer
  https://example.com/download

Windows Zone
  Internet (3)
```

Source is the recorded download address; it may be an API or CDN endpoint. Referrer may be the human-facing page. WhereFrom keeps them separate and does not invent missing information.

If only the zone is available, it shows the zone followed by `No source URL recorded.`. If no usable provenance is available, it shows:

```text
example.zip

No provenance information found.
```

## Commands

Browser download records use a local SQLite database at `%LOCALAPPDATA%\WhereFrom\wherefrom.db`. Inspect its path with `wherefrom data path`; permanently clear saved download records with `wherefrom data clear --yes`. These commands do not change a file's Windows source metadata. The current source tree supports developer-mode capture of **new** Chrome and Edge downloads; see [extension setup](extension/README.md). The published v0.1.1 package does not include the extension or Native Host yet.

Single-file queries combine Windows metadata and matching browser records. Text output labels each evidence source; JSON keeps `schemaVersion: 1` and its existing fields while adding `completeness`, `conflict`, `browserStoreStatus`, `historicalBrowserRecords`, and `evidence`. Browser URLs have query strings and fragments removed before storage; Windows metadata URLs remain unchanged and may contain private parameters. New browser records also store SHA-256 for files up to 64 MiB. A same-path record is direct evidence only when path, size, modified time, and hash agree; identical content can still be a copy. Older records without hashes, and same-content records found after a move or copy, appear only as **possible associations**. Possible sources are shown in the evidence list, never promoted to the top-level `sourceUrl` or opened by `open <file>`. Files above the hash limit remain metadata-only possible associations. JSON remains `schemaVersion: 1` and adds `possibleBrowserRecords` and evidence `matchBasis`. Directory `scan` still reads Windows metadata only; `audit` uses the combined query.

From the extracted package directory:

```powershell
.\wherefrom.exe "C:\path\to\file.exe"
.\wherefrom.exe --help
.\wherefrom.exe --version
```

Provide one file at a time. For a filename beginning with `-` or named `debug-zone`, use its full path or prefix it with `.\`.

### Open a source page

```powershell
.\wherefrom.exe open "C:\path\to\file.zip"
```

Opens the recorded Referrer URL in your default browser, or the Source URL when no parsed Referrer is available. The selected address is printed after `Opening:`. Only valid HTTP/HTTPS URLs are allowed. If the selected address uses another scheme, the command refuses it without falling back to another URL.

With no URL, it prints `No source URL is available for this file.` and returns 1. Refused URLs return 2, browser launch failures return 4, and successful dispatch returns 0. File-reading errors retain the single-file exit codes below. Exit 0 means Windows accepted the launch; it does not confirm that the page loaded.

This command explicitly opens a browser and may contact the recorded website. Full query parameters and fragments are passed through. Regular queries and scans never open URLs. `open` does not support `--json`.

### Scan a directory

```powershell
.\wherefrom.exe scan "$HOME\Downloads"
```

Scans only the first level, including hidden/system files. Each row shows the filename and the first available Referrer host, Source host, or Windows zone. Valid URLs without a host are labeled `URL recorded (no host)`. Files without usable evidence show `Unknown`; failed reads show `Error`, with details on standard error. Full URLs remain available through single-file queries.

The summary reports `Scanned`, `Known provenance`, `Unknown`, `Errors`, and `Skipped reparse points`. Scanned equals known + unknown + errors. Ordinary subdirectories are ignored; reparse points (including links and junctions) are skipped. If the scan directory itself is a reparse point, provide its target directory directly.

A completed scan returns 0, including an empty directory or all-unknown results. Invalid directory input returns 2; a missing/inaccessible root returns 3; file read failures or an interrupted directory enumeration return 4; unexpected failures return 5. A failed file does not stop the remaining files. An incomplete enumeration is explicitly reported on standard error, with partial counts.

Scanning is sequential and read-only. `--recursive` and scan `--json` are not supported. Entry order follows the filesystem; the tab-separated display may wrap long filenames in narrow terminals.

### Audit a directory

```powershell
.\wherefrom.exe audit "$HOME\Downloads" --limit 100
```

Audits only the first level and never changes inspected files. It separates `Known`, `Possible`, `Unknown`, and `Error` results. The default limit is 100 files; set `--limit` from 1 to 1000. Reparse points are skipped. A possible match displays only the source host in the audit table; inspect that file directly to see the evidence and its uncertainty.
### JSON output

Append `--json` after the file path to return one JSON object:

```powershell
$result = .\wherefrom.exe "C:\path\to\file.exe" --json | ConvertFrom-Json
$code = $LASTEXITCODE
$result.sourceUrl
```

The version 1 contract always includes `schemaVersion` (1), `path` (the supplied path), `hasProvenance` (boolean), `zone` (`{ "id": 3, "name": "Internet" }` or `null`), `sourceUrl`, and `referrerUrl`. Missing URLs and unknown zone names are `null`; properties are never omitted. URLs retain their recorded spelling, query parameters, and fragments after JSON decoding.

Completed queries return JSON even when no provenance is available (exit code 1). Argument and read errors return no JSON; use the exit code and standard error to distinguish them. Metadata warnings go only to standard error. Keep standard error separate from the JSON pipeline; do not merge it with `2>&1`.

### Raw metadata

The diagnostic command shows the original metadata text:

```powershell
.\wherefrom.exe debug-zone "C:\path\to\file.exe"
```

It preserves field order and URLs, while displaying dangerous control characters as visible escapes such as `\u0000`. An empty stream is reported separately from a missing stream.

### Using scripts

Reports go to standard output. Errors and metadata warnings go to standard error. In PowerShell, inspect `$LASTEXITCODE`:

| Code | Meaning                                                                                                            |
| ---- | ------------------------------------------------------------------------------------------------------------------ |
| 0    | Usable provenance was found, or help/version was displayed                                                         |
| 1    | The query completed but no usable provenance was found                                                             |
| 2    | Invalid arguments, an invalid path, or a directory instead of a file                                               |
| 3    | File not found or access denied; see the error message                                                             |
| 4    | Read failure, unavailable ADS support, a failed capability query, invalid encoding, or the size limit was exceeded |
| 5    | An unexpected internal error                                                                                       |

For `debug-zone`, code 0 means the stream was read, including an empty stream; code 1 means the stream was not found.

## Privacy and safety

- **Read-only:** WhereFrom does not modify inspected files or delete, change, or unblock Mark of the Web.
- **Local:** Checking local files does not require internet access. There are no accounts, telemetry, or cloud sync. Building for the first time requires downloading development dependencies.
- **URLs may be private:** Both reports and raw output preserve query parameters and fragments, which can contain tokens. Review and redact output before sharing it.
- **Provenance is not a safety verdict:** Missing information does not make a file dangerous, and a recorded URL does not prove a file is safe. Only the explicit `open` command or the GUI's open button dispatches validated HTTP/HTTPS URLs to your default browser.

## Limitations

- Windows only. Local Windows 11 x64 / NTFS has been tested. Other file systems and network shares may behave differently.
- WhereFrom cannot recover missing metadata or infer a file's history from its absence.
- Damaged fields are reported while other usable fields remain visible. Unknown zone numbers are shown without a guessed name.
- If no stream is found, the tool checks file-system support. A failed capability query is reported as an error rather than a claim that the file has no provenance. Some network shares do not support this query.
- Metadata reads are limited to 64 KiB. Oversized data produces an error, not silent truncation.
- Text is decoded as UTF-8 by default, with BOM detection. Not all legacy encodings or damaged text are supported.
- Reads are not atomic snapshots; another process can change the file during inspection.
- The current source tree and full preview package have a basic GUI; the published v0.1.1 installer remains CLI-only. Explorer integration and file-move tracking are not available yet.

## Build from source

On Windows, install the [.NET 10 SDK](https://learn.microsoft.com/dotnet/core/install/windows), clone this repository, and run from its root:

```powershell
dotnet build
dotnet test
dotnet run --project src/WhereFrom.Cli -- "C:\path\to\file.zip"
.\scripts\publish.ps1
```

To also build the installer, install [Inno Setup 6](https://jrsoftware.org/isdl.php) and run `./scripts/build-installer.ps1` after publishing. A custom compiler path can be supplied with `-Compiler`.

Publishing creates the portable ZIP and SHA256 checksum under `artifacts/`. See [release validation](docs/release-validation.md) for package checks on a machine without .NET installed. The Windows workflow performs restore, build, test, publish and package smoke checks.

## Roadmap

The source-built preview includes Chrome/Edge capture, unified queries, the GUI, an Explorer file menu, and tentative content-based associations for moved or copied files. The published v0.1.1 release remains CLI-only. Chrome and clean-machine validation are still needed before a stable full-feature release.

## Feedback and contributions

Report problems through [GitHub Issues](https://github.com/opsorart/WhereFrom/issues). Include your Windows version, how the file was obtained, the command you ran, and the output. Do not upload private files or unredacted URLs.

See [ENGINEERING.md](ENGINEERING.md) for the engineering design and [release validation](docs/release-validation.md) for the release checklist.

## License

[MIT](LICENSE).
