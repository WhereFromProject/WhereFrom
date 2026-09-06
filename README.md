# WhereFrom

查看 Windows 文件中已有的来源线索。

下载了一个 ZIP、安装包或 PDF，过一段时间却忘了它来自哪里？WhereFrom 帮你查看文件里保留的来源信息，例如下载网址和来源页面。

项目仍处于早期开发。目前提供命令行工具，用于显示 Windows `Zone.Identifier` 中的原始文本；文件可能没有这项信息，也可能只有部分字段。

## 快速开始

从源码运行需要 **Windows** 和 [.NET 10 SDK](https://learn.microsoft.com/dotnet/core/install/windows)。只有 .NET 运行时还不够。

下载或克隆本仓库后，在仓库根目录打开 PowerShell：

```powershell
dotnet build
dotnet run --project src/WhereFrom.Cli -- debug-zone "C:\Users\YourName\Downloads\example.zip"
```

将示例路径替换为要查看的文件。路径包含空格时，请保留双引号。

某个文件的输出可能是：

```ini
[ZoneTransfer]
ZoneId=3
HostUrl=https://example.com/download/example.zip
ReferrerUrl=https://example.com/download
```

工具显示文件中实际存在的文本，不补齐缺失字段，也不推断来源是否可信。

如果没有找到这项元数据，会显示：

```text
No Zone.Identifier stream found.
```

空的元数据流、文件不存在、权限不足和读取失败会有各自的提示。

## 命令

构建后，也可以直接运行程序：

```powershell
.\src\WhereFrom.Cli\bin\Debug\net10.0-windows\wherefrom.exe debug-zone "C:\path\to\file.exe"
.\src\WhereFrom.Cli\bin\Debug\net10.0-windows\wherefrom.exe --version
```

当前可用命令是 `debug-zone <file>` 和 `--version`。尚不支持目录扫描、格式化来源报告或 JSON 输出，也没有图形界面和右键菜单。

### 在脚本中使用

读取到的原文写入标准输出；错误、空流说明和隐私提醒写入标准错误。PowerShell 中可通过 `$LASTEXITCODE` 获取结果：

| 退出码 | 含义 |
| --- | --- |
| 0 | 成功读取，包括空流；或成功显示版本 |
| 1 | 未找到 Zone.Identifier |
| 2 | 参数或路径不合法，或输入的是目录 |
| 3 | 文件不存在或权限不足，具体原因见提示 |
| 4 | 读取失败、编码错误或内容超过大小限制 |

## 隐私与安全

- **只读**：不修改被检查的文件，不删除、修改或解除 Mark of the Web。
- **本地运行**：检查文件不需要联网，没有账号、遥测或云同步。首次构建需要下载开发依赖。
- **原文可能包含隐私信息**：输出保留完整 URL，包括查询参数和可能存在的 token。分享输出前请检查并脱敏。
- **来源不是安全证明**：没有来源信息不代表文件危险，有来源网址也不代表文件安全。

## 使用限制

- 当前仅面向 Windows；已验证环境为 Windows 11 x64 和 NTFS。其他文件系统及网络盘的行为可能不同。
- 无法恢复不存在的来源信息，也不能仅凭“没有元数据”判断文件是否曾经下载过。
- 这是原始文本查看工具，不验证 URL、不解释 ZoneId，也不提供文件移动后的来源关联。
- 单次最多读取 64 KiB；超过限制时会报错，不会静默截断。
- 默认按 UTF-8 读取，并识别字节顺序标记（BOM）；不保证兼容所有旧编码或损坏内容。
- 为避免终端执行控制序列，危险控制字符会显示为 `\uXXXX`；换行和制表符保留。输出不属于逐字节导出。

## 反馈与贡献

欢迎在 [GitHub Issues](https://github.com/strategist0/WhereFrom/issues) 报告问题。请说明 Windows 版本、获取文件的方式、运行的命令及实际提示；无需上传私人文件或未经脱敏的 URL。

工程设计见 [ENGINEERING.md](ENGINEERING.md)，平台验证资料见 [docs/](docs/)。

## 许可证

[MIT](LICENSE)。
