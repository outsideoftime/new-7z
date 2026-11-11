---
version: alpha
name: New7z
description: Windows 11 Fluent 本地归档工具，使用熟悉的文件工作区与蓝色拉链品牌。
colors:
  primary: "#0067c0"
  primary-hover: "#005a9e"
  background: "#edf1f7"
  surface: "#ffffff"
  layer: "#f8f9fc"
  text: "#1b1b1f"
  muted: "#5c6470"
  border: "#dce1e8"
  selected: "#e5f0fb"
  success: "#107c41"
  danger: "#b42318"
  warning: "#8a5200"
typography:
  sans:
    fontFamily: '"Segoe UI Variable", "Segoe UI", "Microsoft YaHei UI", sans-serif'
    fontSize: "14px"
    lineHeight: "1.5"
  mono:
    fontFamily: 'Consolas, "Cascadia Mono", monospace'
rounded:
  DEFAULT: "4px"
  card: "8px"
  window: "12px"
spacing:
  unit: "4px"
  page: "32px"
  section: "24px"
components:
  button:
    rounded: "4px"
    backgroundColor: "{colors.primary}"
  card:
    rounded: "8px"
    backgroundColor: "{colors.surface}"
  dialog:
    rounded: "12px"
    backgroundColor: "{colors.surface}"
---

# New7z Design System

2026-11-04 原生系统集成：Windows 11 Explorer 采用系统绘制的 New7z 子菜单，随文件、文件夹、归档选择切换命令；不使用 HTML 浮层替代系统菜单。快捷压缩和解压进入现有任务界面，错误和密码交互由主程序处理。当前用户开发注册与真实操作证据见 `docs/Explorer右键验收.md`；应用内右键的 HTML 原型与原生 Explorer 扩展分别维护实现范围。

## Overview

设计北极星：Windows 11 文件资源管理器的工作密度，结合当前蓝色拉链 Logo 的归档语义。用户通过目录和文件列表操作，品牌表达集中在归档属性中的体积对照条。产品工具语域，简体中文，Windows 桌面优先；本轮原型不包含日本市场或日语范围。

这是用户委托的首轮候选设计，尚未经用户评审。项目此前仅有 SDK 与图标，无既有 UI、业务文档或共享组件；依据见 [功能设计](docs/功能设计.md)。不引入营销大标题、统计看板或全屏毛玻璃。

当前工程已增加 WinUI 3 原生开发版，真实能力与差异见 [工程说明](docs/工程说明.md)。本文原型 token 规范继续用于设计评审；原生界面通过 Windows ThemeResource 适配系统主题和强调色，尚未完成逐组件对齐。Explorer 菜单已原生实现，HTML 场景页继续保留为设计参考。

Token ownership：本文件 frontmatter 的浅色命名值是规范；`prototype/tokens.css` 用 `--color-<key>` 映射，深色补充在同文件 `[data-theme="dark"]` 中，运行时统一由 `prototype/styles.css` 消费。主题补充值由 `tokens.css` 独占定义，不在页面内另建调色板。HTML 不内嵌视觉值。原生 WinUI 阶段将通过 ResourceDictionary 适配同一语义。

## Colors

蓝用于主操作、选中标记与焦点。白色连续内容层置于偏冷的底材之上。正文 text、辅助 muted，描边 border，选择区域 selected。成功/错误/注意使用图标与文本共同传意，不能仅靠色彩。浅深主题维持相同语义；高对比使用系统颜色覆盖，系统透明关闭时退化为实色。

## Typography

Segoe UI Variable 优先，中文使用 Microsoft YaHei UI 回退。标题 28/600，区块 18/600，正文与控件 14，辅助 12；中文不刻意加字距。数据使用 tabular-nums；路径与扩展名保留原文，不为了紧凑裁切掉必要操作信息。

## Layout

Explorer 任务采用独立 `ShellTaskWindow`，任务窗口目标大小 600 × 380 DIP；配置窗口按表单内容调整高度，并受当前显示器工作区约束，内容较长时可滚动。按用户要求不显示系统标题栏和标题栏按钮，图标/任务标题区域用于拖动，底部「关闭」和 Escape 负责退出；任务执行期间先确认取消并等待安全结束。保留原生边框，内容与错误共用内部滚动，底部按钮固定。压缩、解压与测试不实例化主窗口；只有「用 New7z 打开」进入归档浏览器。这是用户指定的系统快捷任务变体，与主程序任务中心区分入口。

4 px 节奏：页面边距 32，区块间距 24，导航宽 208。窗口外边距 28，设计评审画布 1440 × 960。列表行高 52，主内容与可收起的 248 px 属性栏并列。980 px 以下收起导航文字、属性转下方；640 px 以下导航移为横排且表格局部横向滚动。原生产品最小窗口目标 900 × 620 DIP，与浏览器重排检查分开。

## Elevation & Depth

标题栏/导航模拟 Mica，内容以连续浅色层承托。只在应用窗口与模态浮层使用明显阴影；静态列表不用漂浮卡片。Acrylic 保留给短暂菜单；Smoke 遮罩表示阻塞。CSS 是视觉近似，不是真正 DWM 材质。

## Shapes

控件圆角 DEFAULT、卡片 card、主窗与弹窗 window；分别通过 `--radius-control/card/window` 映射。按钮为轻微圆角矩形；状态小标签可用小胶囊。边界细、间距稳定，不把所有行套入独立大圆角卡片。

## Components

系统集成设置沿用原生卡片、按钮与 InfoBar。菜单修复与关联注册均在原位反馈，按钮处理中保持原尺寸并禁用。默认应用的格式选择交给 Windows 设置；应用不绘制替代的系统选择器。

安装向导是独立的系统流程：复用 Inno Setup 的原生现代 Windows 11 控件与简体中文消息，主题跟随系统，保持目录选择→可选任务→安装→完成的顺序。只把安装位置、菜单和快捷方式作为用户决策，不在向导中暴露构建参数。现代菜单条件及兼容回退放在安装前说明中；不会代用户启用开发者模式或信任证书。用户数据保留、卸载与覆盖安装属于同一安装器责任边界，来源见 `docs/安装包发布.md`。

原生系统图标来自 `图标/logo.png`，由 `scripts/build-icons.ps1` 转为七种尺寸的 `Assets/New7z.ico`；App EXE、Shell DLL、启动器嵌入同一资源，窗口通过共享 `WindowAppearance` 设置。`ShellTaskWindow` 的进度、输入、密码、InfoBar 和 ContentDialog 使用 WinUI 原生组件与 ThemeResource，同主程序保持语义与主题一致，不增加独立调色板。小窗口默认保留完成结果与「打开文件夹」，也可在设置中选择成功后自动关闭；错误与取消始终保留结果供查看，取消需确认且等待引擎安全退出。

原生设置页的系统集成区由 `MainWindow.Settings.cs` 维护：右键菜单总开关、九项命令勾选共用一个「应用菜单设置」提交；未修改时按钮禁用，保存失败保留草稿并显示 InfoBar。设置页采用分类入口（常规、压缩、右键菜单、外观与图标、文件关联、关于），进入分类后顶部返回，分类内只保留该领域的卡片。弹窗关闭策略和 Windows 11 24H2+「压缩到」菜单开关均即时保存，失败回退已保存值并显示 InfoBar。Windows 菜单开关只修改当前用户的 Shell 扩展屏蔽项，保存前备份原始值，关闭开关或卸载时按原值恢复；不触碰其他压缩菜单命令。应用需通知资源管理器刷新，并告知用户必要时重启资源管理器。

共享原型模块位于 `prototype/app.js`：`render` 导航；`archiveTable` 表格；`showDialog` 模态；`notify` 提示；`addTask` 任务状态机；`setTheme` 主题适配。控件统一用按钮、输入、标签和原生 select，不自造列表框。

`prototype/preview.js` 复用 `showDialog` 提供图片与纯文本只读预览，窗口最大宽度 1120 px；图像画布使用 background 层，文本使用 surface 层与 mono 字体。行号用 muted，查找高亮使用 selected + primary 描边。顶部位置/切换/缩放与底部关闭保持稳定，内容自身滚动；首尾切换禁用，不跨越当前目录。样式位于 styles.css，继续消费既有语义 token，不引入独立配色。

`prototype/context-menu.js` 是菜单定义与浮层的共享 owner，`context-menu-app.js` 连接原型选择与已有操作，场景图在 `context-menu-design.html`。短暂菜单使用 surface、border、hover、muted、既有浮层阴影，默认宽 284 px、行高至少 38 px，具备浅深/高对比降级。原生交付改用 WinUI flyout，不照搬 HTML 层。菜单行为及 Shell 作用域见 [右键菜单设计](docs/右键菜单设计.md)。

按钮 hover 加深/背景、active 减弱、focus-visible 双像素强调轮廓；disabled 不响应。一个任务区域一个主按钮。输入在焦点时强调边框，错误文字直接关联字段。密码掩码与显示切换统一使用可访问名称。加载使用文字和不定进度，不画假骨架。空/无结果有下一步，错误保留上下文。

搜索清除按钮与输入为同一组件；中文组合输入期间不筛选。模态使用原生 `<dialog>` 的焦点隔离和 Escape，关闭后恢复触发器焦点。表格是语义 table，排序按钮配 aria-sort，选择用 checkbox，不把只读数据伪装成 spreadsheet grid。

工具图标是内联 SVG 线稿 18–20 px；文件图标是代码原生 SVG 轮廓，格式文本仍可辨识。提供的 Logo 使用去背景版本；格式图标合集已导出为 `Assets/Formats` 下的透明 PNG/ICO，供文件关联与格式展示使用。图标替换按逻辑 ID，缺失可回退，具体范围见功能设计 §6。

动效 120–180 ms，任务进度演示单独标明；reduced-motion 关闭过渡。共享通知在稳定位置显示，live region 宣告；错误不依赖通知存活时间。字节单位示例采用 MB/GB，正式实现应在设置中统一十进制或二进制并准确标注。

全局滚动条使用 `--scroll-thumb/track/hover/active`，标准属性与 WebKit 回退均实现；新滚动区自动继承。强制颜色模式回到系统色。长表格拥有自己的横向滚动，长表单拥有模态内部滚动。

## Do's and Don'ts

- 保持命令、路径、列表、属性四层清晰；同一操作跨页面保持同名与同结果。
- 真实能力才可用，模拟与后续规划都显式说明。
- 使用现有品牌素材，同时保留清晰文字和回退图标。
- 不将格式图标等同引擎支持，不承诺实时修改已签名程序图标。
- 不自动覆盖或删除源文件，不用持续装饰动画吸引注意。
