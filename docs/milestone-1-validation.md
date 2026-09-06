# Milestone 1：Windows ADS 验证记录

## 自动验证环境与范围

- Windows 11 专业版 x64，版本 10.0.26200。
- .NET SDK 10.0.400。
- 测试目录位于 G:，实机查询文件系统为 NTFS。
- 合成样本由测试新建，结束后删除；不修改任何现有下载文件的正文、ADS 或权限。
- dotnet build：通过，0 errors、0 warnings。
- dotnet test：36 通过，0 失败，0 跳过（Core 1，Windows/CLI 35）。
- 实际 wherefrom.exe 已验证 Unicode 文件路径、UTF-8 输出、原始 URL 保留、退出码及正文/ADS 字节不变。

这些结果只证明当前环境和合成样本的行为，不证明 Chrome/Edge 一定写入某些字段。

## 实际观察与实现选择

| 实际观察 | 实现选择 |
| --- | --- |
| 不存在的文件与不存在的 ADS 都可产生 FileNotFoundException / Windows 错误 2 | 先查询基础文件属性，读取流遇到缺失时再次检查基础文件；不使用会隐藏错误的 File.Exists 判定所有情况 |
| 独占 Zone.Identifier 时产生 IOException / Windows 错误 32 | 返回 ReadFailed，保留错误码，不当作流缺失 |
| 拒绝测试文件的 ReadData 权限时产生 UnauthorizedAccessException / Windows 错误 5 | 返回 AccessDenied，不当作文件或流缺失 |
| 独占文件正文仍能读取其 Zone.Identifier | 不以正文能否打开判断 ADS 能否读取 |
| 空流能成功读取为零长度字符串 | Found + 空文本，与 StreamNotFound 分开 |
| 不完整 INI、未知字段、无效 URL 可以作为原始文本读出 | 不做 parser，不删除或推断字段 |
| 读取前后正文、Zone.Identifier、另一个 ADS 的字节及最后写入时间保持一致 | 产品代码只使用 FileMode.Open / FileAccess.Read，没有写入或解除 MotW 的路径 |

流的命名及共享模式也可参考 [Microsoft：File streams](https://learn.microsoft.com/en-us/windows/win32/fileio/file-streams)；
上述错误与权限结论来自本次真实 .NET 10 / NTFS 实验和测试，而非仅照抄文档。

用户已完成真实浏览器样本验证，结果见下方“用户手工验证结果”。这些结果补充了合成测试，但不能推广为所有下载文件都有 HostUrl/ReferrerUrl，也不能证明文件时间就是下载时间。

### 实测对后续设计的约束（仅记录，尚未实现）

- 浏览器样本在 HostUrl 后的原始 ADS 文本末尾包含 U+0000。不能假定字段文本天然就是可直接用于 URL 解析的字符串；后续解析需单独确定尾部终止符的处理方式，保留原始证据，不回写 ADS。本次不改变现有读取和控制字符显示行为。
- HostUrl 可以是下载端点，ReferrerUrl 可以是面向人的页面，两者不应自动等同。以下观察不证明 HostUrl 在所有场景中都是最终重定向地址，也不建立通用的 URL 选择规则。
- 本次跨 NTFS 卷复制保留了 ADS 及其值。“复制必然丢失来源元数据”不符合这个样本；ENGINEERING.md 中“复制可能丢失某些来源元数据”的条件性描述仍不能被推广为必然丢失或必然保留。

## 自动测试覆盖

- 文件不存在、父目录不存在、存在文件但没有 ADS。
- 完整内容、空流、不完整/非法 INI、未知字段。
- Unicode 路径与 Unicode 文本。
- UTF-8、带 BOM 的 UTF-8、UTF-16 LE/BE；无效 UTF-8 报错。
- 64 KiB 边界和超限拒绝。
- 文件正文、目标 ADS、其他 ADS 字节及最后写入时间保持不变。
- 只读文件、流占用、正文占用、真实 ACL 权限拒绝及恢复后可读。
- 目录输入、无效路径、已带 ADS 后缀的路径。
- CLI 原始输出、空流、缺失区分、退出码、stderr、控制字符转义及版本回归。

## 用户手工验证结果

**状态：Milestone 1 手工验证已完成，由用户明确确认。**

以下是用户提供的真实环境观察，与上方自动测试结果分开记录；本次文档更新没有重新运行或独立复验这些下载样本。
反馈未包含具体文件名、浏览器版本、样本数量、完整 URL 或逐项退出码，因此不补写这些信息，也不据此计算覆盖率。

| 实测场景 | Zone.Identifier | ZoneId | ReferrerUrl | HostUrl | 观察 |
| --- | --- | --- | --- | --- | --- |
| Chrome：GitHub 下载 | 存在 | 3 | 存在 | 存在 | 用户实测 |
| Edge：GitHub 下载 | 存在 | 3 | 存在 | 存在 | 用户实测 |
| Chrome：普通网页下载 | 存在 | 3 | 存在 | 存在 | 用户实测 |
| Edge：普通网页下载 | 存在 | 3 | 存在 | 存在 | 用户实测 |
| 本地新建 TXT | 不存在 | 不适用 | 不适用 | 不适用 | 没有 Zone.Identifier stream |
| 下载文件从 Downloads 复制到另一个 NTFS 卷 | 保留 | 与复制前一致，具体值未单列 | 与复制前一致 | 与复制前一致 | Zone.Identifier 流及其值均保留；具体复制方式未提供 |

### 原始文本和 URL 的额外观察

- **尾部 U+0000**：浏览器生成的样本在 HostUrl 后的原始 ADS 文本末尾包含 U+0000 字符。本记录使用可见记法 `U+0000`；它不是原始数据中六个字面字符。不能把这个观察扩展为所有浏览器、所有版本都具有该特征。
- **GitHub 下载**：HostUrl 可能指向 `codeload.github.com`，而 ReferrerUrl 指向面向用户的 GitHub releases 页面。两者的地址差异是实际证据，不应人为统一。
- **网页服务下载**：HostUrl 可能是 API/download 端点，而 ReferrerUrl 指向面向用户的网站。这两种 URL 的用途不同。
- Chrome 与 Edge 在已反馈的四类浏览器场景中都包含上述三个字段；这仅表示字段存在情况一致，不表示字段值、其他字段、顺序或字节内容完全一致。

原验证清单中的 PDF、ZIP、EXE 与此次样本之间的具体对应关系未单独提供，不推测文件类型或添加未经反馈的逐类型结论。

### 复现命令（保留供后续参考）

在仓库根目录构建后，对要检查的实际文件运行：

```powershell
Set-Location G:\WhereFrom
dotnet build
G:\WhereFrom\src\WhereFrom.Cli\bin\Debug\net10.0-windows\wherefrom.exe debug-zone "C:\path\to\file.exe"
$LASTEXITCODE
```

有 ADS 时显示实际原文（危险控制字符以可见形式转义）；没有流时显示 `No Zone.Identifier stream found.`。
复现不需要创建、修改或解除已有文件的 ADS/MotW。分享输出时可脱敏私人信息，但应保留字段缺失、空值、控制字符及 URL 结构之间的差异。

## 已知限制与下一步边界

- 用户已确认 Milestone 1 手工验证完成；本次仅记录实测结果，不改变实现行为。Milestone 2 尚未开始，须等待用户另行指示。
- exFAT、UNC/网络盘、重解析点、超长路径及其他 Windows 版本尚未专门验证。
- 本阶段不探测文件系统能力，StreamNotFound 不保证能区分“流被删除/从未存在”和“不支持 ADS”。
- 64 KiB 是诊断入口的资源限制，不是 Windows ADS 的容量上限。
- 这是文本读取，不是二进制取证导出；默认 UTF-8/BOM 解码不保证兼容所有旧代码页或损坏的 UTF-16/UTF-32 数据。
- 允许其他进程共享访问，不提供原子快照；读取期间的外部替换或并发修改仍可能影响观察。
- 只读指应用不请求写入或修改证据；操作系统仍可能按自身策略维护最后访问时间。应用不会为“恢复时间”反向写入文件元数据。
- 没有 provenance model、parser、置信度判断、hash、scan、JSON、open、SQLite、GUI、扩展或后台服务。
