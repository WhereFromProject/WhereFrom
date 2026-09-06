# Milestone 3：单文件 CLI 验证记录

## 范围与设计

已基于当前工作区已有的 M3 CLI/测试改动完成检查、修正和补充。M2 Core 模型、接口、parser 及原始 ADS reader 均未修改。

- CommandLine 增加单文件参数分派、--help、--version；保留 debug-zone。
- ProvenanceFormatter 直接消费稳定的 ProvenanceResult，不建立新的领域模型或 DTO。
- 完整、部分和无可用来源分别输出；不推断缺失 URL，不合并 Source 与 Referrer。
- TerminalText 在普通报告的单行值中转义控制字符；raw 诊断继续保留换行和制表符。
- 字段问题通过 stderr 提示，不清空其他有效信息。意外异常不泄漏内部异常消息或 stack trace。
- 没有引入 CLI framework、DI 容器或新 NuGet 包。

无参数返回用法提示和退出码 2。裸 debug-zone 保留为诊断命令，缺少文件参数时返回 2；同名文件可通过显式路径查询。

## 文件系统能力

M3 要求区分不支持 ADS 与没有来源。WindowsZoneProvider 在流缺失或读取失败时增加能力查询，仍使用现有 ReadFailed + Error 表达不可用情况，没有修改 M2 模型。

NamedStreamSupport 使用 CreateFileW 的零数据访问权限取得文件句柄，再调用 GetVolumeInformationByHandleW 检查 FILE_NAMED_STREAMS。
只查询元数据，不要求读取正文，也不请求写入权限；句柄通过 SafeFileHandle 释放。长路径在需要时转为扩展路径形式。

- 支持命名流但没有流：NoMetadata，CLI 报告没有来源，退出码 1。
- 能力明确不支持：ReadFailed，CLI 提示 Alternate data streams are unavailable on this filesystem.，退出码 4。
- 缺失流且能力查询失败：明确提示无法确定可用性，退出码 4。
- 已有读取错误且能力查询也失败：保留原始读取错误。
- 成功读到流时不增加能力查询，避免要求远程文件系统额外支持卷管理查询。

参考 [Microsoft GetVolumeInformationByHandleW](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-getvolumeinformationbyhandlew)。
官方文档注明某些 SMB 情况不支持此查询；本阶段没有把查询失败自动当成“不支持 ADS”。

## Validation

环境：Windows 11 x64、G: NTFS、.NET SDK 10.0.400。

| 检查 | 结果 |
| --- | --- |
| dotnet build | PASS，0 errors、0 warnings |
| dotnet test | PASS，144 通过、0 失败、0 跳过 |
| Core 测试 | 11 通过 |
| Windows / CLI 测试 | 133 通过 |
| 实际 wherefrom.exe | 单文件、无来源、缺失文件、目录、帮助、版本、raw 诊断通过 |
| Unicode | 真正的中文/emoji 路径及 UTF-8 进程输出通过 |
| 只读性 | 实际程序运行前后正文和 ADS 字节一致 |
| M2 稳定性 | Core、parser 和原始 reader 无改动 |

基线测试复现并修正了：
1. 缺少参数的 debug-zone 被当成文件路径。
2. 测试源文件中的旧编码及丢失的 emoji 导致非法文件名。
3. 控制字符断言使用语言相关比较导致误报，改为 ordinal 比较。

测试覆盖完整记录、仅 Zone、仅来源 URL、仅 Referrer、空流、损坏字段、没有 ADS、Unicode、目录、缺失文件、访问拒绝的格式化结果、读取失败、未预期异常、控制字符转义和现有 raw 入口。

文件系统测试分开标注：
- **真实 Windows/NTFS**：能力标志、零数据访问句柄、正文独占时查询、长路径、查询失败。
- **模拟能力结果**：不支持命名流、能力查询失败及读取失败组合。不能据此宣称已在真实 exFAT/网络盘上验证。

README.md 已改为英文，README.zh-CN.md 提供对应中文说明，顶部互相链接。README 仅包含用户可见能力、使用方式和限制；本记录保存内部验收信息。

## Manual verification required

在 PowerShell 和 Windows Terminal 中，对以下已准备的样本运行：

```powershell
G:\WhereFrom\src\WhereFrom.Cli\bin\Debug\net10.0-windows\wherefrom.exe "C:\path\to\file.exe"
$LASTEXITCODE
```

| 文件 | 验证重点 |
| --- | --- |
| Chrome 下载文件 | 实际 Source、Referrer、Zone，与 M1 原文一致 |
| Edge 下载文件 | 保留浏览器实际字段差异 |
| 本地文件 | 无 ADS 时显示 No provenance information found.，退出码 1 |
| GitHub release asset | 下载端点和人类页面分别展示，不互相替换 |
| PDF | 文件名和可用来源正常显示 |
| ZIP | 文件名和可用来源正常显示 |
| EXE | 只读查询，不运行目标文件 |

不同类型可由同一组样本覆盖。只有 Zone 时应显示 Zone 和 No source URL recorded.；仅 Referrer 时仍显示该页面。
尾部 U+0000 不应出现在普通来源 URL 中；debug-zone 仍保留其可见转义。

还请验证：

```powershell
G:\WhereFrom\src\WhereFrom.Cli\bin\Debug\net10.0-windows\wherefrom.exe --help
G:\WhereFrom\src\WhereFrom.Cli\bin\Debug\net10.0-windows\wherefrom.exe --version
```

确认帮助内容正确、版本为 WhereFrom 0.1.0，以及中文/emoji 在终端中没有乱码。

若有现成的 exFAT 或其他不支持 ADS 的卷，可选择其上一个已有文件验证退出码 4 和 ADS 不可用提示。无需格式化磁盘。
真实 ACL 拒绝和网络共享场景仍建议在具备相应环境时检查；不能为构造测试而修改现有下载文件的权限或 MotW。

## Known limitations

- exFAT/网络共享尚未完成本次真实介质验证；能力查询失败会如实报告。
- 普通报告保留完整 URL，不移除 query/fragment；分享前需脱敏。
- 使用同步、有限大小读取，不提供取消或原子快照。
- 没有从来源信息推断安全性、浏览器、下载时间或文件身份。
- 仅完成 M3 的实现和自动验收，等待用户对新报告的手工反馈。

## Not implemented

没有 scan、--json、open、config、history、GUI、数据库、扩展或后台服务。
未创建 commit，完成后停止，不进入 M4。
