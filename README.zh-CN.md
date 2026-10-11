# WhereFrom

**查看 Windows 和浏览器为文件留下的来源线索。**

[English](README.md) · [工程设计](ENGINEERING.md) · [项目文档](docs/README.md)

WhereFrom 显示可用的下载地址、引用页面和 Windows 区域信息。它读取文件的 `Zone.Identifier` 数据流；当前源码构建的预览版还能查询本机记录的 Chrome／Edge 新下载。它不会补造缺失的历史，也不判断文件是否安全。查询不修改被检查文件或解除 Mark of the Web。

已公开的 **v0.1.1** 包仅包含 CLI。**源码预览版**增加了 GUI、浏览器捕获和资源管理器文件右键入口；这不表示新的公开版本已经发布。

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

## 当前能力

| 功能 | 已发布的 v0.1.1 | 源码预览版 |
| --- | --- | --- |
| 查询 `Zone.Identifier` | CLI 文本与版本 1 JSON | CLI 和 GUI |
| 扫描目录 | 第一层 `scan` | `scan` 与有数量上限的 `audit` |
| 打开来源页面 | 显式调用 `open` | CLI 命令或主动点击 GUI 按钮 |
| 浏览器证据 | — | 扩展与 Native Host 捕获 Chrome／Edge 此后的新下载 |
| 本地记录 | — | `%LOCALAPPDATA%\WhereFrom` 下的 SQLite |
| 文件移动与复制 | — | 内容相同的历史记录只标为“可能关联” |
| 安装 | 当前用户安装器或自带运行时的 ZIP | 预览安装器或 ZIP；包含 GUI 和可卸载的右键入口 |

当前开发机器没有完成 Chrome 实机验证。其余干净系统验收项见[发布检查清单](docs/release-validation.md)。

## 安装预览版

在 Windows x64 的仓库根目录构建：

```powershell
./scripts/publish.ps1
./scripts/build-installer.ps1
```

第二条命令需要 [Inno Setup 6](https://jrsoftware.org/isinfo.php)。生成物为 `artifacts/WhereFrom-preview-win-x64.zip`、`artifacts/WhereFrom-preview-win-x64-Setup.exe` 和 `artifacts/SHA256SUMS.txt`。这些安装包尚未签名；文件名不代表已发布 GitHub Release。

安装器无需提权，为当前用户安装 CLI、GUI 和单文件资源管理器右键入口，将 CLI 目录加入用户 PATH，并注册 Native Host。安装后请重新打开终端。重复安装沿用原目录；更换目录需先卸载。卸载时清理安装器拥有的 PATH、Host 和右键注册项，但保留 `%LOCALAPPDATA%\WhereFrom\wherefrom.db`。便携 ZIP 本身不修改系统注册项，包内提供 Host 注册与卸载脚本。两种形式都自带 .NET，并需要可写的用户临时目录。

按照[浏览器安装说明](docs/browser-setup.txt)在 Chrome 或 Edge 的开发者模式下手动加载扩展；发布包内同样提供 `BROWSER-SETUP.txt`。扩展只记录此后完成的新下载，不导入旧历史；失败、取消和无痕下载不入库。扩展本身需在浏览器中手动移除。

只需要原有 CLI 的用户仍可使用已发布的 v0.1.1 安装器和 ZIP。

## 使用 WhereFrom

```powershell
wherefrom "C:\path\to\file.zip"
wherefrom "C:\path\to\file.zip" --json
wherefrom scan "$HOME\Downloads"
wherefrom audit "$HOME\Downloads" --limit 100
```

GUI 可选择或拖入单个文件，也可通过一个文件路径参数启动。它分别展示 Windows 和浏览器证据；复制网址或打开网页都需要主动点击。资源管理器右键入口会用选中的文件打开 GUI。

| 命令 | 用途 |
| --- | --- |
| `wherefrom <file>` | 显示可用的 Windows 与浏览器证据 |
| `wherefrom <file> --json` | 输出一个版本 1 JSON 对象 |
| `wherefrom scan <directory>` | 仅查询第一层的 Windows 元数据 |
| `wherefrom audit <directory> [--limit 1..1000]` | 将第一层文件分为已知、可能关联、未知或错误 |
| `wherefrom open <file>` | 主动打开通过验证的引用或来源网址 |
| `wherefrom debug-zone <file>` | 显示原始 `Zone.Identifier` 内容 |
| `wherefrom data path` / `data clear --yes` | 查看本地数据库位置／主动清除浏览器记录 |
| `wherefrom --help` / `--version` | 显示帮助／版本 |

路径含空格时请加引号。文件名以 `-` 开头或名为 `debug-zone` 时，使用完整路径或 `.\` 前缀。`scan` 和 `audit` 都不递归，并跳过重解析点。`audit` 默认最多检查 100 个文件，可用 `--limit` 设置 1–1000；表格只显示来源域名，不显示完整网址。

### 证据与 JSON

`Source` 是记录的下载地址，可能指向 CDN 或 API；`Referrer` 可能是浏览时打开的页面。Windows 元数据中的网址保持原样，可能带有敏感查询参数。浏览器网址入库前会去掉查询参数和片段；不会保存请求头或 Cookie。

对不超过 64 MiB 的新浏览器下载，WhereFrom 保存 SHA-256。只有路径、大小、修改时间和哈希均一致，同路径记录才作为直接浏览器证据。没有哈希的旧记录、较大文件，以及在其他路径找到的同内容记录均标为**可能关联**。相同内容不能证明两个路径指向同一个文件实例。可能网址只出现在证据列表，不会作为顶层来源网址，也不会被 `open` 或 GUI 按钮自动打开。Windows 和浏览器证据冲突时并列展示。

JSON 保持 `schemaVersion: 1` 及既有的 `path`、`hasProvenance`、`zone`、`sourceUrl` 和 `referrerUrl` 字段；新增字段描述来源完整度、冲突、历史与可能关联记录，以及结构化证据。缺失的网址和未知区域名称为 `null`。查询正常完成但没有证据时仍返回 JSON，退出码为 1；参数或读取错误不返回 JSON。不要把标准错误并入 JSON 管道。

`open` 优先使用解析出的 Referrer，没有可用 Referrer 时使用 Source。只接受合法的 HTTP/HTTPS URL；被拒绝的 Referrer 不会悄悄换成 Source。退出码 0 只表示 Windows 接受了打开请求，不保证网页加载成功。只有用户主动操作时，Windows 元数据中的完整网址才会传给浏览器。

| 退出码 | 单文件查询含义 |
| --- | --- |
| 0 | 找到可用证据 |
| 1 | 查询完成，但没有可用证据 |
| 2 | 参数或路径无效；输入的是目录 |
| 3 | 文件不存在或权限不足 |
| 4 | 读取或存储失败 |
| 5 | 未预期的内部错误 |

其他命令的特殊情况见 `wherefrom --help` 和[发布检查清单](docs/release-validation.md)。`debug-zone` 成功读取数据流时返回 0（即使流为空），未找到数据流时返回 1。

## 隐私与限制

- 查询在本机进行，不修改文件内容或 Mark of the Web。只有主动打开页面时才会发起浏览器请求。
- 没有账号、遥测、云同步、常驻后台服务或旧下载导入。从源码构建可能下载开发依赖。
- 来源网址是证据，不是安全评级。来源未知不代表文件危险。
- 分享命令输出前，请对私人文件名、网址、查询参数和 token 脱敏。
- 已在 Windows 11 x64 / NTFS 上测试；其他文件系统和网络共享可能有差异。`Zone.Identifier` 单次最多读取 64 KiB；损坏字段会报告问题，不补造缺失数据。
- 查询期间若其他进程修改文件，结果不保证是原子快照。

## 构建与贡献

在 Windows 上安装 [.NET 10 SDK](https://learn.microsoft.com/dotnet/core/install/windows)，然后执行：

```powershell
dotnet restore WhereFrom.sln
dotnet test WhereFrom.sln -c Release -p:Platform=x64
node --test extension/test/worker.test.js
./scripts/publish.ps1
./scripts/check-repository-hygiene.ps1 -Package artifacts/WhereFrom-preview-win-x64.zip
```

[Windows 工作流](.github/workflows/windows.yml)还会构建并测试安装器。架构和维护原则见 [ENGINEERING.md](ENGINEERING.md)，用户及发布文档见 [docs/README.md](docs/README.md)。如需报告问题，请在 [GitHub Issues](https://github.com/opsorart/WhereFrom/issues) 提供可复现步骤和脱敏后的输出。

采用 [MIT 许可证](LICENSE)。