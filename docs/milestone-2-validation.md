# Milestone 2：核心模型与 Windows provider

## 范围与前提

已阅读 AGENTS.md、ENGINEERING.md、当前 Milestone 2 要求，以及用户确认完成的 [Milestone 1 实测记录](milestone-1-validation.md)。

本阶段新增内部模型、parser 和 provider，未接入新的 CLI 命令。现有 debug-zone 仍直接使用 M1 读取器，原始输出及控制字符显示保持原样。
README 不更新：本次没有改变用户可见的命令行为。

## Core model

Core 继续使用 net10.0，无项目引用或额外包依赖，不引用 Windows API，也不包含 Zone.Identifier/NTFS ADS 的知识。

| 类型或字段 | 含义 |
| --- | --- |
| IProvenanceProvider.Inspect(path) | 最小同步读取接口 |
| ProvenanceResult.Path | 调用者传入的文件路径，不充当文件身份，也不作移动关联 |
| Provider | 提供证据的来源标识，由具体 provider 填入 |
| Status | Read、NoMetadata、FileNotFound、AccessDenied、ReadFailed、InvalidPath、NotAFile |
| Zone | 可空的数字 Id 和可空名称；Core 不定义 Windows Zone 的编号语义 |
| SourceUrl / ReferrerUrl | 两条独立的、经过语法检查的原文本 URL；缺失或无效时为 null |
| Issues | 字段级问题：EmptyValue、InvalidValue、DuplicateField、InvalidFormat、MissingSection |
| Error | I/O 或解码错误说明；不用于代替字段级问题 |
| HasProvenance | 读取状态为 Read，且 Zone 或任一 URL 有可用值；不是安全或可信度判断 |

读到了空流或完全损坏的文本，仍是 Read，但 HasProvenance 为 false。
NoMetadata 表示读取器未找到来源元数据，不能等同于“文件从未被下载过”。
未出现的字段为 null 且没有字段问题；出现但为空的字段为 null + EmptyValue；无效值为 null + InvalidValue。

## Windows provider

WindowsZoneProvider 实现 Core 接口，Provider 标识为 Windows.ZoneIdentifier。

流程：

1. 复用 M1 的 ZoneIdentifierReader.Read(path)。
2. 将读取失败或缺失状态映射到 Core，保留已有错误说明。
3. 成功读取后，在内存中调用 ZoneIdentifierParser，返回 Core 数据。

provider 不读取正文、不写 ADS、不访问网络，也不推断浏览器、下载时间或文件身份。
没有新增 NuGet 包。parser 为 Windows 项目内部类型，仅向对应测试程序集开放。

采用同步方法，是因为当前读取器本身同步且限制为 64 KiB；没有增加 Task.Run、虚假异步包装、后台队列或依赖注入框架。
此接口目前没有取消支持，阻塞 I/O 的限制沿用 M1。

## Parser 容错规则

- 仅处理有效的 [ZoneTransfer] section，section 与键名忽略大小写，允许周围的空格和制表符。
- 未知键、其他 section、空行和整行注释不影响已知字段；URL 中的 # 和 = 不当作注释或新的分隔符。
- 只按第一个 = 分隔键和值，字段顺序不影响结果。
- 缺失字段留空；空值、非法值和格式问题分别记录，不清空其他有效字段。
- 没有有效 section 的非空文本记录 MissingSection；不猜测其他 section 或无 section 的字段归属。
- 不完整 section 头记录 InvalidFormat 并停止归属到上一 section；后续有效 section 可以恢复解析，之前的有效字段仍保留。
- 重复已知字段记录 DuplicateField，采用第一个有效值。前面的无效值不会阻止后面的有效值，已有有效值也不会被后面的损坏或冲突值覆盖。原文仍可通过 M1 的 raw 入口查看；本阶段不实现冲突 resolver。
- ZoneId 接受非负 Int32 数值。0–4 的名称由 Windows parser 提供；其他非负编号保留数字、名称为 null，不猜测其含义。负数、非数字或溢出记录 InvalidValue。

### NUL：解析副本与原始证据分开

依据用户实测，只从整段解析副本末尾移除连续的 U+0000、CR/LF、空格和制表符。
不全局删除 NUL，不把字面的六字符序列 `\u0000` 当成终止符，也不把 URL 中的 `%00` 解码后删除。

出现在中间字段中的 NUL 仍使对应 URL 无效；其余可解析字段继续保留。
读取器返回的原文本、文件正文和 ADS 字节均保持不变。

### URL：保留信息，不作打开决策

- HostUrl 映射为 SourceUrl；ReferrerUrl 单独保留，不互相替换。
- 不将 codeload.github.com 或 API/download 端点改写成人类页面。
- 保留原 URL 的 Unicode、大小写、query、fragment 和百分号编码；不使用 Uri 的规范化结果覆盖证据文本。
- 先检查控制字符、空白、明显非法字符、错误百分号转义和未配对的 UTF-16 代理项，再使用 Uri.TryCreate 检查绝对 URI 和显式 scheme。
- 本机 .NET 10 实测中，IsWellFormedOriginalString 会拒绝含 emoji 的测试 URL，因此不将它作为唯一有效性判断。回归测试同时覆盖合法 emoji 和混有错误字符的 emoji URL。
- 合法的 file、ftp、javascript 等绝对 URI 也可作为原文证据；“能解析”不代表“允许执行”。本阶段不打开任何 URL，HTTP/HTTPS 打开许可属于后续 milestone。
- 字段问题只记录类别，不在错误列表中重复保存可能含 token 的原始 URL。

上述规则是本工具的解析约定，不宣称复刻所有 Windows INI/URL API 的容错行为，也不是完整的 RFC 验证器。

## 实测对工程假设的影响

- 不能假定原始字段已是可直接解析的干净 URL：用户的浏览器样本确实带尾部 U+0000。修正方式是在解析副本中处理尾部填充，保留证据；不是修改 MotW。
- HostUrl 与 ReferrerUrl 的不同地址是有效证据，不是必须消除的冲突；本阶段不推断哪个一定是最终下载地址。
- 跨 NTFS 卷复制保留 ADS 的结果不构成文件身份或移动追踪能力。
- ENGINEERING.md 的长期事件模型、置信度 resolver、Storage 等不属于当前 M2。没有为这些内容预先建立结构。
- 未发现必须推翻 M1 只读读取机制的实机证据；当前 .NET URI 校验差异已用测试复现并在 parser 中处理。

## 资料依据

- Windows 预定义 Zone 编号参考 [Microsoft URLZONE](https://learn.microsoft.com/en-us/previous-versions/windows/internet-explorer/ie-developer/platform-apis/ms537175(v=vs.85))；本项目的名称映射和未知编号处理属于明确的展示约定，不调用系统来推断有效安全区域。
- URI 构造行为参考 [Microsoft Uri.TryCreate](https://learn.microsoft.com/en-us/dotnet/api/system.uri.trycreate?view=net-10.0)；对具体输入的接受或拒绝以本次 .NET 10 回归测试为准。

## Validation

环境沿用 Windows 11 x64、G: NTFS、.NET SDK 10.0.400。

| 检查 | 结果 |
| --- | --- |
| dotnet build | PASS，0 errors，0 warnings |
| dotnet test | PASS，106 通过，0 失败，0 跳过 |
| Core 测试 | 11 通过 |
| Windows / CLI 测试 | 95 通过 |
| M1 回归 | 原有 36 个测试全部通过 |
| Core 引用检查 | 无项目引用，无额外包 |
| git diff --check | PASS |

首轮测试发现一个 emoji URL 被严格 URI 检查误判的问题，修正后全部通过。编辑过程中的字符字面量转义错误也已修正，最终编译没有错误或警告。

新增覆盖：完整记录、三种单字段记录、无 URL、空值、字段顺序、额外 section、未知字段、格式损坏、重复字段、无效/Unicode URL、尾部与中间 NUL、百分号编码、未知 Zone 编号，以及真实 ADS 到 Core 的状态映射。

provider 的真实 NTFS 集成测试覆盖 Unicode 路径、权限拒绝、独占流、无流、文件不存在、解码失败，以及读取前后正文/ADS 字节和最后写入时间一致。测试仅修改和删除自己新建的临时样本。

## Manual verification required

本阶段没有新 CLI 入口，也不需要重新采集 M1 的九类浏览器样本。

建议使用已验收的一个 Chrome 文件、一个 Edge 文件和一个本地 TXT，确认现有命令没有发生用户可见的回归：

```powershell
G:\WhereFrom\src\WhereFrom.Cli\bin\Debug\net10.0-windows\wherefrom.exe debug-zone "C:\path\to\downloaded-file.zip"
$LASTEXITCODE
```

下载样本应仍显示原有字段、原始 URL 和尾部 NUL 的可见转义（若该样本存在），而不是输出 M2 解析后的结构。
本地 TXT 若仍无 ADS，应显示 No Zone.Identifier stream found.，退出码为 1。
不要修改或解除样本的 MotW。

模型和 provider 的可重复检查：

```powershell
dotnet test G:\WhereFrom\tests\WhereFrom.Platform.Windows.Tests\WhereFrom.Platform.Windows.Tests.csproj --filter "FullyQualifiedName~ZoneIdentifierParserTests|FullyQualifiedName~WindowsZoneProviderTests"
```

此命令只使用测试新建的样本。面向用户的解析结果展示留给 Milestone 3。

## Known limitations

- 不提供原子文件快照；文件系统能力探测、网络盘、重解析点和其他平台的限制沿用 M1。
- parser 不保留未知字段到 Core；原始文本仍由 M1 的读取入口提供。
- 第一个有效重复值胜出是明确的容错约定，不代表哪个值更可信。
- 没有建立来源可信度、浏览器或下载时间推断规则。
- 当前仅通过合成测试复现用户报告的结构，未读取用户的真实私有下载文件重新解析。
- 没有为绕过 warning 或失败测试禁用检查。

## Not implemented

没有新增正式单文件 CLI、scan、JSON、open、hash、SQLite、GUI、浏览器扩展、后台服务、移动追踪或跨平台 provider。
未创建 Git commit；本阶段完成后停止，等待用户指示。
