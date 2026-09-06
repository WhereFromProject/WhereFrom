# WhereFrom 工程设计文档

> Windows-first、Local-first 的文件来源追踪与 Provenance 工具 · v0.1 设计基线

文档状态：Draft for implementation

目标读者：项目作者、后续贡献者、代码审查者、打包/发布维护者

核心原则：先把 Windows 上“这个文件从哪里来的？”做到最好；架构允许未来扩展到 macOS/Linux，但 v0.x 不为跨平台一致性牺牲 Windows 体验。

# 0. 执行摘要

WhereFrom 是一个 Windows-first 的开源文件来源追踪工具。它读取 Windows/浏览器已经产生的来源线索，并在必要时通过浏览器扩展补充下载上下文，最终让用户可以对任意本地文件执行“查看来源”。

产品的核心体验不是一个需要每天打开的文件管理器，而是一个“平时几乎不存在、需要时右键一下”的 Windows Utility。CLI 是稳定、可脚本化的底层入口；GUI/Explorer 集成负责普通用户体验。

| 项目决策 | 当前结论 |
| --- | --- |

| 主语言 | C# / .NET 10 |

| GUI | WinUI 3 + Windows App SDK（v0.2 起） |

| CLI | System.CommandLine 或 Spectre.Console 之一；优先减少依赖 |

| 数据库 | SQLite（Microsoft.Data.Sqlite） |

| 浏览器扩展 | TypeScript + Chromium Manifest V3 |

| 扩展与本机通信 | Chrome Native Messaging |

| MVP 平台 | Windows 11 优先；兼容目标 Windows 10 1809+ 视 WinUI/SDK 实测 |

| 隐私 | 100% local-first；无账号、无遥测、无云同步 |

| 第一版常驻后台 | 不需要 |

| 跨平台策略 | Core 可移植、Platform 分层；产品先 Windows-only |



# 1. 产品定义

## 1.1 一句话定位

WhereFrom：Right-click any file and find out where it came from.

中文：右键任意文件，查看它从哪里来。

## 1.2 要解决的问题

- 下载目录中存在大量名字模糊的 zip/exe/pdf，数周或数月后无法回忆来源。

- 浏览器下载历史可能被清理，或文件被移动后很难从浏览器反查。

- Windows 的来源/安全元数据对普通用户不直观，且不同下载方式留下的信息完整度不同。

- 开发者需要脚本化查询文件来源，而普通用户更需要 Explorer 右键和轻量详情窗口。

## 1.3 不解决的问题（v0.x 明确非目标）

- 不做杀毒软件，不判断文件是否恶意。Unknown source 只代表来源不足。

- 不做 Everything/Explorer 替代品。

- 不做云端“第二大脑”、文件内容 AI 分类或 OCR。

- 不主动移除 Mark of the Web / Zone.Identifier。

- 不在 v0.x 实现通用文件 lineage（复制、解压、构建产物链）；只为未来预留事件模型。

- 不承诺跨平台功能完全一致。

# 2. 用户场景与验收标准

## 2.1 P0 场景：查询单个文件

```
wherefrom inspect .\setup.exe
# 简写
wherefrom .\setup.exe
```

期望输出包含：文件名、当前路径、来源级别、URL（若存在）、referrer（若存在）、采集来源、时间、文件大小、可选 hash。

## 2.2 P0 场景：扫描目录

```
wherefrom scan "$HOME\Downloads"
```

输出统计：Known / Partial / Unknown，并列出可疑但不带安全定性的项目，例如来源未知的可执行文件。

## 2.3 P1 场景：Explorer 右键

用户右键文件 → “Where did this come from?” → 打开轻量详情页。详情页提供 Open source page、Copy URL、Copy report、Show raw metadata。

## 2.4 P1 场景：浏览器下载捕获

Chrome/Edge/Brave 扩展在下载完成后读取 DownloadItem，并通过 Native Messaging 发送 filename、url、finalUrl、referrer、startTime、mime、size 等字段。本机端清洗 URL 后写入 SQLite。Chrome 官方 Downloads API 当前明确提供这些字段，其中 filename 为绝对本地路径。

## 2.5 P2 场景：文件移动后仍能查询

文件从 Downloads 移到其他目录后，WhereFrom 应通过强指纹（默认 SHA-256）或受控的重定位策略重新关联既有 provenance。大文件 hash 允许延迟计算。

# 3. 产品形态

```
WhereFrom
├── wherefrom.exe            # CLI：稳定核心入口
├── WhereFrom.App.exe        # WinUI 3 图形界面
├── Native Messaging Host    # 浏览器扩展桥接（v0.2）
├── Explorer integration     # 右键菜单（v0.2/v0.3）
└── Chromium Extension       # Chrome / Edge / Brave
```

## 3.1 CLI 的角色

- 最早交付，负责验证底层来源读取是否可靠。

- 作为自动化/API 边界：支持 --json、退出码、管道。

- GUI 只能调用 Core，不复制业务逻辑。

## 3.2 GUI 的角色

- 详情视图：来源、时间、浏览器/Agent、原始元数据。

- Recent：最近捕获的文件。

- Search：按文件名、domain、provider、URL 搜索。

- Audit：扫描 Downloads 等目录并展示 Known/Partial/Unknown。

## 3.3 Explorer 集成策略

MVP 不建议一开始写复杂 COM Shell Extension。优先使用风险更低、可卸载清晰的注册式命令/SendTo/应用激活方案；等 GUI 与命令稳定后再评估 Windows 11 现代上下文菜单集成。这样可以避免 Shell 崩溃、签名和部署复杂度过早进入主路径。

# 4. 技术栈与版本基线

| 层 | 建议技术 | 说明 |
| --- | --- | --- |

| Runtime | .NET 10 | 2026 新项目基线；Core 尽量保持纯 .NET |

| UI | WinUI 3 / Windows App SDK | 微软当前推荐的新原生 Windows 桌面路线 |

| Windows API | P/Invoke / CsWin32（按需） | 只在 Platform.Windows 层使用 |

| Storage | SQLite + Microsoft.Data.Sqlite | 单机、事务、FTS 可扩展 |

| CLI | C# console | 输出 text/json；不依赖 GUI |

| Browser | TypeScript + Manifest V3 | Chromium 扩展 |

| IPC | Native Messaging | 避免暴露 localhost 服务 |

| Serialization | System.Text.Json | 减少依赖 |

| Logging | Microsoft.Extensions.Logging | 默认本地日志，可关闭/轮换 |

| Tests | xUnit | Core/Storage/Windows provider 分层测试 |

| Packaging | MSIX 或 unpackaged + installer（二选一后实测） | 不要在 P0 阶段阻塞核心功能 |



备注：微软截至 2026-09 的 Windows 开发文档仍将 WinUI 3 + Windows App SDK 作为新原生桌面应用的推荐路径。

# 5. 总体架构

```
                    ┌──────────────────────┐
                    │   Chromium Extension  │
                    └──────────┬───────────┘
                               │ Native Messaging
                               ▼
┌───────────────┐     ┌──────────────────────┐     ┌───────────────┐
│ wherefrom CLI │────▶│   WhereFrom.Core     │◀────│ WinUI 3 App   │
└───────────────┘     └──────────┬───────────┘     └───────────────┘
                                 │
                 ┌───────────────┼─────────────────┐
                 ▼               ▼                 ▼
        Platform.Windows      Storage         Providers
        (ADS/MotW/etc.)       (SQLite)        (GitHub/etc. later)
```

## 5.1 项目目录建议

```
WhereFrom.sln

src/
  WhereFrom.Core/
  WhereFrom.Storage/
  WhereFrom.Platform.Windows/
  WhereFrom.Cli/
  WhereFrom.App/
  WhereFrom.NativeHost/

extension/
  chromium/

tests/
  WhereFrom.Core.Tests/
  WhereFrom.Storage.Tests/
  WhereFrom.Platform.Windows.Tests/
  WhereFrom.IntegrationTests/

docs/
  architecture.md
  privacy.md
  threat-model.md
  data-model.md
  contributing.md
```

## 5.2 依赖方向

```
App ───────┐
CLI ───────┼──> Core <── Storage abstractions
NativeHost ┘      ▲
                  │
        Platform.Windows

Core 不引用 WinUI / Registry / Windows Shell。
```

核心约束：WhereFrom.Core 必须可在不加载 Windows UI 的测试环境中运行。Platform.Windows 可以引用 Windows-specific API，但不得把 Windows 类型泄漏到 Core 公共模型。

# 6. 核心领域模型

## 6.1 ProvenanceRecord

```
public sealed record ProvenanceRecord(
    FileIdentity File,
    ProvenanceConfidence Confidence,
    IReadOnlyList<ProvenanceEvent> Events,
    IReadOnlyList<SourceReference> Sources);
```

## 6.2 来源置信度

| 等级 | 定义 | UI 呈现 |
| --- | --- | --- |

| Confirmed | 浏览器事件 + 文件身份匹配，或多来源强一致 | Known source |

| Strong | Windows metadata 提供完整 URL/Zone 且文件路径/时间一致 | Known source |

| Partial | 仅知道 Internet Zone、domain 或下载 Agent | Partial source |

| Unknown | 没有可用 provenance | Unknown source |

| Conflict | 多个来源发生冲突 | Needs review |



置信度不是安全评分，不允许用红色“危险”语义暗示 Unknown 即恶意。

## 6.3 Event 模型

即使 v0.1 只处理 downloaded/imported，也建议一开始采用事件模型，以免未来 lineage 推翻 schema。

```
ProvenanceEvent
- Id
- FileId
- Type: Downloaded | Imported | Moved | Renamed | Copied | Extracted | Generated
- TimestampUtc
- Agent
- MetadataJson
- CreatedAtUtc
```

# 7. Windows 来源采集

## 7.1 Provider 接口

```
public interface IProvenanceProvider
{
    string Name { get; }
    Task<ProviderResult> InspectAsync(
        FileProbe probe,
        CancellationToken cancellationToken);
}
```

Provider 的职责是“采集证据”，Resolver 的职责是“合并证据并计算置信度”。不要让单个 provider 直接决定最终结论。

## 7.2 v0.1 Provider 列表

- WindowsZoneProvider：读取可获得的 Zone.Identifier / Mark-of-the-Web 相关数据。

- FileSystemProvider：路径、大小、创建/修改时间、volume/file identity（能力允许时）。

- HashProvider：SHA-256 强指纹；按文件大小阈值决定同步或后台计算。

- DatabaseHistoryProvider：如果数据库已有该文件身份，返回历史来源。

## 7.3 读取原则

- 只读系统 provenance；默认绝不执行 unblock 或删除 ADS。

- 所有原始元数据都视为不可信输入：限制长度、拒绝控制字符、输出时转义。

- 不能假设所有文件系统都是 NTFS；FAT/exFAT/网络盘可能没有 ADS。

- 复制文件可能丢失某些来源元数据；数据库记录与当前文件状态必须分开表示。

# 8. 浏览器扩展与 Native Messaging

## 8.1 为什么选择 Native Messaging

扩展与本机端通信不通过 localhost HTTP，从而避免长期监听端口、CORS/CSRF 风险和本地服务发现问题。Chrome 的 Native Messaging 机制会启动注册的 native host，并通过 stdin/stdout 交换消息。

## 8.2 扩展权限

```
{
  "manifest_version": 3,
  "permissions": ["downloads", "nativeMessaging"],
  "background": { "service_worker": "background.js" }
}
```

downloads 权限本身会触发浏览器权限提示，README/商店描述必须解释用途。

## 8.3 下载事件 payload

```
{
  "schemaVersion": 1,
  "event": "download.completed",
  "downloadId": 123,
  "filename": "C:\\Users\\alice\\Downloads\\tool.zip",
  "url": "https://example.com/download?...",
  "finalUrl": "https://cdn.example.com/file?...",
  "referrer": "https://example.com/releases",
  "startTime": "2026-09-06T01:23:45Z",
  "mime": "application/zip",
  "fileSize": 1234567,
  "incognito": false
}
```

## 8.4 URL 清洗

URL 在写库前必须通过 sanitizer。默认策略建议“保留 scheme/host/path，丢弃 query 与 fragment”；未来对明确安全的 provider 做白名单字段保留。

```
https://example.com/file?token=SECRET&signature=...
↓
https://example.com/file
```

- 禁止保存 Authorization/Cookie 等请求头。

- 无痕下载默认不入库，除非用户显式开启；扩展 UI 明示该选项。

- Native Host 必须限制 allowed_origins，只接受官方扩展 ID。

# 9. 文件身份与移动后的关联

## 9.1 不把路径当身份

path 是 location，不是 identity。文件可以 rename/move，且同一内容可有多个 location。

## 9.2 v0.1/v0.2 策略

```
Fast identity
  path + size + timestamps + optional Windows file id
        ↓
Strong identity
  SHA-256
```

小文件可同步 hash；大文件先写入 fast identity，再由后台/空闲任务计算 SHA-256。阈值应配置化，例如 64–256 MB 之间通过 benchmark 决定。

## 9.3 为什么 v0.1 先用 SHA-256

SHA-256 在 .NET 中无额外依赖、生态兼容性好。BLAKE3 可作为性能优化后加，不应成为 MVP 的依赖风险。

## 9.4 复制语义

相同 hash 不能自动推断“这是同一个逻辑文件”还是“独立副本”。数据库层应区分 ContentIdentity 与 FileInstance，v0.x 可先以内容匹配作为“可能关联”，UI 显示来源为 inherited/likely，而不是绝对 confirmed。

# 10. 数据库设计

建议数据库位置：%LOCALAPPDATA%\WhereFrom\wherefrom.db。用户可通过配置改为便携模式目录。

```
files
- id TEXT/INTEGER PK
- sha256 BLOB NULL
- size INTEGER
- first_seen_utc TEXT
- last_seen_utc TEXT

file_locations
- id
- file_id FK
- path TEXT
- first_seen_utc
- last_seen_utc
- exists_now INTEGER

events
- id
- file_id FK
- event_type TEXT
- event_time_utc TEXT
- agent TEXT NULL
- metadata_json TEXT NULL

sources
- id
- event_id FK
- source_kind TEXT
- url_sanitized TEXT NULL
- referrer_sanitized TEXT NULL
- provider TEXT NULL
- title TEXT NULL
- confidence INTEGER

raw_evidence (optional, off by default)
- id
- event_id
- provider_name
- payload_json
- created_at_utc
```

## 10.1 索引

```
CREATE INDEX ix_locations_path ON file_locations(path);
CREATE INDEX ix_files_sha256 ON files(sha256);
CREATE INDEX ix_events_time ON events(event_time_utc DESC);
CREATE INDEX ix_sources_provider ON sources(provider);
```

URL/domain 搜索量变大后再引入 FTS5；不要第一天过度设计。

## 10.2 Schema migration

数据库必须从第一版就维护 schema_version，并使用单向 migration。CLI 提供 wherefrom doctor 显示版本、迁移状态和数据库路径。

# 11. CLI 规范

## 11.1 命令

```
wherefrom <file>
wherefrom inspect <file> [--json] [--raw]
wherefrom scan <dir> [--recursive] [--executables] [--json]
wherefrom search <query> [--json]
wherefrom open <file>
wherefrom import <dir>
wherefrom doctor
wherefrom config get|set ...
```

## 11.2 退出码

| Code | 含义 |
| --- | --- |

| 0 | 成功，有结果 |

| 1 | 成功执行，但来源未知 |

| 2 | 参数错误 |

| 3 | 文件不存在/不可访问 |

| 4 | 数据库或 Provider 错误 |

| 5 | 内部错误 |



## 11.3 文本输出示例

```
setup.exe

Source
  https://example.com/products/foo

Downloaded
  2026-09-03 18:42
  via Edge

Evidence
  Browser capture + Windows metadata

Confidence
  Confirmed
```

## 11.4 JSON 输出契约

JSON 是公开兼容面。字段新增允许，字段删除/重命名必须跨 major version。必须包含 schemaVersion。

# 12. GUI 信息架构

## 12.1 页面

| 页面 | 职责 |
| --- | --- |

| File Detail | 单文件来源详情；最重要 |

| Recent | 最近捕获/导入文件 |

| Search | 全文/结构化搜索 |

| Audit | 目录来源完整度与 Unknown 列表 |

| Settings | 隐私、hash、大文件阈值、浏览器集成 |

| Diagnostics | 数据库、Native Host、扩展连接、日志 |



## 12.2 File Detail 线框

```
┌─────────────────────────────────────────────┐
│ tool.zip                                    │
│ D:\Downloads\tool.zip                    │
│                                             │
│ Source                                      │
│ github.com/example/tool/releases/tag/v2     │
│ [Open source page] [Copy URL]               │
│                                             │
│ Downloaded                                  │
│ Sep 5, 2026 · Chrome                        │
│                                             │
│ Evidence                                    │
│ ✓ Browser download record                   │
│ ✓ Windows provenance metadata               │
│                                             │
│ Confidence: Confirmed                       │
│                                             │
│ [Show raw metadata]                         │
└─────────────────────────────────────────────┘
```

## 12.3 UX 原则

- 来源信息先人类可读，再显示原始 URL。

- Unknown 不恐吓用户。

- 所有“打开网页”操作显示即将访问的 domain。

- 详情页不默认展示被 sanitizer 删除的 query。

- 原始 metadata 视图明确标注“可能包含隐私信息”。

# 13. 隐私与安全设计

## 13.1 隐私承诺

- No account。

- No telemetry by default。

- No cloud sync。

- 数据库仅存在本机。

- 可一键导出/删除数据库。

## 13.2 威胁模型

| 威胁 | 缓解措施 |
| --- | --- |

| URL 含 token/signature | 入库前 sanitizer；默认丢 query/fragment |

| 恶意文件名/metadata 注入 UI | 所有文本转义；不解释为 markup |

| 恶意网页访问本机 daemon | 不使用 localhost；Native Messaging allowed_origins |

| 扩展伪造记录 | Host 校验来源扩展；记录 evidence provider；不以扩展输入作为安全结论 |

| 数据库泄露浏览历史 | 本地存储、最小化 URL、可选数据库加密后续评估 |

| 点击来源 URL 遭钓鱼 | 显示 domain；使用系统默认浏览器；不自动打开 |

| 损坏/巨型文件造成 hash DoS | 大小阈值、取消 token、后台队列、限并发 |

| 系统 provenance 被删除 | 显示 Evidence unavailable，不伪造来源 |



## 13.3 安全边界

WhereFrom 是 provenance viewer，不是 trust engine。即使来源 URL 为 microsoft.com，也不能证明文件内容未被篡改；只有数字签名/hash 与可信发布信息结合后才可能建立更强结论。v0.x 不做“安全/不安全”评级。

# 14. Provider/Resolver 设计

```
Evidence[]
   │
   ├─ WindowsZoneProvider
   ├─ BrowserCaptureProvider
   ├─ DatabaseHistoryProvider
   └─ FutureProvider...
          │
          ▼
   ProvenanceResolver
          │
          ├─ normalize URL/domain/time
          ├─ detect conflicts
          ├─ confidence scoring
          └─ produce ProvenanceRecord
```

## 14.1 合并规则示例

- Browser finalUrl 与 Windows URL 同 host/path → 增强置信度。

- 时间差在合理窗口内（如 5 分钟）且 size/path 对得上 → 合并同一下载事件。

- 来源 URL 冲突 → 不覆盖旧证据，标记 Conflict 并保留两条。

- 数据库只存 sanitized URL；raw evidence 默认关闭。

# 15. 配置

```
%LOCALAPPDATA%\WhereFrom\config.json

{
  "privacy": {
    "storeQueryString": false,
    "storeIncognitoDownloads": false,
    "rawEvidence": false
  },
  "hashing": {
    "enabled": true,
    "maxSynchronousBytes": 67108864,
    "maxParallel": 1
  },
  "scan": {
    "defaultDirectories": ["%USERPROFILE%\Downloads"]
  }
}
```

配置项必须有版本与默认值；未知字段忽略以支持前向兼容。

# 16. 日志与诊断

- 默认日志级别 Information；URL 日志只输出 sanitized 版本。

- 日志轮换并设置体积上限，例如 5×2MB。

- wherefrom doctor 输出：OS、.NET、数据库 schema、扩展 host 注册状态、读写权限、最近错误。

- 任何诊断导出默认再次做敏感字段清洗。

```
wherefrom doctor --json
wherefrom diagnostics export wherefrom-support.zip
```

# 17. 测试策略

## 17.1 单元测试

- URL sanitizer：token、signed URL、unicode、超长 URL、非法 URI。

- Resolver：一致来源、冲突来源、部分来源、时间窗口。

- File identity：同内容不同路径、同路径内容变化、空文件、大文件。

- Schema migration：从每个历史版本升级到最新。

## 17.2 Windows 集成测试

- NTFS 有/无来源 metadata 的文件。

- exFAT/网络盘/无 ADS 情况。

- 路径含中文、emoji、长路径、UNC。

- 无权限文件、占用文件、符号链接/重解析点。

## 17.3 浏览器集成测试

- 普通 HTTPS 下载、redirect 下载、blob/data 下载、Save As。

- Chrome 与 Edge。

- Native Host 不存在/版本不匹配。

- 无痕模式默认不记录。

## 17.4 性能基准

- 10k 文件 scan 时间。

- 64MB/1GB/10GB hash 吞吐与 UI 响应。

- SQLite 100k/1M provenance records 搜索延迟。

# 18. CI/CD 与发布

## 18.1 GitHub Actions

```
pull_request:
  - dotnet restore
  - dotnet build -c Release
  - dotnet test
  - npm ci && npm test && npm run build (extension)

tag v*:
  - build x64/arm64
  - package
  - generate checksums
  - create GitHub Release
```

## 18.2 发布物

- WhereFrom-Setup-x64.exe 或 MSIX。

- wherefrom-win-x64.zip（便携 CLI）。

- SHA256SUMS.txt。

- Chromium extension 单独发布/商店审核。

## 18.3 代码签名

早期 GitHub Release 可以先无商业证书，但要明确 SmartScreen 体验可能较差。项目形成用户后再引入 Authenticode 签名。不要为了签名阻塞 v0.1。

# 19. 版本路线图

| 版本 | 目标 | 交付条件 |
| --- | --- | --- |

| v0.1 CLI MVP | Windows 本地 provenance 读取 + scan + JSON | 无需后台、无需扩展；能对真实 Downloads 文件给出有意义结果 |

| v0.2 Browser Capture | Chromium 扩展 + Native Messaging + SQLite | 新下载能稳定记录 URL/finalUrl/referrer/time |

| v0.3 GUI | WinUI 3 File Detail + Recent + Search | 非开发者可使用；主流程不需要 terminal |

| v0.4 Explorer | 右键打开 File Detail | 一秒理解的核心产品体验 |

| v0.5 Identity | hash + 移动后关联 + audit | 移动文件后仍尽量找回来源 |

| v1.0 Stability | 稳定 schema/JSON/API、安装/升级可靠 | 可长期维护的 Windows utility |

| v1.x Platform growth | 根据社区需求 macOS provider | Core 不重写；功能不强求完全一致 |



# 20. v0.1 详细任务拆分

## 20.1 Milestone A：仓库与模型

1. 创建 solution 与 Core/Platform.Windows/Storage/Cli/Test 项目。

1. 定义 FileProbe、Evidence、ProvenanceRecord、Confidence。

1. 定义 IProvenanceProvider 与 Resolver。

1. 建立 JSON 契约测试。

## 20.2 Milestone B：Windows Provider

1. 实现文件基础信息读取。

1. 实现来源元数据探测，并覆盖无 ADS/非 NTFS。

1. 建立 15–30 个 fixture 文件/场景。

1. 输出 raw evidence（仅 CLI --raw，默认隐藏敏感值）。

## 20.3 Milestone C：CLI

1. wherefrom <file> / inspect。

1. --json 与稳定退出码。

1. scan <dir>。

1. doctor。

## 20.4 Milestone D：SQLite

1. schema v1 + migration runner。

1. 保存 scan/import 记录。

1. search 命令。

1. 数据库损坏/锁定的错误处理。

## 20.5 v0.1 Definition of Done

- 在 Windows 11 实机对至少 50 个来自 Chrome/Edge/其他方式的下载文件测试。

- 不崩溃；无法识别时明确 Unknown，而不是错误猜测。

- CLI 支持中文/长路径。

- 无网络也完整可用。

- 单元测试/集成测试通过。

- README 有 30 秒 Quick Start 和隐私说明。

# 21. v0.2 浏览器扩展任务

1. Manifest V3 service worker 监听 download changes。

1. 下载完成后通过 downloads.search 获取完整 DownloadItem。

1. Native Host schemaVersion + message size limit。

1. 安装/卸载 host manifest。

1. URL sanitizer 与 incognito 策略。

1. Edge/Chrome 双浏览器实测。

1. 重试：host 暂不可用时最多有限次数，不无限队列。

# 22. v0.3 GUI 任务

1. WinUI 3 App shell。

1. File Detail：通过命令行参数/文件激活打开指定文件。

1. Recent 与 Search。

1. Settings / Privacy。

1. Diagnostics。

1. 键盘可访问性、缩放、深色模式。

# 23. 跨平台设计约束

“跨平台 by design”只意味着领域模型和存储不绑定 Windows；不意味着现在引入 Avalonia/.NET MAUI 或同时维护三端 UI。

```
WhereFrom.Core              net10.0
WhereFrom.Storage           net10.0
WhereFrom.Platform.Windows  net10.0-windows
WhereFrom.Cli               net10.0 / + platform adapters
WhereFrom.App               net10.0-windows
```

## 23.1 未来 macOS

新增 Platform.Mac，读取 macOS 自带的下载来源/隔离元数据，并做 Finder 集成。Core schema 不变，仅 provider/event source 增加。

## 23.2 未来 Linux

Linux 来源数据碎片化，优先 CLI + browser capture；是否做 GUI 根据社区需求决定。不要为了“功能矩阵打勾”实现低质量 provider。

# 24. 主要工程风险与决策

| 风险 | 影响 | 处理 |
| --- | --- | --- |

| Windows 元数据完整度并不一致 | 部分文件只有 zone，没有 URL | 把 evidence/confidence 设计成一等概念；永不猜测 |

| 浏览器扩展权限让用户警惕 | 安装转化下降 | 权限最小化、开源扩展、清晰隐私说明 |

| Signed URL 泄露 token | 严重隐私风险 | 默认删除 query/fragment；测试 sanitizer |

| Shell 集成复杂/崩溃风险 | 影响 Explorer 稳定性 | 延后；先使用低风险调用方式 |

| hash 大文件耗时 | 高 CPU/磁盘 IO | 阈值、后台、取消、限并发 |

| 同 hash 多副本语义不清 | 错误 lineage | 区分 content identity / file instance |

| 跨平台拖慢开发 | 烂尾 | v0.x Windows-only；只保持 Core 边界 |



# 25. 开源仓库建议

## 25.1 README 首屏

```
WhereFrom

Right-click any file and find out where it came from.

✓ Local-first
✓ No account
✓ No telemetry
✓ Windows-first
✓ CLI + Explorer integration
```

## 25.2 License

若希望最大化采用，可优先 MIT 或 Apache-2.0。若希望专利条款更明确，Apache-2.0 更完整。最终选定后全仓库保持一致。

## 25.3 Issue 标签

```
bug
feature
provider
windows
browser-extension
privacy
security
good-first-issue
help-wanted
```

## 25.4 贡献边界

任何新 provider 必须：提供测试 fixture；声明读取什么数据；说明隐私影响；不得静默发网络请求；不得把“来源”升级为“安全结论”。

# 26. 产品指标（不需要遥测）

项目不需要内置 telemetry 才能判断方向。开源阶段主要看外部信号：GitHub issues 中“识别失败”的真实案例、扩展安装反馈、用户主动请求的平台/浏览器、重复出现的来源 provider。

- P0：单文件查询正确率/覆盖率（由测试语料人工评估）。

- P1：浏览器捕获成功率。

- P1：移动后重新关联成功率。

- P2：scan 10k 文件性能。

# 27. 暂定 API 设计示例

```
public sealed record FileProbe(
    string Path,
    long Size,
    DateTimeOffset LastWriteTime,
    byte[]? Sha256);

public sealed record Evidence(
    string Provider,
    EvidenceKind Kind,
    DateTimeOffset? ObservedAt,
    IReadOnlyDictionary<string, string> Values);

public sealed class ProvenanceResolver
{
    public ProvenanceRecord Resolve(
        FileProbe file,
        IEnumerable<Evidence> evidence);
}
```

原则：Evidence 保留 provider-specific 数据，但 Core 输出 ProvenanceRecord 必须稳定、可序列化、可被 CLI 与 GUI 共用。

# 28. 第一周建议开发顺序

1. 创建仓库、solution、CI。

1. 写 Evidence/Resolver 数据模型和测试，不碰 GUI。

1. 做 Windows 来源 metadata 探针的小实验，把真实样本输出 JSON。

1. 实现 wherefrom inspect。

1. 实现 sanitizer 和 --raw。

1. 拿 Downloads 真实文件做人工验证，记录“能识别/不能识别”的类型。

1. 确认 v0.1 的真实覆盖率后，再决定数据库与 scan 的优先级。

第一周的成功标准不是“界面出来了”，而是：对你电脑上真实下载文件，CLI 能稳定回答一部分来源，并且对回答不了的文件不会胡说。

# 29. 决策记录（ADR 摘要）

| ADR | 决策 | 原因 |
| --- | --- | --- |

| ADR-001 | Windows-first | 作者主力环境 + 产品切口清晰 |

| ADR-002 | C#/.NET 10 | Windows 集成与开发效率优先 |

| ADR-003 | WinUI 3 | 新原生 Windows UI 推荐路线 |

| ADR-004 | CLI first | 降低 MVP 风险、利于自动化与测试 |

| ADR-005 | Native Messaging | 避免 localhost daemon 暴露面 |

| ADR-006 | SQLite | local-first、零运维、易迁移 |

| ADR-007 | Sanitized URL only | 降低 signed URL/token 泄露风险 |

| ADR-008 | Read-only provenance | 不主动移除 MotW/系统来源证据 |

| ADR-009 | No telemetry | 隐私即产品价值 |

| ADR-010 | Cross-platform by architecture only | 防止 v0.x scope 爆炸 |



# 30. 官方资料与实现依据

以下资料用于确认本设计中的平台/浏览器能力。实现时应以最新官方文档和实机测试为准。

- **Microsoft — Choose a Windows development path** — 截至 2026-09，微软推荐新原生 Windows 桌面应用使用 WinUI 3 + Windows App SDK。  https://learn.microsoft.com/en-us/windows/apps/get-started/

- **Microsoft — WinUI 3** — WinUI 3 是 Windows App SDK 的原生 UI 框架。  https://learn.microsoft.com/en-us/windows/apps/winui/winui3/

- **Microsoft — Windows App SDK** — Windows App SDK 的桌面开发与部署能力。  https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/

- **Chrome for Developers — chrome.downloads** — DownloadItem 提供 filename、url、finalUrl、referrer、startTime、mime、fileSize 等。  https://developer.chrome.com/docs/extensions/reference/api/downloads

- **Chrome for Developers — Native Messaging** — 扩展与 native host 通过标准输入/输出交换消息；实现时检查 MV3 最新要求。  https://developer.chrome.com/docs/extensions/develop/concepts/native-messaging

# 31. 最终建议

不要先做“大而完整的 File Provenance Platform”。先把 v0.1 做成一个很锋利的 Windows CLI：`wherefrom <file>`。当它能稳定读到真实来源，再逐步加浏览器捕获、数据库、GUI 与 Explorer。

如果 v0.1 发布后用户最强的反馈是“希望右键就能看”，优先 GUI/Explorer；如果反馈是“移动文件后来源就丢了”，优先 identity/hash；如果反馈是“某下载器抓不到”，优先 provider。路线应由真实失败案例驱动，而不是预先填满功能矩阵。

项目长期可以成长为 File Provenance Layer，但第一条承诺永远保持简单：Tell me where this file came from.
