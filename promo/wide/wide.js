/* New7z · B 站 / YouTube 横屏宣传片 —— 确定性时间轴渲染（中 / 英）
   所有动画由 renderAt(t) 计算，避免 CSS 动画，保证逐帧截图可复现。 */
(() => {
  const $ = (id) => document.getElementById(id);
  const clamp01 = (x) => Math.min(1, Math.max(0, x));
  const ramp = (t, a, b) => clamp01((t - a) / (b - a));
  const easeOutExpo = (x) => (x >= 1 ? 1 : 1 - Math.pow(2, -10 * x));
  const easeOutCubic = (x) => 1 - Math.pow(1 - x, 3);
  const easeInCubic = (x) => x * x * x;
  const easeInOutCubic = (x) => (x < 0.5 ? 4 * x * x * x : 1 - Math.pow(-2 * x + 2, 3) / 2);
  const easeOutBack = (x) => { const c = 2.2; return 1 + (c + 1) * Math.pow(x - 1, 3) + c * Math.pow(x - 1, 2); };
  const mix = (a, b, p) => a + (b - a) * p;
  const T = 55.0;
  const SHIFT = 5.0;   // 文件图标章节插入后，后续章节整体顺延

  /* ---------- 文案（中 / 英） ---------- */
  const I18N = {
    zh: {
      "hook.l1": "一个压缩软件",
      "hook.l2": "能有多讲究？",
      "hook.micro": "简单 · 实用 · 按你的习惯",
      "brand.tag": "为 Windows 11 而生",
      "brand.micro": "WINUI 3 · 完整 7-Zip 引擎 · 23 MB",
      "s3.title": "打开，就能用",
      "s3.sub": "打开、新建、拖进来。没有向导，也没有广告。",
      "s3.f1": "打开压缩包",
      "s3.f1d": "双击、拖放，或者右键打开",
      "s3.f2": "新建压缩",
      "s3.f2d": "7z / ZIP / TAR，两步完成",
      "s3.f3": "拖进窗口",
      "s3.f3d": "自动识别文件还是压缩包",
      "s4.title": "右键，就是一整套",
      "s4.sub": "解压到此处、解压到…、压缩、测试完整性，都在资源管理器里完成。",
      "s4.batch": "多选批量解压 · 一次完成",
      "s4.note": "资源管理器 · New7z 菜单",
      "menu.open": "用 New7z 打开",
      "menu.extractTo": "解压到“项目素材\\”",
      "menu.extractDots": "解压到…",
      "menu.extractHere": "解压到当前文件夹",
      "menu.test": "测试完整性",
      "s5.title": "不解压，先看一眼",
      "s5.sub": "图片、文本、源码，在压缩包里直接预览。",
      "s5.foot": "压缩包内 · 只读 · 源码预览",
      "s6.title": "常见格式，一个入口",
      "s6.note": "创建 7z / ZIP / TAR · 读取和解压其余格式",
      "s6.extra1": "另支持 ZST / LZ4 单文件流",
      "sf.title": "文件图标，一眼认出",
      "sf.sub": "压缩包列表里，42 种文件类型自动显示对应图标。",
      "sf.caption": "42 种类型 · 按扩展名识别",
      "s7.title": "剩下的，按你的习惯来",
      "s7.sub": "设置分类清楚，改的都是你常用的地方。",
      "s7.iconBrand": "品牌图标",
      "s7.iconnote": "自定义图标：换成你的 PNG",
      "s7.panelTitle": "设置",
      "s7.theme": "深色",
      "s7.r1": "常规",
      "s7.r2": "压缩",
      "s7.r3": "右键菜单",
      "s7.r4": "外观与图标",
      "s7.r5": "文件关联",
      "s7.r6": "关于",
      "s7.pill1": "深色 / 浅色",
      "s7.pill2": "中文 / English",
      "s8.title": "简单，也要安全",
      "s8.sub": "本地运行，解压之前先检查。",
      "s8.c1": "完全离线",
      "s8.c1d": "除了检查更新，不访问网络",
      "s8.c2": "无需管理员",
      "s8.c2d": "当前用户安装，卸载干净",
      "s8.c3": "解压前预检",
      "s8.c3d": "路径、链接与大小上限先检查",
      "s9.title": "小，但完整",
      "s9.sub": "一个安装包，不需要额外的运行库。",
      "s9.r1": "安装包 · 含完整 7-Zip 引擎",
      "s9.r2": "额外运行库 · 完全离线可用",
      "s9.r3": "管理员权限要求",
      "s9.r4": "开源许可 · 免费商用",
      "outro.line": "开源免费 · 为 Windows 11 而生"
    },
    en: {
      "hook.l1": "A file archiver",
      "hook.l2": "can be this simple?",
      "hook.micro": "SIMPLE · PRACTICAL · YOURS",
      "brand.tag": "Built for Windows 11",
      "brand.micro": "WINUI 3 · FULL 7-ZIP ENGINE · 23 MB",
      "s3.title": "Open. Create. Drag.",
      "s3.sub": "No wizards, no ads, no extra steps.",
      "s3.f1": "Open an archive",
      "s3.f1d": "Double-click, drop it, or right-click",
      "s3.f2": "Create one",
      "s3.f2d": "7z / ZIP / TAR in two steps",
      "s3.f3": "Drag it in",
      "s3.f3d": "Files and archives are both recognised",
      "s4.title": "Right-click. Done.",
      "s4.sub": "Extract here, extract to…, compress, test — right in Explorer.",
      "s4.batch": "Batch extraction · in one pass",
      "s4.note": "Explorer · New7z menu",
      "menu.open": "Open with New7z",
      "menu.extractTo": "Extract to “Project files\\”",
      "menu.extractDots": "Extract to…",
      "menu.extractHere": "Extract here",
      "menu.test": "Test archive",
      "s5.title": "Peek inside",
      "s5.sub": "Images, text and source — previewed without extracting.",
      "s5.foot": "Inside archive · read-only · source preview",
      "s6.title": "Every common format",
      "s6.note": "Create 7z / ZIP / TAR · extract the rest",
      "s6.extra1": "Plus ZST / LZ4 single-file streams",
      "sf.title": "Know the file at a glance",
      "sf.sub": "42 file types get their own icon in the archive list.",
      "sf.caption": "42 types · matched by extension",
      "s7.title": "Make it yours",
      "s7.sub": "Clear settings, right where you use them.",
      "s7.iconBrand": "Brand icon",
      "s7.iconnote": "Custom icons — your own PNG",
      "s7.panelTitle": "Settings",
      "s7.theme": "Dark",
      "s7.r1": "General",
      "s7.r2": "Compression",
      "s7.r3": "Context menu",
      "s7.r4": "Appearance & icons",
      "s7.r5": "File associations",
      "s7.r6": "About",
      "s7.pill1": "Dark / Light",
      "s7.pill2": "中文 / English",
      "s8.title": "Simple, and safe",
      "s8.sub": "Runs locally, checks before it extracts.",
      "s8.c1": "Fully offline",
      "s8.c1d": "Only update checks touch the network",
      "s8.c2": "No admin rights",
      "s8.c2d": "Per-user install, clean uninstall",
      "s8.c3": "Pre-flight checks",
      "s8.c3d": "Paths, links and size limits first",
      "s9.title": "Small. Complete.",
      "s9.sub": "One installer, no extra runtimes.",
      "s9.r1": "Installer · full 7-Zip engine",
      "s9.r2": "Extra runtimes · fully offline",
      "s9.r3": "Admin rights required",
      "s9.r4": "Open source · free to use",
      "outro.line": "Open source · built for Windows 11"
    }
  };
  const LANG = new URLSearchParams(location.search).get("lang") === "en" ? "en" : "zh";
  document.documentElement.lang = LANG === "en" ? "en" : "zh-CN";
  document.documentElement.setAttribute("data-lang", LANG);
  const dict = I18N[LANG];
  document.querySelectorAll("[data-i18n]").forEach((el) => {
    const k = el.getAttribute("data-i18n");
    if (dict[k]) el.textContent = dict[k];
  });
  if (LANG === "en") {
    $("s1-kicker").textContent = "NEW7Z · WINDOWS 11";
    const names = ["Quarterly review.pptx", "MainWindow.cs", "settings.json", "User guide.pdf", "Demo recording.mp4", "Cover model.blend"];
    document.querySelectorAll("#sf-list .fl-row span").forEach((el, i) => { if (names[i]) el.textContent = names[i]; });
    document.querySelector("#sf-list .fc-head span").textContent = "Project files.7z";
    document.querySelector("#sf-list .fc-head em").textContent = "4 folders · 38 files";
  }

  /* ---------- build format grid ---------- */
  const FORMATS = [
    ["7z", "7Z"], ["zip", "ZIP"], ["rar", "RAR"], ["tar", "TAR"],
    ["gz", "GZ"], ["xz", "XZ"], ["bz2", "BZ2"], ["iso", "ISO"]
  ];
  const grid = $("s6-grid");
  FORMATS.forEach(([file, label]) => {
    const d = document.createElement("div");
    d.className = "fmt";
    d.innerHTML = `<img src="../assets/formats/${file}.png" alt="${label}"><span>${label}</span>`;
    grid.appendChild(d);
  });

  const TYPES = [
    ["archive", "ARCHIVE"], ["image", "IMAGE"], ["video", "VIDEO"], ["audio", "AUDIO"],
    ["font", "FONT"], ["word", "WORD"], ["excel", "EXCEL"], ["powerpoint", "PPT"], ["pdf", "PDF"],
    ["text", "TEXT"], ["markdown", "MD"], ["log", "LOG"], ["config", "CONFIG"],
    ["json", "JSON"], ["xml", "XML"], ["yaml", "YAML"], ["sql", "SQL"],
    ["html", "HTML"], ["css", "CSS"], ["js", "JS"], ["typescript", "TS"],
    ["react", "REACT"], ["vue", "VUE"], ["sass", "SASS"], ["python", "PY"],
    ["csharp", "C#"], ["c", "C"], ["cpp", "C++"], ["java", "JAVA"],
    ["kotlin", "KOTLIN"], ["swift", "SWIFT"], ["go", "GO"], ["rust", "RUST"],
    ["ruby", "RUBY"], ["php", "PHP"], ["shell", "SHELL"], ["powershell", "PS1"],
    ["docker", "DOCKER"], ["git", "GIT"], ["unity", "UNITY"], ["blender", "BLEND"],
    ["binary", "BINARY"]
  ];
  const typeGrid = $("sf-grid");
  TYPES.forEach(([file, label]) => {
    const d = document.createElement("div");
    d.className = "tcell";
    d.innerHTML = `<img src="../assets/filetypes/${file}.png" alt="${label}"><span>${label}</span>`;
    typeGrid.appendChild(d);
  });

  document.querySelectorAll("#s4-menu .menu-item").forEach((el) => {
    const hl = document.createElement("div");
    hl.className = "hl";
    el.prepend(hl);
  });

  const els = {
    hud: $("hud"), progress: $("hud-progress"), chapterIndex: $("chapter-index"), chapterName: $("chapter-name"),
    sweep: $("sweep"), rings: $("rings"),
    a1: document.querySelector(".a1"), a2: document.querySelector(".a2"), a3: document.querySelector(".a3"),
    b1: document.querySelector(".b1"), b2: document.querySelector(".b2"),
    s1: $("s1"), s2: $("s2"), s3: $("s3"), s4: $("s4"), s5: $("s5"), s6: $("s6"),
    sf: $("sf"), s7: $("s7"), s8: $("s8"), s9: $("s9"), s10: $("s10")
  };

  const SCENES = [
    { t0: 0, t1: 3, name: "HOOK" },
    { t0: 3, t1: 7, name: "BRAND" },
    { t0: 7, t1: 13, name: "SIMPLE" },
    { t0: 13, t1: 19, name: "EXTRACT" },
    { t0: 19, t1: 24, name: "PREVIEW" },
    { t0: 24, t1: 29, name: "FORMATS" },
    { t0: 29, t1: 34, name: "FILES" },
    { t0: 34, t1: 41, name: "YOURS" },
    { t0: 41, t1: 46, name: "SAFE" },
    { t0: 46, t1: 51, name: "SPECS" },
    { t0: 51, t1: 55, name: "OUTRO" }
  ];
  const CUTS = [3, 7, 13, 19, 24, 29, 34, 41, 46, 51];

  function gate(el, t, t0, t1, inDur = 0.55, outDur = 0.35) {
    if (t < t0 - 0.001 || t > t1 + 0.001) {
      el.style.opacity = 0;
      el.style.visibility = "hidden";
      return 0;
    }
    el.style.visibility = "visible";
    const pin = inDur <= 0.02 ? (t >= t0 ? 1 : 0) : ramp(t, t0, t0 + inDur);
    const pout = ramp(t, t1 - outDur, t1);
    const o = easeOutCubic(pin) * (1 - easeInCubic(pout));
    el.style.opacity = o;
    return o;
  }

  function renderGlobal(t) {
    els.a1.style.transform = `translate3d(${Math.sin(t * 0.21) * 70}px,${Math.cos(t * 0.17) * 55}px,0)`;
    els.a2.style.transform = `translate3d(${Math.cos(t * 0.16) * 80}px,${Math.sin(t * 0.23) * 65}px,0)`;
    els.a3.style.transform = `translate3d(${Math.sin(t * 0.13 + 1) * 90}px,${Math.cos(t * 0.19) * 45}px,0)`;
    els.b1.style.opacity = 0.16 + 0.12 * (0.5 + 0.5 * Math.sin(t * 0.6));
    els.b2.style.opacity = 0.10 + 0.10 * (0.5 + 0.5 * Math.sin(t * 0.5 + 2));

    const hudOn = ramp(t, 0.7, 1.5) * (1 - ramp(t, 54.2, 55));
    els.hud.style.opacity = easeOutCubic(hudOn);
    els.progress.style.width = (1728 * clamp01(t / T)) + "px";

    let idx = 0;
    for (let i = 0; i < SCENES.length; i++) if (t >= SCENES[i].t0) idx = i;
    els.chapterIndex.textContent = String(idx + 1).padStart(2, "0");
    els.chapterName.textContent = SCENES[idx].name;

    let sweepOp = 0, sweepX = 0;
    for (const c of CUTS) {
      const p = (t - (c - 0.18)) / 0.72;
      if (p > 0 && p < 1) {
        sweepOp = Math.sin(p * Math.PI) * 0.55;
        sweepX = mix(-2400, 2400, easeInOutCubic(p));
      }
    }
    els.sweep.style.opacity = sweepOp;
    els.sweep.style.transform = `translate3d(${sweepX}px,0,0)`;
  }

  /* ---------- 01 hook ---------- */
  function renderS1(t) {
    const on = gate(els.s1, t, 0, 3, 0.01, 0.45);
    if (!on) return;
    const k = easeOutCubic(ramp(t, -0.25, 0.5));
    const k1 = easeOutExpo(ramp(t, -0.3, 0.72));
    const k2 = easeOutExpo(ramp(t, 0.12, 1.05));
    const kr = easeOutCubic(ramp(t, 0.62, 1.2));
    const km = easeOutCubic(ramp(t, 0.8, 1.5));

    const kicker = $("s1-kicker");
    kicker.style.opacity = 0.55 + 0.45 * k;
    kicker.style.transform = `translate3d(0,${mix(14, 0, k)}px,0)`;

    $("s1-l1").firstElementChild.style.transform = `translate3d(0,${mix(105, 0, k1)}%,0)`;
    $("s1-l2").firstElementChild.style.transform =
      `translate3d(0,${mix(112, 0, k2)}%,0) scale(${mix(1.05, 1, k2)})`;

    const rule = $("s1-rule");
    rule.style.width = (160 * kr) + "px";
    rule.style.opacity = kr;
    rule.style.left = `calc(50% - ${80 * kr}px)`;

    const micro = $("s1-micro");
    micro.style.opacity = km * 0.9;
    micro.style.top = `${700 + mix(12, 0, km)}px`;
  }

  /* ---------- 02 brand ---------- */
  function renderS2(t) {
    const on = gate(els.s2, t, 3, 7, 0.42, 0.4);
    if (!on) return;
    const ring = $("s2-ring");
    const pr = easeOutCubic(ramp(t, 3.0, 3.6));
    ring.style.opacity = pr * 0.9;
    ring.querySelector("svg").style.transform = `rotate(${(t - 3) * 7}deg)`;

    const halo = $("s2-halo");
    halo.style.opacity = pr * (0.7 + 0.3 * Math.sin((t - 3) * 1.4));

    const pl = ramp(t, 3.0, 3.6);
    const logo = $("s2-logo");
    logo.style.opacity = easeOutCubic(pl);
    logo.style.transform =
      `translate3d(0,${mix(46, 0, easeOutExpo(pl)) + Math.sin((t - 3) * 1.5) * 5}px,0)` +
      ` scale(${mix(0.78, 1, easeOutExpo(pl))})`;
    logo.style.filter = `blur(${mix(18, 0, easeOutCubic(pl))}px)`;

    const sh = ramp(t, 3.9, 4.65);
    const sheen = $("s2-sheen");
    sheen.style.backgroundPosition = `${mix(160, -60, easeInOutCubic(sh))}% 0`;
    sheen.style.opacity = sh > 0 && sh < 1 ? 0.6 : 0;

    const pw = ramp(t, 3.3, 4.05);
    $("s2-word").firstElementChild.style.transform = `translate3d(0,${mix(115, 0, easeOutExpo(pw))}%,0)`;

    const pc = easeOutCubic(ramp(t, 3.85, 4.5));
    const cn = $("s2-cn");
    cn.style.opacity = pc;
    cn.style.transform = `translate3d(0,${mix(10, 0, pc)}px,0)`;

    const pt = easeOutCubic(ramp(t, 4.1, 4.85));
    const tag = $("s2-tag");
    tag.style.opacity = pt;
    tag.style.transform = `translate3d(0,${mix(12, 0, pt)}px,0)`;

    const pm = easeOutCubic(ramp(t, 4.5, 5.15));
    const micro = $("s2-micro");
    micro.style.opacity = pm * 0.85;
    micro.style.transform = `translate3d(0,${mix(8, 0, pm)}px,0)`;

    els.s2.style.transform = `scale(${mix(1, 1.015, ramp(t, 3, 7))})`;
  }

  /* ---------- 03 simple ---------- */
  function renderS3(t) {
    const on = gate(els.s3, t, 7, 13, 0.42, 0.36);
    if (!on) return;
    const head = $("s3-head");
    const ph = easeOutCubic(ramp(t, 7.05, 7.6));
    head.style.opacity = ph;
    head.style.transform = `translate3d(0,${mix(26, 0, ph)}px,0)`;

    const pw = ramp(t, 7.05, 8.5);
    const win = $("s3-win");
    const e = easeOutExpo(pw);
    win.style.transform =
      `rotateY(${mix(-9, 0, easeOutCubic(pw))}deg)` +
      ` translate3d(${mix(70, 0, e)}px,${mix(40, 0, e)}px,0) scale(${mix(0.94, 1, e) + 0.01 * ramp(t, 8.5, 13)})`;
    win.style.opacity = easeOutCubic(ramp(t, 7.05, 7.8));
    $("s3-pool").style.opacity = easeOutCubic(ramp(t, 7.2, 8.4)) * 0.9;

    $("s3-features").querySelectorAll(".feature").forEach((el, i) => {
      const st = 7.5 + i * 0.18;
      const p = ramp(t, st, st + 0.62);
      el.style.opacity = easeOutCubic(ramp(t, st, st + 0.4));
      el.style.transform = `translate3d(${mix(-26, 0, easeOutExpo(p))}px,0,0)`;
    });

    els.s3.style.transform = `scale(${mix(1, 1.008, ramp(t, 7, 13))})`;
  }

  /* ---------- 04 extract ---------- */
  function renderS4(t) {
    const on = gate(els.s4, t, 13, 19, 0.4, 0.34);
    if (!on) return;
    const head = $("s4-head");
    const ph = easeOutCubic(ramp(t, 13.05, 13.6));
    head.style.opacity = ph;
    head.style.transform = `translate3d(0,${mix(26, 0, ph)}px,0)`;

    const pm = ramp(t, 13.25, 14.1);
    const menu = $("s4-menu");
    menu.style.opacity = easeOutCubic(ramp(t, 13.25, 13.9));
    menu.style.transform = `translate3d(0,${mix(44, 0, easeOutExpo(pm))}px,0) scale(${mix(0.96, 1, easeOutExpo(pm))})`;

    menu.querySelectorAll(".menu-item").forEach((el, i) => {
      const p = ramp(t, 13.5 + i * 0.075, 14.05 + i * 0.075);
      el.style.opacity = easeOutCubic(p);
      el.style.transform = `translate3d(${mix(-14, 0, easeOutCubic(p))}px,0,0)`;
    });

    const pc = ramp(t, 14.35, 15.2);
    const cursor = $("s4-cursor");
    cursor.style.opacity = easeOutCubic(ramp(t, 14.35, 14.6));
    const cx = mix(1660, 1092, easeInOutCubic(pc));
    const cy = mix(760, 382, easeInOutCubic(pc));
    cursor.style.transform = `translate3d(${cx}px,${cy}px,0) scale(${mix(1.1, 1, pc)})`;

    const hl = menu.querySelectorAll(".hl")[1];
    const phl = ramp(t, 15.05, 15.35) * (1 - 0.35 * ramp(t, 16.4, 17));
    hl.style.opacity = phl;
    const press = Math.sin(clamp01((t - 15.2) / 0.28) * Math.PI);
    hl.style.boxShadow = `inset 0 0 0 1px rgba(120,180,255,${0.22 + 0.4 * press})`;

    const pb = easeOutCubic(ramp(t, 15.5, 16.1));
    const batch = $("s4-batch");
    batch.style.opacity = pb;
    batch.style.transform = `translate3d(${mix(-16, 0, pb)}px,0,0)`;

    const pn = easeOutCubic(ramp(t, 15.9, 16.5));
    const note = $("s4-note");
    note.style.opacity = pn * 0.9;
    note.style.transform = `translate3d(0,${mix(10, 0, pn)}px,0)`;

    els.s4.style.transform = `scale(${mix(1, 1.006, ramp(t, 13, 19))})`;
  }

  /* ---------- 05 preview ---------- */
  function renderS5(t) {
    const on = gate(els.s5, t, 19, 24, 0.42, 0.36);
    if (!on) return;
    const head = $("s5-head");
    const ph = easeOutCubic(ramp(t, 19.05, 19.6));
    head.style.opacity = ph;
    head.style.transform = `translate3d(0,${mix(26, 0, ph)}px,0)`;

    const pw = ramp(t, 19.05, 20.4);
    const win = $("s5-win");
    win.style.transform =
      `rotateY(${mix(8, 0, easeOutExpo(pw))}deg)` +
      ` translate3d(${mix(60, 0, easeOutExpo(pw))}px,${mix(50, 0, easeOutExpo(pw)) - ramp(t, 20.4, 24) * 8}px,0)` +
      ` scale(${mix(0.93, 1, easeOutExpo(pw))})`;
    win.style.opacity = easeOutCubic(ramp(t, 19.05, 19.8));

    const pk = ramp(t, 20.0, 20.95);
    const card = $("s5-card");
    card.style.opacity = easeOutCubic(ramp(t, 20.0, 20.5));
    card.style.transform =
      `rotate(${mix(-5, -2.4, easeOutCubic(pk))}deg) translate3d(0,${mix(80, 0, easeOutBack(pk)) + Math.sin((t - 20.2) * 1.7) * 5}px,0)` +
      ` scale(${mix(0.9, 1, easeOutBack(pk))})`;

    els.s5.style.transform = `scale(${mix(1, 1.01, ramp(t, 19, 24))})`;
  }

  /* ---------- 06 formats ---------- */
  function renderS6(t) {
    const on = gate(els.s6, t, 24, 29, 0.4, 0.34);
    if (!on) return;
    const head = $("s6-head");
    const ph = easeOutCubic(ramp(t, 24.05, 24.6));
    head.style.opacity = ph;
    head.style.transform = `translate3d(0,${mix(24, 0, ph)}px,0)`;

    grid.querySelectorAll(".fmt").forEach((el, i) => {
      const start = 24.3 + i * 0.065;
      const p = ramp(t, start, start + 0.62);
      el.style.opacity = easeOutCubic(ramp(t, start, start + 0.3));
      el.style.transform =
        `perspective(800px) rotateY(${mix(-52, 0, easeOutExpo(p))}deg)` +
        ` translate3d(0,${mix(24, 0, easeOutCubic(p)) + Math.sin((t - start) * 1.8 + i) * 3}px,0)` +
        ` scale(${mix(0.6, 1, easeOutBack(p))})`;
    });

    const pl = easeOutCubic(ramp(t, 25.25, 25.95));
    const line = $("s6-line");
    line.style.opacity = pl;
    line.style.transform = `translate3d(0,${mix(10, 0, pl)}px,0)`;

    const pn = easeOutCubic(ramp(t, 25.6, 26.3));
    const note = $("s6-note");
    note.style.opacity = pn * 0.95;
    note.style.transform = `translate3d(0,${mix(10, 0, pn)}px,0)`;

    const pe = easeOutCubic(ramp(t, 26.05, 26.7));
    const pills = $("s6-pills");
    pills.style.opacity = pe;
    pills.style.transform = `translate3d(0,${mix(12, 0, pe)}px,0)`;

    els.s6.style.transform = `scale(${mix(1, 1.008, ramp(t, 24, 29))})`;
  }

  /* ---------- 07 file icons ---------- */
  function renderSFile(t) {
    const on = gate(els.sf, t, 29, 34, 0.4, 0.34);
    if (!on) return;
    const head = $("sf-head");
    const ph = easeOutCubic(ramp(t, 29.05, 29.6));
    head.style.opacity = ph;
    head.style.transform = `translate3d(0,${mix(24, 0, ph)}px,0)`;

    const pc = ramp(t, 29.15, 30.0);
    const card = $("sf-list");
    card.style.opacity = easeOutCubic(ramp(t, 29.15, 29.8));
    card.style.transform = `translate3d(0,${mix(40, 0, easeOutExpo(pc))}px,0) scale(${mix(0.97, 1, easeOutExpo(pc))})`;

    card.querySelectorAll(".fl-row").forEach((el, i) => {
      const st = 29.45 + i * 0.09;
      const p = ramp(t, st, st + 0.55);
      el.style.opacity = easeOutCubic(ramp(t, st, st + 0.35));
      el.style.transform = `translate3d(${mix(-18, 0, easeOutExpo(p))}px,0,0)`;
    });

    typeGrid.querySelectorAll(".tcell").forEach((el, i) => {
      const st = 29.25 + i * 0.022;
      const p = ramp(t, st, st + 0.45);
      el.style.opacity = easeOutCubic(p);
      el.style.transform = `scale(${mix(0.7, 1, easeOutBack(p))})`;
    });

    const pcap = easeOutCubic(ramp(t, 31.1, 31.75));
    const cap = $("sf-caption");
    cap.style.opacity = pcap;
    cap.style.transform = `translate3d(0,${mix(10, 0, pcap)}px,0)`;

    els.sf.style.transform = `scale(${mix(1, 1.006, ramp(t, 29, 34))})`;
  }

  /* ---------- 08 yours ---------- */
  const HUES = [-120, -70, 80];
  function renderS7(t) {
    t -= SHIFT;
    const on = gate(els.s7, t, 29, 36, 0.4, 0.34);
    if (!on) return;
    const head = $("s7-head");
    const ph = easeOutCubic(ramp(t, 29.05, 29.6));
    head.style.opacity = ph;
    head.style.transform = `translate3d(0,${mix(24, 0, ph)}px,0)`;

    const pp = ramp(t, 29.15, 30.0);
    const panel = $("s7-panel");
    panel.style.opacity = easeOutCubic(ramp(t, 29.15, 29.8));
    panel.style.transform = `translate3d(${mix(50, 0, easeOutExpo(pp))}px,${mix(26, 0, easeOutExpo(pp))}px,0) scale(${mix(0.97, 1, easeOutExpo(pp))})`;

    panel.querySelectorAll(".sp-row").forEach((el, i) => {
      const st = 29.5 + i * 0.12;
      const p = ramp(t, st, st + 0.55);
      el.style.opacity = easeOutCubic(ramp(t, st, st + 0.35));
      el.style.transform = `translate3d(${mix(26, 0, easeOutExpo(p))}px,0,0)`;
    });

    const themeEl = $("s7-theme");
    themeEl.textContent = t > 33.7 ? (LANG === "en" ? "Light" : "浅色") : (LANG === "en" ? "Dark" : "深色");

    const pt = easeOutCubic(ramp(t, 31.15, 31.75));
    const pills = $("s7-pills");
    pills.style.opacity = pt;
    pills.style.transform = `translate3d(0,${mix(12, 0, pt)}px,0)`;

    const pi = easeOutCubic(ramp(t, 31.55, 32.15));
    $("s7-icons").style.opacity = pi;
    $("s7-icons").style.transform = `translate3d(0,${mix(18, 0, pi)}px,0)`;

    $("s7-icons").querySelectorAll(".mini-card").forEach((card, i) => {
      const fs = 32.8 + i * 0.28;
      const fp = easeInOutCubic(ramp(t, fs, fs + 0.55));
      const flip = card.querySelector(".flip");
      flip.style.setProperty("--hue", HUES[i] + "deg");
      flip.style.transform = `rotateY(${180 * fp}deg)`;
    });

    const pn = easeOutCubic(ramp(t, 33.0, 33.6));
    const note = $("s7-iconnote");
    note.style.opacity = pn;
    note.style.transform = `translate3d(0,${mix(10, 0, pn)}px,0)`;

    els.s7.style.transform = `scale(${mix(1, 1.006, ramp(t, 29, 36))})`;
  }

  /* ---------- 09 safe ---------- */
  function renderS8(t) {
    t -= SHIFT;
    const on = gate(els.s8, t, 36, 41, 0.4, 0.34);
    if (!on) return;
    const head = $("s8-head");
    const ph = easeOutCubic(ramp(t, 36.05, 36.6));
    head.style.opacity = ph;
    head.style.transform = `translate3d(0,${mix(24, 0, ph)}px,0)`;

    $("s8-cards").querySelectorAll(".safe-card").forEach((el, i) => {
      const st = 36.4 + i * 0.14;
      const p = ramp(t, st, st + 0.65);
      el.style.opacity = easeOutCubic(ramp(t, st, st + 0.42));
      el.style.transform = `translate3d(0,${mix(34, 0, easeOutExpo(p))}px,0) scale(${mix(0.97, 1, easeOutBack(p))})`;
    });

    els.s8.style.transform = `scale(${mix(1, 1.006, ramp(t, 36, 41))})`;
  }

  /* ---------- 10 specs ---------- */
  function renderS9(t) {
    t -= SHIFT;
    const on = gate(els.s9, t, 41, 46, 0.4, 0.34);
    if (!on) return;
    const head = $("s9-head");
    const ph = easeOutCubic(ramp(t, 41.05, 41.6));
    head.style.opacity = ph;
    head.style.transform = `translate3d(0,${mix(24, 0, ph)}px,0)`;

    $("s9-list").querySelectorAll(".spec-row").forEach((el, i) => {
      const start = 41.4 + i * 0.13;
      const p = ramp(t, start, start + 0.7);
      el.style.opacity = easeOutCubic(ramp(t, start, start + 0.45));
      el.style.transform = `translate3d(${mix(-34, 0, easeOutExpo(p))}px,0,0)`;
    });

    els.s9.style.transform = `scale(${mix(1, 1.006, ramp(t, 41, 46))})`;
  }

  /* ---------- 11 outro ---------- */
  function renderS10(t) {
    t -= SHIFT;
    const on = gate(els.s10, t, 46, 50, 0.4, 0.01);
    if (!on) return;
    const pg = easeOutCubic(ramp(t, 46.0, 46.55));
    const glow = $("s10-glow");
    glow.style.opacity = pg;
    glow.style.transform = `scale(${mix(0.9, 1.05, pg)})`;

    const pl = ramp(t, 46.0, 46.55);
    const logo = $("s10-logo");
    logo.style.opacity = easeOutCubic(pl);
    logo.style.transform = `translate3d(0,${mix(26, 0, easeOutExpo(pl))}px,0) scale(${mix(0.84, 1, easeOutExpo(pl))})`;
    logo.style.filter = `blur(${mix(12, 0, easeOutCubic(pl))}px)`;

    const sh = ramp(t, 47.7, 48.5);
    const sheen = $("s10-sheen");
    sheen.style.backgroundPosition = `${mix(160, -60, easeInOutCubic(sh))}% 0`;
    sheen.style.opacity = sh > 0 && sh < 1 ? 0.6 : 0;

    const pw = ramp(t, 46.35, 47.0);
    $("s10-word").style.opacity = easeOutCubic(ramp(t, 46.35, 46.85));
    $("s10-word").style.transform = `translate3d(0,${mix(20, 0, easeOutExpo(pw))}px,0)`;

    const pline = easeOutCubic(ramp(t, 46.95, 47.55));
    const line = $("s10-line");
    line.style.opacity = pline;
    line.style.transform = `translate3d(0,${mix(12, 0, pline)}px,0)`;

    const pu = easeOutCubic(ramp(t, 47.35, 47.95));
    const url = $("s10-url");
    url.style.opacity = pu;
    url.style.transform = `translateX(-50%) translateY(${mix(14, 0, pu)}px)`;

    const pm = easeOutCubic(ramp(t, 47.75, 48.35));
    const micro = $("s10-micro");
    micro.style.opacity = pm * 0.9;
    micro.style.transform = `translate3d(0,${mix(8, 0, pm)}px,0)`;

    els.s10.style.transform = `scale(${mix(1.03, 1, ramp(t, 46, 50))})`;
  }

  window.renderAt = function renderAt(t) {
    renderGlobal(t);
    renderS1(t); renderS2(t); renderS3(t); renderS4(t); renderS5(t);
    renderS6(t); renderSFile(t); renderS7(t); renderS8(t); renderS9(t); renderS10(t);
    document.documentElement.setAttribute("data-t", t.toFixed(3));
  };

  window.PROMO_DURATION = T;

  const ready = () => {
    renderAt(0);
    window.PROMO_READY = true;
  };
  if (document.fonts && document.fonts.ready) {
    Promise.all([
      document.fonts.ready,
      ...Array.from(document.images).map((img) => (img.complete ? Promise.resolve() : new Promise((res) => {
        img.addEventListener("load", res, { once: true });
        img.addEventListener("error", res, { once: true });
      })))
    ]).then(ready);
  } else {
    window.addEventListener("load", ready);
  }
})();
