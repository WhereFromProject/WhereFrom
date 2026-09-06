[English](README.md) | [Simplified Chinese](README.zh-CN.md)

# WhereFrom

Find the source information recorded on a Windows file.

Downloaded a ZIP, installer, or PDF and forgotten where it came from? WhereFrom reads the source metadata already attached to the file and shows the available download URL, referring page, and Windows zone.

WhereFrom is an early-stage command-line tool. It works locally and does not modify your files or remove Mark of the Web.

## Quick start

To build from source, you need **Windows** and the [.NET 10 SDK](https://learn.microsoft.com/dotnet/core/install/windows). The runtime alone is not enough to build the project.

Download or clone this repository, open PowerShell in its root directory, and run:

```powershell
dotnet build
dotnet run --project src/WhereFrom.Cli -- "C:\Users\YourName\Downloads\example.zip"
```

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

After building, you can run the program directly from the repository root:

```powershell
.\src\WhereFrom.Cli\bin\Debug\net10.0-windows\wherefrom.exe "C:\path\to\file.exe"
.\src\WhereFrom.Cli\bin\Debug\net10.0-windows\wherefrom.exe --help
.\src\WhereFrom.Cli\bin\Debug\net10.0-windows\wherefrom.exe --version
```

Provide one file at a time. For a filename beginning with `-` or named `debug-zone`, use its full path or prefix it with `.\`.

### Raw metadata

The diagnostic command shows the original metadata text:

```powershell
.\src\WhereFrom.Cli\bin\Debug\net10.0-windows\wherefrom.exe debug-zone "C:\path\to\file.exe"
```

It preserves field order and URLs, while displaying dangerous control characters as visible escapes such as `\u0000`. An empty stream is reported separately from a missing stream.

### Using scripts

Reports go to standard output. Errors and metadata warnings go to standard error. In PowerShell, inspect `$LASTEXITCODE`:

| Code | Meaning |
| --- | --- |
| 0 | Usable provenance was found, or help/version was displayed |
| 1 | The query completed but no usable provenance was found |
| 2 | Invalid arguments, an invalid path, or a directory instead of a file |
| 3 | File not found or access denied; see the error message |
| 4 | Read failure, unavailable ADS support, a failed capability query, invalid encoding, or the size limit was exceeded |
| 5 | An unexpected internal error |

For `debug-zone`, code 0 means the stream was read, including an empty stream; code 1 means the stream was not found.

## Privacy and safety

- **Read-only:** WhereFrom does not modify inspected files or delete, change, or unblock Mark of the Web.
- **Local:** Checking local files does not require internet access. There are no accounts, telemetry, or cloud sync. Building for the first time requires downloading development dependencies.
- **URLs may be private:** Both reports and raw output preserve query parameters and fragments, which can contain tokens. Review and redact output before sharing it.
- **Provenance is not a safety verdict:** Missing information does not make a file dangerous, and a recorded URL does not prove a file is safe. WhereFrom does not open or execute recorded URLs.

## Limitations

- Windows only. Local Windows 11 x64 / NTFS has been tested. Other file systems and network shares may behave differently.
- WhereFrom cannot recover missing metadata or infer a file's history from its absence.
- Damaged fields are reported while other usable fields remain visible. Unknown zone numbers are shown without a guessed name.
- If no stream is found, the tool checks file-system support. A failed capability query is reported as an error rather than a claim that the file has no provenance. Some network shares do not support this query.
- Metadata reads are limited to 64 KiB. Oversized data produces an error, not silent truncation.
- Text is decoded as UTF-8 by default, with BOM detection. Not all legacy encodings or damaged text are supported.
- Reads are not atomic snapshots; another process can change the file during inspection.
- Directory scanning, JSON output, opening source pages, a GUI, Explorer integration, and file-move tracking are not available.

## Feedback and contributions

Report problems through [GitHub Issues](https://github.com/strategist0/WhereFrom/issues). Include your Windows version, how the file was obtained, the command you ran, and the output. Do not upload private files or unredacted URLs.

See [ENGINEERING.md](ENGINEERING.md) for the engineering design and [docs/](docs/) for validation records.

## License

[MIT](LICENSE).
