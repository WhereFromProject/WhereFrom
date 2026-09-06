# WhereFrom

WhereFrom 是一个 Windows-first、local-first 的文件来源追踪工具。
当前仅完成 **Milestone 0：工程骨架**，CLI 只输出版本，还不能查询文件来源。

## 开发环境

- Windows，当前验证目标为 Windows x64。
- [.NET 10 SDK](https://learn.microsoft.com/dotnet/core/install/windows)（仅安装运行时不能编译）。

在仓库根目录运行：

```powershell
dotnet restore
dotnet build
dotnet test
dotnet run --project src/WhereFrom.Cli -- --version
```

CLI 预期输出：

```text
WhereFrom 0.1.0
```

也可在构建后直接运行：

```powershell
.\src\WhereFrom.Cli\bin\Debug\net10.0-windows\wherefrom.exe --version
```

本阶段 CLI 只有启动输出，不解析参数；`--version` 与不带参数的输出相同。
首次还原测试依赖需要访问 NuGet；CLI 运行本身不联网。

## 项目结构

| 项目                                     | 本阶段职责                                 | 项目引用         |
| ---------------------------------------- | ------------------------------------------ | ---------------- |
| `src/WhereFrom.Core`                     | 平台无关的类库骨架，暂不定义领域模型       | 无               |
| `src/WhereFrom.Platform.Windows`         | Windows 实现的类库骨架，暂不读取来源元数据 | Core             |
| `src/WhereFrom.Cli`                      | Windows 命令行入口，输出版本               | Core             |
| `tests/WhereFrom.Core.Tests`             | 验证 Core 的目标框架与平台边界             | Core             |
| `tests/WhereFrom.Platform.Windows.Tests` | 验证 Windows 类库的目标平台和测试运行环境  | Platform.Windows |

统一启用 nullable、implicit usings 和 warnings-as-errors。使用 xUnit，测试仅覆盖工程骨架约束，不能证明任何 ADS/MotW 行为。

## 隐私与范围

v0.1 仅面向 Windows，保持 local-first，不加入账号、遥测或云同步。
WhereFrom 对被检查的现有文件和 Mark of the Web 保持只读，绝不删除、修改或解除 MotW。
当前骨架不读取或写入任何被检查文件，也不创建来源数据库。

## 许可证

[MIT](LICENSE)。
