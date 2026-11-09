# Microsoft Store 上架说明

Store 版本以 MSIX 形式提交，构建入口是 `scripts/build-msix.ps1`，与 GitHub / Inno 版本共用同一套源码和打包脚本。

## 1. 在 Partner Center 准备

1. 注册个人开发者账号（免费，需证件验证）。
2. 新建产品时选择 **MSIX or PWA app**，预留应用名称。
3. 打开该产品的 **Product identity** 页面，复制以下三项：

| Partner Center 字段 | 当前值 | 写入位置 |
|---|---|---|
| Package/Identity/Name | `B8EC7EE8.New7z` | `Package.appxmanifest` 的 `<Identity Name="...">` |
| Package/Identity/Publisher | `CN=7C74DB3A-AD98-46DA-A414-D5D981956821` | `<Identity Publisher="...">` |
| Package/Properties/PublisherDisplayName | `王国梁` | `<Properties><PublisherDisplayName>` |

包族名（PFN）：`B8EC7EE8.New7z_2n9mjq0a4yk0`；Store ID：`9N9T8LX28P3V`。

`Version` 不需要手改：构建脚本会从 `Directory.Build.props` 读取并写入清单，构建结束后还原。

## 2. 构建上传包

```powershell
pwsh -File scripts/build-msix.ps1 -RequireStoreIdentity
```

`-RequireStoreIdentity` 会在 Publisher 仍是占位 GUID 时直接报错，避免上传一个必然被拒的包。

产物：

- `artifacts/msix/New7z.App_<版本>_x64.msixupload` —— 提交到 Partner Center 的文件
- `artifacts/msix/New7z.App_<版本>_x64_Test/*.msix` —— 本地测试用的包（未签名）

## 3. 提交认证

在 Partner Center 的提交页面填写：

- **包**：上传 `.msixupload`
- **商店列表**：截图（按提交时的实际界面自行准备）、描述、分类（实用程序）
- **系统要求**：Windows 11 x64（包清单里 MinVersion 已限定 10.0.22000）
- **年龄分级**、**隐私政策**：使用仓库中的 [PRIVACY.md](../PRIVACY.md)，页面地址为 `https://github.com/outsideoftime/new-7z/blob/main/PRIVACY.md`；也可以在提交表单中选择「提供隐私策略文本」直接粘贴正文
- **定价**：免费

认证报告曾指出产品描述前两行未披露 `.NET Desktop Runtime`。每次提交时请把以下说明放在商店描述开头（即使运行时已随 MSIX 自包含，也要披露组件）：

> New7z includes the .NET Desktop Runtime; no separate runtime installation is required.

认证一般 1～3 个工作日。通过后微软会重新签名，无需自备代码签名证书。

## 4. 发版流程

1. 修改 `Directory.Build.props` 中的 `<Version>`
2. 运行 `scripts/build-msix.ps1`
3. 在 Partner Center 新建提交并上传新的 `.msixupload`

## Store 版与 GitHub 版的差异

| 项目 | GitHub（Inno 安装包） | Microsoft Store（MSIX） |
|---|---|---|
| 更新方式 | 应用内从 GitHub Releases 自更新 | 由 Store 自动更新，应用内不检查 |
| 文件关联 | `Integrate-Shell.ps1` 写注册表 | 包清单声明，自动出现在「打开方式」 |
| 资源管理器右键菜单 | 支持 | 支持（打包 COM 扩展） |
| 安装位置 | `%LOCALAPPDATA%\Programs\New7z` | `WindowsApps`（只读） |
| 设置存储 | `%LOCALAPPDATA%\New7z` | 同一目录，已申请不虚拟化，与其他版本共用 |
| 卸载 | Inno 卸载程序 | Windows「已安装的应用」 |

## 已知限制

- **右键菜单设置的 `unvirtualizedResources` 受限能力**：Partner Center 于 2026-11-09 拒绝了当前请求。若保留此功能并申请复议，需在 Partner Center 补充有效的支持联系方式或开发者网站，并明确说明：应用设置与打包的 Explorer COM 菜单处理程序需要共享 `HKCU\\Software\\New7z` 的命令开关和自动关闭偏好；该豁免仅限应用自身键值，不读写其他应用数据。若不申请复议，则需先移除该能力及相应虚拟化排除，并改造设置与菜单处理程序的数据共享后再提交。
- **设置不迁移**：从安装包版本切换到 Store 版本时，主题、语言、最近使用记录不会自动带过去（两者目录相同但 Store 版首次运行使用自己的默认值）。
- **本地安装测试**：安装未签名的 `.msix` 需要开启开发者模式并用自签名证书，或直接把包上传到 Partner Center 用其测试通道验证。
