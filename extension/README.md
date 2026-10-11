# Developer-mode browser capture

This Manifest V3 extension records **new completed downloads only**. It does not import browser history or inspect page content. Incognito downloads are ignored. The extension sends the download path and available URLs to the local WhereFrom Native Host, which strips URL query strings and fragments before saving them. The popup reports the last capture result without storing URLs.

The preview installer includes and registers the Native Host. After installation, load its `extension` folder in each browser as described in `BROWSER-SETUP.txt`. The developer-mode extension ID is fixed at `ggdgeholjgmlijgfpnmdggbofkofjhlp`; verify the browser shows this ID. The portable package includes registration scripts and the same guide.

For source-tree development:

1. Publish the host: `dotnet publish src/WhereFrom.NativeHost/WhereFrom.NativeHost.csproj -c Release -r win-x64 --self-contained false -o artifacts/native-host`.
2. In Chrome open `chrome://extensions` or in Edge open `edge://extensions`. Enable Developer mode, choose **Load unpacked**, and select this `extension` folder.
3. Copy the extension ID from the browser's extension page. Register the host with `./scripts/register-native-host.ps1 -HostExe ./artifacts/native-host/WhereFrom.NativeHost.exe -ChromeExtensionId <id>` or `-EdgeExtensionId <id>`. Supply both IDs if using both browsers.
4. Download a new HTTP(S) file. Click the extension icon to inspect the last capture result. `wherefrom data path` shows the database location; `wherefrom data clear --yes` explicitly removes its records.
5. Unregister with `./scripts/unregister-native-host.ps1` when developer testing is over.

If the popup says **Native Host unavailable**, verify the published `.exe`, browser-specific registration, and extension ID, then restart the browser. A red `!` badge signals an error. The current CLI and GUI combine matching browser records with Windows metadata.
