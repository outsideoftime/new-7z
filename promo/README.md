# New7z 竖屏宣传片（中文 / English）

34 秒、1080×1920、60 fps 的竖屏宣传片，主题是「一个压缩软件，能有多讲究？」。
同一套时间轴渲染中文、英文两个版本；画面由 HTML/CSS 动效逐帧生成，配乐由代码合成，全部素材来自本仓库，可商用、无第三方音乐版权问题。

## 交付物

| 文件 | 规格 | 用途 |
|---|---|---|
| `out/new7z-promo-douyin-zh-1080x1920.mp4` | 1080×1920 · 34 秒 · 60 fps · 含配乐 | 抖音（中文） |
| `out/new7z-promo-douyin-en-1080x1920.mp4` | 1080×1920 · 34 秒 · 60 fps · 含配乐 | 海外竖屏平台（英文） |
| `out/cover-douyin-1080x1440.png` | 1080×1440（3:4） | 中文封面 |
| `out/cover-douyin-1080x1440-en.png` | 1080×1440（3:4） | 英文封面 |
| `out/score.wav` | 48 kHz · 34 秒 | 配乐单独保留 |

上传文案见 [上传文案.md](上传文案.md)。

## 分镜

| 时间 | 段落 | 内容 |
|---|---|---|
| 0–2s | HOOK | 「一个压缩软件，能有多讲究？」/ "A file archiver can look this good?" |
| 2–6s | BRAND | Logo 亮相、New7z 新 7z、为 Windows 11 而生 |
| 6–10s | INTERFACE | 真实主界面 3D 入场，切到「新建压缩包」对话框 |
| 10–14s | PREVIEW | 真实压缩包浏览界面 + 源码预览浮层 |
| 14–18s | FORMATS | 八种格式图标依次入场 |
| 18–22s | ICONS | 品牌图标 / 7Z / ZIP 三张卡片翻面，演示自定义 PNG 图标 |
| 22–26s | CONTEXT | 资源管理器右键菜单（菜单项与设计文档一致） |
| 26–30s | SPECS | 23 MB / 0 依赖 / 0 管理员权限 / MIT |
| 30–34s | OUTRO | 片尾 Logo、GitHub 地址、版本号 |

## 素材真实性

- 界面截图：`assets/ui-*.png` 均为 `artifacts/store-assets/screenshots/zh-CN/` 下的真实应用截图（深色 / 浅色主题）。英文版中的界面截图仍是中文界面——应用本身支持英文界面，但仓库只有中文截图，没有伪造英文截图。
- 格式图标：`assets/formats/*.png` 为 `src/New7z.App/Assets/Formats/` 下的正式图标。
- 自定义图标一幕：用色相旋转演示「换成自己的 PNG 图标」这一真实功能，卡片上的「默认 / 自定义」状态为示意。
- 预览浮层：按应用预览窗的样式绘制，展示的 `MainWindow.Preview.cs` 片段来自本仓库源码。
- 右键菜单：菜单项取自 [docs/右键菜单设计.md](../docs/右键菜单设计.md) 与 `docs/Explorer右键验收.md`，为示意图，不是系统截图。
- 配乐：`audio.cjs` 程序化合成的原创音轨，无外部采样。

## 重新渲染

依赖：Node、Playwright（仓库根目录已安装）、ffmpeg 在 PATH 中。

```bash
node promo/render.cjs --fps=60 --quality=96 --lang=zh --out=new7z-promo-douyin-zh-1080x1920-silent.mp4
node promo/render.cjs --fps=60 --quality=96 --lang=en --out=new7z-promo-douyin-en-1080x1920-silent.mp4
node promo/audio.cjs
ffmpeg -y -i promo/out/new7z-promo-douyin-zh-1080x1920-silent.mp4 -i promo/out/score.wav \
  -map 0:v -map 1:a -c:v copy -af loudnorm=I=-14:TP=-1.5:LRA=11 -c:a aac -b:a 192k -shortest \
  promo/out/new7z-promo-douyin-zh-1080x1920.mp4
ffmpeg -y -i promo/out/new7z-promo-douyin-en-1080x1920-silent.mp4 -i promo/out/score.wav \
  -map 0:v -map 1:a -c:v copy -af loudnorm=I=-14:TP=-1.5:LRA=11 -c:a aac -b:a 192k -shortest \
  promo/out/new7z-promo-douyin-en-1080x1920.mp4
```

检查单帧与封面：

```bash
node promo/render.cjs --still=8.2
node promo/render.cjs --still=8.2 --lang=en
node promo/render.cjs --cover=true
node promo/render.cjs --cover=true --lang=en
```

## 结构

- `index.html` / `promo.css` / `promo.js`：舞台、样式与确定性时间轴（`renderAt(t)`），不依赖 CSS 动画，逐帧可复现。中英文文案集中在 `promo.js` 顶部的 `I18N` 字典，页面用 `?lang=en` 切换。
- `render.cjs`：本地静态服务器 + Playwright 逐帧截图（JPEG 采集，PNG 编码太慢），通过管道直接送入 ffmpeg。
- `audio.cjs`：120 BPM、A 小调的合成音轨，鼓点与 2 秒一格的剪辑点对齐。
- `cover.html`：封面版式，同样支持 `?lang=en`。

改文案主要改 `promo.js` 的 `I18N`，改时间点改 `SCENES` 与各 `renderS*` 函数。
