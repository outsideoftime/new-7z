# New7z UX Contract

首轮设计候选，2026-11-03。范围：本地交互原型，不连接压缩引擎。用户任务以 [功能设计](docs/功能设计.md) 为提案来源，已存在能力以 SDK 目录与 `7zr.exe i` 为证据。

范围说明：当前已有真实 WinUI 原生工程，其实现边界见 `docs/工程说明.md`。下表的 JavaScript owner 与模拟任务行为专指 HTML 设计原型；2026-11-04 已另行实现并注册 Windows 11 Explorer 现代菜单，原生 owner 为 `native/New7z.Shell.cpp`、`ShellRequest.cs` 与 `ShellTaskWindow`。

## Product context

简体中文 `zh-CN`；日期按用户本地系统显示，原型固定示例日期为 2026-11-03，时区 Asia/Shanghai。桌面工具语域，文件、扩展名、命令原文保留。可访问性目标 WCAG 2.2 AA；本轮只报告实际自动化与键盘验证，不宣称已通过全部标准认证。

## Business-context sources

| 范围 | 来源 | 性质 |
|---|---|---|
| 产品/平台/图标替换 | 当前用户要求；docs/功能设计.md | 用户 brief / 候选设计 |
| 格式能力 | 7zSdk/DOC/lzma-sdk.txt；7zr.exe i | 本地技术证据 |
| 覆盖/取消/输出安全 | docs/功能设计.md §5 | 待产品评审的流程提案，原型不产生真实写入 |
| 权限 | 本地操作系统权限；本轮不访问注册表 | 系统约束 |
| 账户、计费、云端权限 | 不适用 | 本地工具，无外部服务 |
| 隐私 | docs/功能设计.md §5–7 | 本地默认与密码不持久化提案 |

## Visual contract

视觉依据 `DESIGN.md`。浅色规范映射到 `prototype/tokens.css`；深色补充 token 由该 CSS 独占定义。共享样式在 `styles.css`，组件行为在 `app.js`。主题系统/浅/深；原生高对比遵守系统色。

## Canonical UI Map

| Capability | Canonical owner | Source of truth | Allowed variants | Verification |
|---|---|---|---|---|
| Table Selection | app.js archiveTable / selection Set | 本文件 | 当前可见目录 | browser-check.cjs |
| Select/Listbox | native select | DESIGN.md | native，接受操作系统弹层 | browser-check.cjs |
| Form | app.js showDialog / validateName | 本文件 | 压缩 / 解压 | browser-check.cjs |
| Scrollbar | styles.css 全局规则 | DESIGN.md / tokens.css | table 横向、dialog 纵向 | 浏览器检查 |
| Toast | app.js notify | 本文件 | info / success / error | live region 检查 |
| CRUD | app.js render / addTask / icon input | 本文件 | 内存创建任务、取消确认、替换会话图标 | browser-check.cjs |
| Context Menu | context-menu.js / context-menu-app.js | docs/右键菜单设计.md | 应用内条目/多选/空白原型；Shell 场景设计 | context-menu-check.cjs |
| Explorer Context Menu | native/New7z.Shell.cpp / ShellRequest.cs | docs/右键菜单设计.md | 系统 New7z 子菜单；归档/普通文件/文件夹/多选 | test-shell.ps1 / docs/Explorer右键验收.md |
| Shell Task | ShellTaskWindow / ArchiveEngine | 本文件 | 独立快捷任务窗口；原生输入/下拉/密码/进度/结果 | test-shell.ps1 / 原生窗口检查 |
| Windows Icon | WindowAppearance / build-icons.ps1 | DESIGN.md | 主程序/小窗口/EXE/Explorer 菜单 | test-icons.ps1 |
| Explorer Settings | MainWindow.Settings / ExplorerPreferencesStore | 本文件 | ToggleSwitch 总开关/CheckBox 命令/ComboBox 关闭方式 | test-shell-preferences.ps1 / test-popup-close.ps1 / Core integration |
| Windows Compression Menu Setting | MainWindow.Settings / WindowsCompressMenuSettingsStore | 本文件 | 当前用户、Windows 11 24H2+「压缩到」菜单开关；保存前备份、恢复原注册表值 | test.ps1 隔离注册表键 15 项 + 安装脚本恢复 7 项；真实 Explorer 菜单待验收 |
| Windows Integration | MainWindow.Integration / installer/Integrate-Shell.ps1 | docs/安装包发布.md | 注册修复按钮；Windows 默认应用选择器由系统拥有 | test-installer.ps1 / published UI diagnostic |
| Archive Editing | MainWindow.ShowArchive / ArchiveEngine.ModifyAsync | 本文件 | 仅 7z/ZIP 添加、删除与同名替换；临时文件成功后替换原包 | tests/New7z.Integration |
| Drag Extraction | MainWindow.FileListDragItemsStarting / ArchiveEngine.CreateDragData / native DragDataObject | 本文件 | WinUI 负责手势、选择与鼠标反馈；IDataObjectProvider 接入原生延迟数据；按住鼠标悬停不解压，释放后准备真实 CF_HDROP 条目，由系统复制/移动临时文件；准备阶段显示可取消进度 | test-drag-extraction.ps1 / --verify-drag-ui / 真实鼠标验证 / 临时目录清理 |
| App Update | MainWindow.Updates / GitHubUpdateChecker | docs/安装包发布.md | 启动自动检查、手动检查、SHA-256 校验、确认后安装 | 真实 GitHub API 探针 / 发布版 `--verify-ui` |

Date 不适用：只读示例日期，无日期输入控件。引擎 API、登录、远程请求、真实磁盘修改本轮不适用。

## Component behavior

原生设置页的菜单修复和文件关联注册复用 Button、Card 与 InfoBar，处理中禁用触发按钮，完成后恢复；错误就地显示且可重试。文件关联注册只加入 Capabilities、RegisteredApplications 和 OpenWithProgids，不修改 UserChoice；「选择默认应用」打开系统拥有的 Windows 设置页面，由用户确认格式。两项注册动作通过同一个系统集成脚本执行。

按钮原生语义，hover/focus/pressed/disabled 统一；输入均有 label。select 使用原生控件，不改变弹出层归属。密码默认隐藏，切换 aria-label；不写 URL/日志/持久存储。搜索即时本地筛选，输入法组合期间暂停，非空有清除按钮。表单 novalidate，自有校验、第一错误聚焦；字段错误以 aria-invalid / aria-describedby 关联。

## Dataset navigation

原型仅有限示例条目全量展示，不暗示大规模性能。正式大归档用虚拟化/增量枚举，状态计数真实。筛选/进入目录时清空选择，排序保留按 ID 选择。搜索、选择、路径为当前会话内存，不写 URL，以免泄露文件名；导航回来保留当前包状态。最近记录清空仅影响示例历史。

## Flow ledger

| 操作 | 触发 | 处理中 | 成功去向 | 反馈 | 失败恢复 | 焦点 | 来源 |
|---|---|---|---|---|---|---|---|
| 打开 | 主页打开/最近项 | 原型直接进入示例 | 压缩包 | 显示名称与根路径 | 演示失败可重试 | 页面标题 | 功能设计 §5.1 |
| 新建 | 新建压缩 | 提交去重 | 任务中心 | 模拟任务行 | 保留错误字段 | 第一错误/任务标题 | §5.2 |
| 解压 | 解压按钮 | 提交去重 | 任务中心 | 模拟进度 | 保留配置 | 同上 | §5.3 |
| 取消任务 | 任务取消 | 模态确认 | 任务中心 | 已取消 | 返回保留运行 | 任务按钮 | §5.4 |
| 替换图标 | 图片选择器 | 解码校验 | 保持设置 | 会话内应用 | 显示原因，保留旧图 | 导入按钮 | §6 |
| 恢复默认 | 恢复按钮 | 即时、可再次导入 | 保持设置 | 默认图标 | 不适用 | 原按钮 | §6 |
| 清空最近 | 清空按钮 | 确认 | 保持主页 | 空列表 | 返回保留 | 打开入口 | §5.1 |
| 预览 | 文件名 / 单选预览 / 属性入口 | 有界加载，取消旧结果 | 只读共享模态 | 图片/文本与元数据 | 原位重试/解压单个文件 | 关闭回到触发器 | §5.1a / 用户补充要求 |

## Navigation and responsive behavior

四个导航保留，更新 document.title；主内容标题接收程序化焦点。980 px 收起导航标签，640 px 转顶部横排。表格内部横向滚动，属性下移，弹窗内部纵向滚动，不把设置长表单裁切。长文件名可换行，路径有完整可读值。

## Overlays and feedback

统一 `<dialog>` / showModal；背景隔离由浏览器维护，Tab 限制、Escape 关闭与焦点恢复。严重操作初始焦点落在「返回」。取消任务、清空最近确认写清对象与后果；原型不模拟未授权真实删除。压缩/解压表单取消会保留本次草稿，再次打开仍可编辑；草稿仅内存，刷新清除，密码不进入草稿。主题自动应用，无待保存设置。

图片/文本预览由 `preview.js openPreview` 拥有，复用 showDialog；大小/编码为 native select，接受平台弹层。图片与文本限当前可见目录切换，保留原表格筛选排序与选择。读取通过 sequence 标记丢弃过期结果，关闭也使旧读取失效。预览采用固定内容区，加载/错误/成功不推动底部操作。用户文本通过 esc 后显示，查找用字面量匹配，不当 HTML 执行。文字查找有组合输入保护与清空；清空恢复输入焦点。大小限制与真实临时缓存的产品规则引用功能设计 §5.1a。

共享状态提示 aria-live polite、稳定右下、不遮主要操作。必须处理的问题保留为页面/字段错误。modal z=100，toast z=110；native top layer 仍由浏览器管理。

原型右键菜单使用 native popover 非模态 top layer 和共享键盘行为；命令执行前关闭。单项右键更新选择，多选内右键保留集合，空白清空；排序和复制按当前可见行顺序。Shift+F10、菜单键和可见「更多」是等效入口。Escape 恢复触发控件，外部点击关闭不抢焦点；滚动/resize 关闭。只接管归档列表，文本输入仍保留浏览器文字菜单。HTML 场景页不执行磁盘或注册表操作；实际 Explorer 扩展由系统绘制菜单，并通过受控 JSON 请求触发真实任务，默认冲突保留两者。

## Async and resilience

原生 Explorer 任务变体：启动分流在任何主窗口创建之前完成。设置→执行→完成/失败/取消由同一无系统标题栏小窗口承载，默认手动关闭，提供打开文件夹与关闭；Escape 与底部关闭等效。配置窗口按表单内容调整高度，底部操作栏保持固定；内容超出工作区时内部滚动。可在设置中选择成功后自动关闭，任务完成时读取当前策略；失败、取消、设置读取异常时保留结果。执行中要求确认取消，等待回调停止后才允许退出；错误保留原因，重试快捷打包重新选择唯一输出名，配置任务返回原输入表单。批量解压按包串行，分配输出名时预留目录名避免同批重名，密码分别询问且不缓存到其他包。小窗口使用原生 ComboBox、PasswordBox、ContentDialog、InfoBar，与主程序保持主题与冲突默认值一致。

设置中的菜单总开关与命令勾选为表单草稿，通过「应用菜单设置」一起保存，菜单开启时至少勾选一项。总开关关闭时保留命令选择，重新开启可沿用；没有当前文件类型适用命令时隐藏空的 New7z 根菜单。原生 DLL 每次读取配置，不需重载 DLL；部分 Explorer 窗口缓存仍可能需要重新打开。当前用户设置保存在 `HKCU\Software\New7z\Explorer`。弹窗关闭方式与 Windows 11 24H2+「压缩到」菜单开关选择即保存，失败回退并说明原因。隐藏系统菜单时只屏蔽当前用户的 Windows 压缩命令 CLSID，备份并恢复其原有注册表值；重启 Explorer 后仍会生效。默认菜单开启、全部命令、手动关闭，不隐藏 Windows 菜单。

本轮 addTask 每次触发创建一个内存任务，模拟 0–100% 明确标识。切换页面不清定时器，重复提交通过关闭弹窗与提交锁限制。暂停只暂停模拟计时；不表达真实引擎能力。错误示例可重试，取消需确认。实际引擎准备/运行/输入/结束协议见功能设计；无服务器、网络请求、会话过期或远程写入。

## Validation

输出名不能为空，拒绝 Windows 保留字符、保留设备名、尾部点/空格；目标目录非空。密码不保存在配置或历史。图标仅 PNG/ICO（≤10 MB），最大解码尺寸 4096；错误就地关联，当前有效图标不变。URL.createObjectURL 仅用于受控图片解码，不执行内容，替换后释放旧 URL。

## Permission and clipboard

原型不读写任意路径、不注册关联；目录输入只是示例文本。文件图标选择由用户显式选择；不会上传。不提供未实现的剪贴板或系统操作按钮。

## Verification

静态：node --check；designmd lint；premium audit strict。浏览器：主页→浏览→目录/搜索/选择→压缩校验→模拟任务→取消/重试；解压冲突；浅深主题；图标有效/无效导入与恢复；模态 Escape/Tab；窄视口；减少动态效果。实际证据写入 docs/验证记录.md，原生材质、引擎与 Windows 辅助功能需正式实现后验收。
