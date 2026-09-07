ENGLISH | [简体中文](README.zh-CN.md)

# WhereFrom

Find the source information recorded on a Windows file.

Downloaded a ZIP, installer, or PDF and forgotten where it came from? WhereFrom reads the source metadata already attached to the file and shows the available download URL, referring page, and Windows zone.

WhereFrom is an early-stage command-line tool. It works locally and does not modify your files or remove Mark of the Web.

## Installation

The Windows x64 portable ZIP bundles .NET; **you do not need to install a .NET runtime**. Extract `WhereFrom-0.1.0-win-x64.zip`, open PowerShell in its `WhereFrom-win-x64` folder, and run:

```powershell
.\wherefrom.exe --version
.\wherefrom.exe "C:\Users\YourName\Downloads\example.zip"
```

A package can be built from source using the steps below. This repository does not imply that a public GitHub Release has already been published. Packages are unsigned. The bundled runtime extracts native components into the user's temporary directory, which must be writable.

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
- **Provenance is not a safety verdict:** Missing information does not make a file dangerous, and a recorded URL does not prove a file is safe. Only the explicit open command dispatches validated HTTP/HTTPS URLs to your default browser.

## Limitations

- Windows only. Local Windows 11 x64 / NTFS has been tested. Other file systems and network shares may behave differently.
- WhereFrom cannot recover missing metadata or infer a file's history from its absence.
- Damaged fields are reported while other usable fields remain visible. Unknown zone numbers are shown without a guessed name.
- If no stream is found, the tool checks file-system support. A failed capability query is reported as an error rather than a claim that the file has no provenance. Some network shares do not support this query.
- Metadata reads are limited to 64 KiB. Oversized data produces an error, not silent truncation.
- Text is decoded as UTF-8 by default, with BOM detection. Not all legacy encodings or damaged text are supported.
- Reads are not atomic snapshots; another process can change the file during inspection.
- A GUI, Explorer integration, and file-move tracking are not available.

## Build from source

On Windows, install the [.NET 10 SDK](https://learn.microsoft.com/dotnet/core/install/windows), clone this repository, and run from its root:

```powershell
dotnet build
dotnet test
dotnet run --project src/WhereFrom.Cli -- "C:\path\to\file.zip"
.\scripts\publish.ps1
```

Publishing creates the portable ZIP and SHA256 checksum under `artifacts/`. See [release validation](docs/release-validation.md) for package checks on a machine without .NET installed. The Windows workflow performs restore, build, test, publish and package smoke checks.

## Roadmap

The current v0.1 CLI supports single-file queries, JSON, non-recursive directory scanning and opening source pages. Browser capture/storage is planned for v0.2; GUI, Explorer integration and file tracking are later ideas. These future capabilities are not included, and dates are not committed.

## Feedback and contributions

Report problems through [GitHub Issues](https://github.com/strategist0/WhereFrom/issues). Include your Windows version, how the file was obtained, the command you ran, and the output. Do not upload private files or unredacted URLs.

See [ENGINEERING.md](ENGINEERING.md) for the engineering design and [docs/](docs/) for validation records.

## License

[MIT](LICENSE).
