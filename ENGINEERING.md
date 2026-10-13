# WhereFrom 工程设计

> 面向贡献者的已实现架构、证据规则与演进边界。v0.1.1 仅包含 CLI；v0.2.0 增加 GUI 和浏览器捕获。

[中文使用说明](README.zh-CN.md) · [English README](README.md) · [文档索引](docs/README.md)

## 1. 产品边界

WhereFrom 回答“这个文件有哪些可查的来源线索？”Windows 自带的 `Zone.Identifier` 和浏览器扩展保存的新下载记录是两条独立证据。来源网址不是安全评级；没有来源不意味着文件危险，两个证据不一致时也不擅自选择可信方。

产品优先支持 Windows x64、本地运行、无需账号、无遥测或云同步。查询不修改被检查文件，不解除 Mark of the Web。没有常驻后台服务；浏览器在需要传递下载事件时启动 Native Messaging Host。未来功能以真实案例、明确验收和维护成本决定，不因本文件提及而自动进入当前范围。

本文件描述设计意图。**实际命令、JSON 字段、数据库 schema 和打包内容以代码为准。** 使用方式见 README，正式发布检查见[发布验证清单](docs/release-validation.md)。

## 2. 已实现的系统

```text
Chrome / Edge MV3 extension
        │ Native Messaging
        ▼
WhereFrom.NativeHost ──► WhereFrom.Storage (SQLite)
                               ▲
WhereFrom.Cli ──┐               │
WhereFrom.App ──┼──► UnifiedProvenanceQuery
               │       ├── WhereFrom.Platform.Windows (Zone.Identifier)
               │       └── WhereFrom.Storage (browser records)
               └──► WhereFrom.Core (portable evidence models)
```

| 项目 | 职责与边界 |
| --- | --- |
| `WhereFrom.Core` | 平台无关的来源结果、浏览器证据和 Provider 接口；不引用 Windows API、注册表或 UI 类型 |
| `WhereFrom.Platform.Windows` | 只读读取与解析 Windows ADS、文件系统能力检查、统一查询和浏览器启动策略 |
| `WhereFrom.Storage` | 清洗后浏览器记录的 SQLite 存储、schema 升级和有上限的文件哈希 |
| `WhereFrom.NativeHost` | 验证扩展 ID、消息版本与长度后保存已完成的新下载；不常驻 |
| `WhereFrom.Cli` | 稳定的文本、JSON、退出码与诊断命令；不复制证据合并逻辑 |
| `WhereFrom.App` | WPF 单文件窗口，复用统一查询；选择、拖入、复制及主动打开网址 |
| `extension/` | Chrome／Edge Manifest V3 扩展；只发送完成的普通下载，有限次数重试 |

`WhereFrom.Core` 必须能在不加载 Windows UI 的测试环境中运行。Windows 平台类型不得泄漏进它的公共模型。GUI 和 CLI 都调用统一查询，不各自发明来源归属规则。

## 3. 证据与数据流

### Windows 证据

Windows 层只读打开 `Zone.Identifier`，最多读取 64 KiB。解析 `HostUrl`、`ReferrerUrl`、`ZoneId` 等已知字段；损坏、重复或不完整字段产生问题记录，同时保留其他可用字段。若数据流不存在，还要检查文件系统对命名数据流的支持；能力查询失败不能被写成“文件没有来源”。原始 Windows URL 保持原样，可能含敏感参数。

### 浏览器证据

扩展仅处理 Chrome、Edge 此后完成的新下载；失败、取消和无痕下载默认不入库，不导入旧历史。Host 校验允许的扩展 ID、消息版本和大小，不保存请求头、Cookie 或原始消息。浏览器 URL 入库前移除查询参数和片段。数据库默认位于 `%LOCALAPPDATA%\WhereFrom\wherefrom.db`；`wherefrom data path` 可查看位置，`wherefrom data clear --yes` 可显式清除浏览器记录。卸载程序不删除用户数据库。

当前数据库 schema 为 v2。v1 记录仍可查询；普通查询不触发迁移，下一次成功保存新下载时使用事务升级。既有行不会凭空获得 SHA-256。损坏或不可用的数据库要明确报错，不能把读取失败误判为“没有来源”。

### 文件身份与可能关联

新下载文件不超过 64 MiB 时，保存其 SHA-256；超过上限保留元数据记录，不同步扫描整份大文件。查询同一路径时，路径、大小、修改时间和 SHA-256 均匹配的记录可作为直接浏览器证据，但相同内容仍可能是复制件。旧版无哈希记录，以及在不同路径找到的同内容记录，均只标为**可能关联**。可能网址可在结构化证据中查看，不提升为顶层确定来源，也不供 `open` 或 GUI 自动选择。

同一路径被不同内容替换时，旧浏览器记录列为历史记录，不归给当前文件。Windows 和浏览器网址冲突时并列展示。SHA-256 相同不能证明文件实例相同；当前实现没有文件系统监听、通用 lineage 图或确定性的移动追踪。

## 4. CLI 与 GUI 契约

```text
wherefrom <file> [--json]
wherefrom scan <directory>
wherefrom audit <directory> [--limit 1..1000]
wherefrom open <file>
wherefrom debug-zone <file>
wherefrom data path
wherefrom data clear --yes
```

单文件 JSON 保持 `schemaVersion: 1`、`path`、`hasProvenance`、`zone`、`sourceUrl`、`referrerUrl` 的既有语义；新增的完整度、冲突和证据字段为兼容性扩展。缺失网址为 `null`。正常查询即使未知也返回 JSON，参数与读取错误不输出 JSON。标准错误不混入 JSON。退出码的详细边界由命令测试固定，概览见 README。

`scan` 保留原有的 Windows 元数据行为；`audit` 用统一查询，但只看目录第一层，默认最多检查 100 个文件，最大 1000，并跳过重解析点。GUI 只处理一个文件，展示不同证据来源及不确定性。仅用户主动点击时才打开通过 HTTP/HTTPS 校验的来源页面。

## 5. 隐私与可靠性

- 文件查询只读，不删除、覆盖或解除 Mark of the Web；本机查询无需网络。
- URL、文件路径与 ADS 文本均视为不可信输入。终端输出转义控制字符；分享报告前须脱敏。
- Windows 原始 URL 可能带 token，浏览器 URL 默认清除 query 与 fragment；不保存无痕下载、请求头或 Cookie。
- 本地数据库可由用户明确清除；卸载保留用户数据。无账号、遥测、云同步或常驻服务。
- 证据不等于安全判断；Unknown 与读取失败须区别呈现，不能猜测缺失字段。
- 哈希读取有 64 MiB 上限，并检查读取期间大小和修改时间是否变化；一致内容仍只能支持有限的归属推断。

## 6. 构建、打包与验证

仓库使用 .NET 10；GUI 为 WPF，浏览器扩展为 Manifest V3 JavaScript，存储使用 `Microsoft.Data.Sqlite`。Windows x64 发布物包含自带运行时的 CLI、GUI、Native Host、扩展和用户说明；安装器由 Inno Setup 6 构建。

```powershell
dotnet restore WhereFrom.sln
dotnet test WhereFrom.sln -c Release -p:Platform=x64
node --test extension/test/worker.test.js
./scripts/publish.ps1
./scripts/check-repository-hygiene.ps1 -Package artifacts/WhereFrom-0.2.0-win-x64.zip
./scripts/build-installer.ps1
```

[Windows 工作流](.github/workflows/windows.yml)执行构建、测试、打包、发布包启动检查及隔离安装/升级测试；它上传工件，但不创建 GitHub Release。`scripts/check-repository-hygiene.ps1` 拒绝把本机数据库、构建产物、阶段日志和一次性调试脚本纳入 Git 或发布 ZIP。发布前仍须按[清单](docs/release-validation.md)在干净 Windows 环境检查实际安装、Edge 与 Chrome 下载、浏览器重启、卸载和升级。

当前已实机验证 Edge 新下载、浏览器重启后的再次捕获、GUI 交互、资源管理器入口，以及复制件的“可能关联”显示。Chrome 在开发机器上不可用，干净 Windows 环境的完整验收仍待完成；不能将自动化测试当作这两项实机验收。

## 7. 维护规则与后续方向

- 优先修复可复现的缺陷，不为可能出现的渠道预设插件框架、事件总线或通用依赖注入层。
- 保持 Core 平台无关；Windows API 放在 Windows 项目。新增证据源复用统一查询，并明确何时采集、保存哪些字段及其隐私影响。
- 新命令或 JSON 字段要同步更新中英文 README、自动化测试和发布检查。改变现有字段含义或退出码须单独评估兼容性。
- 用临时 fixture 验证读取与冲突边界，再做必要的真实 Windows 场景验收；不要把用户私人路径、URL、浏览器配置或阶段过程日志提交到交付分支。
- 后续可按真实需求改进移动关联、最近记录与搜索、安装体验和更多平台；这些方向不是已承诺的版本或日期。Brave、旧历史导入、云同步和 AI 分类不属于 v0.2.0。

## 参考入口

- [中文使用说明](README.zh-CN.md) / [English README](README.md)
- [文档索引](docs/README.md)
- [发布验证清单](docs/release-validation.md)
- [浏览器安装说明](docs/browser-setup.txt)
- [MIT 许可证](LICENSE)
