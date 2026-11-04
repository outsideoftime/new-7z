# 第三方组件

New7z 自有代码版权归王国梁所有，采用仓库根目录的 MIT 许可证。该许可不替代下列第三方组件各自的许可证。

- **7-Zip 26.05 / LZMA SDK**：Igor Pavlov 及相应贡献者。7-Zip 完整引擎以 LGPL 为主，部分代码有 BSD/public domain 声明，RAR 解码附 unRAR 限制；以 `7zSdk/DOC`、完整源码归档中的 `DOC/License.txt`、`DOC/copying.txt` 及各文件声明为准。仓库保留未修改的 `7zSdk/7z2605-src.7z`，构建时解压到忽略的 `7zSdk/Source`，发行包随附源码和许可。
- **.NET**：Microsoft 及贡献者，MIT 和各第三方许可证。发行包随附运行库的 LICENSE / THIRD-PARTY-NOTICES。
- **Windows App SDK / WinUI**：Microsoft 及贡献者，以相应 NuGet 包的 license.txt / NOTICE.txt 为准；发行包保留这些文件。
- **Inno Setup**：Jordan Russell、Martijn Laan 及贡献者，保留其许可证及安装器内原有版权标识。
- **简体中文安装器翻译**：`installer/ChineseSimplified.isl` 来自 jrsoftware/issrc，保留原维护者及来源说明。
- **开发/原型工具**：Playwright、axe-core 等仅用于开发验证，见 `package-lock.json` 对应软件包许可证，不作为应用运行依赖打入安装包。

`图标/` 为用户提供的品牌素材，供本项目构建和展示使用，不宣称第三方商标或图形权利归 New7z 所有。
