# GUI 启动修复与验证

## 原因

此前 `src/WhereFrom.App/Directory.Build.props` 禁用了 XBF 生成和资源索引，项目文件还关闭了 PRI 工具链。输出有 EXE，但没有应用的 `resources.pri`，不能据此判定 GUI 可运行。局部 props 也没有导入根目录的构建规范。

恢复资源生成，并在 NuGet props 导入前启用 `EnableMsixTooling`，使 .NET MSBuild 使用包内提供的资源工具；`WindowsPackageType=None` 保持非 MSIX 部署。移除跳过 PRI 目标的补丁，不需要重写 UI 或猜测必须换用 Windows 11。

另外将 solution 的 GUI 平台映射改为 x64。GUI 发布脚本使用独立临时目录，检查原生命令退出码及关键资源文件，不再清空其他发布包，也不再从旧 bin 目录取包。

恢复资源后，还复现了旧版 Windows App SDK 1.5 在当前 .NET 10 环境关闭窗口时的 CLR fail-fast（退出码 `0xc0000602`，提示在线程状态销毁后执行托管代码）。升级到 Windows App SDK `1.8.260804001` 后，相同发布目录启动/关闭检查通过，未使用强制返回成功退出码绕过问题。新版应用索引文件名为 `WhereFrom.App.pri`。

## 验证命令

```powershell
dotnet build -c Release -p:Platform=x64
dotnet test -c Release -p:Platform=x64 --no-build
./scripts/publish-gui.ps1
Expand-Archive artifacts/WhereFrom-GUI-0.1.0-win-x64.zip artifacts/gui-verified
./scripts/smoke-test-gui.ps1 -Executable ./artifacts/gui-verified/WhereFrom-GUI-win-x64/WhereFrom.App.exe
```

启动检查等待真实窗口句柄，确认激活后进程仍然存活，再关闭窗口并检查退出码；仅检查 EXE 存在不足以验收。

## 本次记录（2026-09-20）

- .NET SDK 10.0.401，Windows App SDK 1.8.260804001。
- Release x64 解决方案构建：0 警告，0 错误。
- 现有自动化测试：229 通过，0 失败，0 跳过。
- 发布目录程序：实际创建窗口，正常关闭，退出码 0。
- GUI 自包含 ZIP：已生成，包含应用资源索引和 WinUI 运行时。
- 最终 ZIP 解压后运行 `smoke-test-gui.ps1`：窗口启动及关闭通过，退出码 0。
- GitHub 托管运行器上的执行需要提交并推送后确认；本次未推送。
- 此检查覆盖启动和关闭，不代表已完成拖放、选择器、剪贴板及高 DPI 的全部人工验收，也不代表已在干净系统上验证所有运行时依赖。

微软资源索引说明：https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/mrtcore/mrtcore-overview
