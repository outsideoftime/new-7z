# Windows 11 Explorer 右键验收

历史开发版记录（2026-11-04 早先版本）：当时用户已注册 `New7z.ContextMenu`，实际 Windows 11 主右键菜单显示 **New7z**，展开后可选择操作，不需要安装完整 7-Zip。

## 已完成

- `native/New7z.Shell.cpp`：原生 `IExplorerCommand` COM 扩展，菜单构建仅分类路径，不执行耗时引擎操作。
- `shell/AppxManifest.xml`：文件和文件夹的现代菜单注册；空白处不增加菜单。
- `New7z.ShellHost.exe`：转交受控请求给 WinUI 主程序。
- `ShellRequest.cs`：请求目录/GUID/大小/路径校验与动作验证。
- `ShellTaskWindow`：压缩、解压、测试的独立进度/设置窗口；不创建主窗口，只有打开归档走主界面。
- `scripts/register-shell.ps1`：当前用户注册；`-Remove` 移除。注册引用应用的绝对目录，移动目录后须重新注册。
- 发布包含 `New7z.App.pri` 和 `resources.pri`，同时支持直接启动与系统扩展入口。

## 早先版本的实际菜单验证

以下两张 Explorer 菜单截图和直接点击记录来自 2026-11-04 早先版本，验证系统菜单与引擎链路，不代表本轮图标更新后的菜单视觉复验。

| 验证 | 结果 |
|---|---|
| Explorer 右键 7z → New7z → 解压到同名文件夹 | PASS，真实生成中文目录和文本、二进制、图片文件，SHA-256 与原样本一致 |
| Explorer 右键文本 → New7z → 压缩为 ZIP | PASS，真实生成 ZIP，读取包内条目 SHA-256 与源文件一致 |
| 多文件中文及空格路径 → 创建 ZIP → 同名目录解压 | PASS，`scripts/test-shell.ps1`，两文件 SHA-256 一致，请求已消费 |
| 发布版主窗口与延迟启动错误 | PASS，`scripts/test-startup.ps1`，未新增启动错误日志 |
| 7z/ZIP 引擎回归 | PASS，`scripts/test.ps1`，29 项 |
| 全新目录 `dotnet publish` 的资源索引 | PASS，自动包含 `resources.pri`，与应用 PRI 内容一致 |

机器可读结果见 Explorer 实际操作（本地报告：explorer-integration.json） 与 结构化请求测试（本地报告：shell-integration.json）。后者验证请求与真实引擎链路，前者另有实际资源管理器操作，不用脚本直接启动结果代替菜单点击验收。

历史桌面截图仅保存在本地，不随公共仓库发布。

0.1.6 的安装、兼容菜单与文件关联验证见 [安装包发布](安装包发布.md)。

## 启动问题修复

系统扩展入口曾出现 `Cannot locate resource from 'ms-appx:///Microsoft.UI.Xaml/Themes/themeresources.xaml'`，而直接启动正常。额外发布常规名 `resources.pri` 后，实际菜单压缩与解压均成功且错误日志未增长。这与 Microsoft 记录的 [sparse identity 下模块专属 PRI 查找问题](https://github.com/microsoft/WindowsAppSDK/issues/6375) 一致，构建与注册脚本现检查该文件。

当前使用本机已有开发者模式下的开发包注册，未更改安全设置。正式签名安装/卸载、ARM64、跨版本与多 DPI、RAR 样本仍待验收；菜单可见不代表所有格式均已验证。应用内菜单仍保留原型设计范围。

## 本轮图标与独立窗口更新

后续设置更新：New7z 菜单可配置总开关与八项命令，小任务窗口可选成功后自动关闭，默认手动关闭；Windows 自带菜单按用户最终决定保持原样。设置验证与当前实现边界见 [设置与菜单配置](设置与菜单配置.md)，本节以下保留图标/无标题栏阶段记录。

`docs/icon-integration.json`：EXE、Shell DLL、启动器均检测到图标组，ICO 包含 16/24/32/48/64/128/256 px。`docs/shell-integration.json`：真实 ZIP 创建与解压往返校验通过，并按请求定位进程确认窗口标题分别为 `New7z · 压缩文件`、`New7z · 解压文件`。普通主程序启动与 29 项引擎回归通过。HTML 原型回归 31 项、菜单回归 14 项通过，静态审计零错误；design lint 零错误、九项既有 token 引用警告。

界面控制停止前看到了新版独立解压窗口及其标题栏图标；用户随后提供了同版实际截图，并要求去掉系统标题栏。现使用 `OverlappedPresenter.SetBorderAndTitleBar(true,false)` 去除系统标题栏，应用内容头部通过原生窗口移动消息拖动，底部关闭及 Escape 可退出。此方式的边框/标题栏配置依据 [Windows App SDK API](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.windowing.overlappedpresenter.setborderandtitlebar?view=windows-app-sdk-1.8)。用户以 Escape 停止电脑控制后未再调用其他 UI 自动化路径；去标题栏后的新截图、拖动、密码与取消界面流程尚未复验。上述历史截图不作为这部分的当前证据。


