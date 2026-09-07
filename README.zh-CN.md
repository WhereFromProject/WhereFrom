[ENGLISH](README.md) | 简体中文

# WhereFrom

查看 Windows 文件中已有的来源线索。

下载了 ZIP、安装包或 PDF，过一段时间却忘了它来自哪里？WhereFrom 读取文件上已经存在的来源元数据，显示可用的下载网址、来源页面和 Windows Zone 信息。

项目仍处于早期开发，目前提供命令行工具。本地运行，不修改文件，也不解除 Mark of the Web。

## 安装

Windows x64 便携 ZIP 自带 .NET，**无需另行安装 .NET 运行时**。解压 `WhereFrom-0.1.0-win-x64.zip`，在其中的 `WhereFrom-win-x64` 文件夹打开 PowerShell：

```powershell
.\wherefrom.exe --version
.\wherefrom.exe "C:\Users\YourName\Downloads\example.zip"
```

可按下方步骤从源码生成发布包。仓库中的说明不代表 GitHub Release 已经公开发布。发布包尚未签名；自带运行时会将原生组件解压到用户临时目录，该目录需要可写。

## 快速开始

将示例路径替换为实际文件。路径含空格时，请保留双引号。

信息完整的文件可能显示：

```text
example.zip

Source
  https://example.com/download/example.zip

Referrer
  https://example.com/download

Windows Zone
  Internet (3)
```

Source 是记录的下载地址，可能是 API 或 CDN 端点；Referrer 可能是面向用户的页面。WhereFrom 分别展示，不补齐缺失信息。

如果只有 Zone，会显示 Zone 并提示 `No source URL recorded.`。没有可用来源时显示：

```text
example.zip

No provenance information found.
```

## 命令

在解压后的发布包目录中运行：

```powershell
.\wherefrom.exe "C:\path\to\file.exe"
.\wherefrom.exe --help
.\wherefrom.exe --version
```

每次查询一个文件。如果文件名以 `-` 开头或名为 `debug-zone`，请使用完整路径或加上 `.\` 前缀。

### 打开来源页面

```powershell
.\wherefrom.exe open "C:\path\to\file.zip"
```

在默认浏览器中打开记录的 Referrer URL；没有可用的已解析 Referrer 时，使用 Source URL。选中的地址显示在 `Opening:` 后。只允许有效的 HTTP/HTTPS URL；选中地址使用其他协议时会明确拒绝，不回退打开另一个地址。

没有 URL 时提示 `No source URL is available for this file.`，返回 1。拒绝 URL 返回 2，浏览器启动失败返回 4，成功发起打开返回 0。文件读取错误沿用下方单文件退出码。0 仅表示 Windows 接受了打开请求，不代表网页已经加载成功。

此命令会主动打开浏览器，可能访问记录的网站，并传入完整查询参数和 fragment。普通查询和扫描不会打开 URL。`open` 不支持 `--json`。

### 扫描目录

```powershell
.\wherefrom.exe scan "$HOME\Downloads"
```

仅扫描第一层，包含隐藏文件和系统文件。每行显示文件名，以及依次优先选择的 Referrer 主机名、Source 主机名或 Windows Zone。合法但不含主机名的 URL 显示为 `URL recorded (no host)`。无可用来源显示 `Unknown`；读取失败显示 `Error`，详情写入标准错误。完整 URL 仍可通过单文件查询查看。

汇总包含 `Scanned`、`Known provenance`、`Unknown`、`Errors` 和 `Skipped reparse points`。扫描数等于已知来源数、未知数和错误数之和。普通子目录不进入；重解析点（包括链接和 junction）跳过。如果扫描目录本身是重解析点，请直接提供目标目录。

扫描完成返回 0，包括空目录或全部未知的结果。目录输入不合法返回 2；根目录不存在或无权访问返回 3；文件读取失败或目录枚举中断返回 4；未预期错误返回 5。单个文件失败后继续扫描；枚举中断时会在标准错误明确提示，并给出已处理部分的统计。

扫描串行执行且只读，不支持 `--recursive` 或扫描 `--json`。输出按文件系统枚举顺序排列，以制表符分隔；窄终端中的长文件名可能换行。

### JSON 输出

在文件路径后添加 `--json`，返回一个 JSON 对象：

```powershell
$result = .\wherefrom.exe "C:\path\to\file.exe" --json | ConvertFrom-Json
$code = $LASTEXITCODE
$result.sourceUrl
```

版本 1 的契约始终包含 `schemaVersion`（1）、`path`（传入的路径）、`hasProvenance`（布尔值）、`zone`（`{ "id": 3, "name": "Internet" }` 或 `null`）、`sourceUrl` 和 `referrerUrl`。缺失的 URL 和未知 Zone 名称为 `null`，不会省略字段。JSON 解码后，URL 保留原有拼写、查询参数和 fragment。

查询完成时，即使无来源也返回 JSON（退出码 1）。参数或读取错误不返回 JSON，请检查退出码和标准错误。元数据问题提示仅写入标准错误。不要使用 `2>&1` 将其混入 JSON 管道。

### 原始元数据

诊断命令用于查看原始文本：

```powershell
.\wherefrom.exe debug-zone "C:\path\to\file.exe"
```

它保留字段顺序和 URL，将危险控制字符显示为 `\u0000` 等可见转义。空流与不存在的流会分别提示。

### 在脚本中使用

报告写入标准输出，错误和元数据问题提示写入标准错误。PowerShell 中可读取 `$LASTEXITCODE`：

| 退出码 | 含义                                                   |
| ------ | ------------------------------------------------------ |
| 0      | 找到可用来源，或成功显示帮助/版本                      |
| 1      | 查询完成，但没有可用来源                               |
| 2      | 参数或路径不合法，或输入的是目录                       |
| 3      | 文件不存在或权限不足，具体原因见错误提示               |
| 4      | 读取失败、ADS 不可用、能力查询失败、编码错误或内容超限 |
| 5      | 未预期的内部错误                                       |

对 `debug-zone`，0 表示成功读取流（包括空流），1 表示未找到流。

## 隐私与安全

- **只读**：不修改被检查的文件，不删除、修改或解除 Mark of the Web。
- **本地运行**：检查本地文件无需互联网，没有账号、遥测或云同步。首次构建需要下载开发依赖。
- **URL 可能含隐私信息**：报告和原始输出均保留查询参数和 fragment，其中可能包含 token。分享前请检查并脱敏。
- **来源不是安全证明**：没有信息不代表文件危险，有来源网址也不代表文件安全。仅显式 open 命令会将通过校验的 HTTP/HTTPS URL 交给默认浏览器。

## 使用限制

- 仅面向 Windows。已测试本地 Windows 11 x64 / NTFS；其他文件系统和网络共享可能表现不同。
- 无法恢复不存在的元数据，也不能仅凭缺失信息推断文件历史。
- 损坏字段会产生提示，其他可用字段仍保留。未知 Zone 编号仅显示数字，不猜名称。
- 未找到流时会查询文件系统能力；若查询失败，会报告错误，而不是断言没有来源。部分网络共享不支持这项查询。
- 单次最多读取 64 KiB，超限报错，不静默截断。
- 默认按 UTF-8 解码并检测 BOM，不保证支持所有旧编码或损坏文本。
- 读取不是原子快照，其他进程可能在查询期间修改文件。
- 尚不支持 GUI、右键菜单或文件移动追踪。

## 从源码构建

在 Windows 安装 [.NET 10 SDK](https://learn.microsoft.com/dotnet/core/install/windows)，克隆本仓库后在根目录执行：

```powershell
dotnet build
dotnet test
dotnet run --project src/WhereFrom.Cli -- "C:\path\to\file.zip"
.\scripts\publish.ps1
```

发布包和 SHA256 校验文件生成在 `artifacts/`。在未安装 .NET 的机器上验收，请参阅[发布验证指南](docs/release-validation.md)。Windows 工作流执行依赖还原、构建、测试、发布及发布包检查。

## 路线图

当前 v0.1 CLI 支持单文件查询、JSON、非递归目录扫描和打开来源页面。浏览器捕获与存储计划留给 v0.2；GUI、资源管理器集成和文件追踪属于更后续的想法。这些未来能力尚未包含，也没有承诺日期。

## 反馈与贡献

欢迎在 [GitHub Issues](https://github.com/strategist0/WhereFrom/issues) 报告问题。请说明 Windows 版本、文件获取方式、执行命令及实际输出；不要上传私人文件或未经脱敏的 URL。

工程设计见 [ENGINEERING.md](ENGINEERING.md)，验证记录见 [docs/](docs/)。

## 许可证

[MIT](LICENSE)。
