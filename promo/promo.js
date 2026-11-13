/* New7z · 抖音竖屏宣传片 —— 确定性时间轴渲染（中 / 英）
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
  const T = 34.0;

  /* ---------- 文案（中 / 英） ---------- */
  const I18N = {
    zh: {
      "hook.l1": "一个压缩软件",
      "hook.l2": "能有多讲究？",
      "brand.tag": "为 Windows 11 而生",
      "brand.micro": "WINUI 3 · 完整 7-Zip 引擎",
      "s3.title": "像系统本身一样自然",
      "s3.sub": "WinUI 3 · 深色 / 浅色 · 双语界面",
      "s3.callout": "原生 Fluent 控件",
      "s4.title": "不解压，先看一眼",
      "s4.sub": "图片、文本、源码，包内直接预览",
      "s4.foot": "压缩包内 · 只读 · 源码预览",
      "s5.title": "八种格式，一个入口",
      "s5.note": "创建 7z / ZIP / TAR · 其余格式读取与解压",
      "s9.title": "图标，也能自定义",
      "s9.sub": "品牌图标与八种格式图标，换成你的 PNG",
      "s9.stock": "默认",
      "s9.custom": "自定义",
      "s9.brand": "品牌图标",
      "s9.note": "任意 PNG · 立即生效",
      "s9.pill": "随时恢复默认",
      "s6.title": "右键，就够了",
      "s6.note": "资源管理器 · 现代菜单",
      "menu.open": "用 New7z 打开",
      "menu.extractTo": "解压到“项目素材\\”",
      "menu.extractDots": "解压到…",
      "menu.extractHere": "解压到当前文件夹",
      "menu.test": "测试完整性",
      "s7.title": "小，但完整",
      "s7.r1": "安装包 · 含完整 7-Zip 引擎",
      "s7.r2": "额外运行库 · 完全离线可用",
      "s7.r3": "管理员权限要求",
      "s7.r4": "开源许可 · 免费商用",
      "outro.line": "开源免费 · 为 Windows 11 而生"
    },
    en: {
      "hook.l1": "A file archiver",
      "hook.l2": "can look this good?",
      "brand.tag": "Built for Windows 11",
      "brand.micro": "WINUI 3 · FULL 7-ZIP ENGINE",
      "s3.title": "Native by design",
      "s3.sub": "WinUI 3 · Dark / Light · Bilingual",
      "s3.callout": "Native Fluent controls",
      "s4.title": "Peek inside",
      "s4.sub": "Images, text and source — without extracting",
      "s4.foot": "Inside archive · read-only · source preview",
      "s5.title": "Eight formats, one app",
      "s5.note": "Create 7z / ZIP / TAR · read the rest",
      "s9.title": "Make it yours",
      "s9.sub": "Replace brand and format icons with your own PNG",
      "s9.stock": "Default",
      "s9.custom": "Custom",
      "s9.brand": "Brand icon",
      "s9.note": "Any PNG · applied instantly",
      "s9.pill": "Restore anytime",
      "s6.title": "Right-click. Done.",
      "s6.note": "Explorer · modern context menu",
      "menu.open": "Open with New7z",
      "menu.extractTo": "Extract to “Project files\\”",
      "menu.extractDots": "Extract to…",
      "menu.extractHere": "Extract here",
      "menu.test": "Test archive",
      "s7.title": "Small. Complete.",
      "s7.r1": "Installer · full 7-Zip engine",
      "s7.r2": "Extra runtimes · fully offline",
      "s7.r3": "Admin rights required",
      "s7.r4": "Open source · free to use",
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
  if (LANG === "en") $("s1-kicker").textContent = "NEW7Z · WINDOWS 11";

  /* ---------- build format grid ---------- */
  const FORMATS = [
    ["7z", "7Z"], ["zip", "ZIP"], ["rar", "RAR"], ["tar", "TAR"],
    ["gz", "GZ"], ["xz", "XZ"], ["bz2", "BZ2"], ["iso", "ISO"]
  ];
  const grid = $("s5-grid");
  FORMATS.forEach(([file, label]) => {
    const d = document.createElement("div");
    d.className = "fmt";
    d.innerHTML = `<img src="assets/formats/${file}.png" alt="${label}"><span>${label}</span>`;
    grid.appendChild(d);
  });

  /* ---------- menu highlight bars ---------- */
  document.querySelectorAll(".menu-item").forEach((el) => {
    const hl = document.createElement("div");
    hl.className = "hl";
    el.prepend(hl);
  });

  const els = {
    hud: $("hud"), progress: $("hud-progress"), chapterIndex: $("chapter-index"), chapterName: $("chapter-name"),
    bg: $("bg"), rings: $("rings"), sweep: $("sweep"),
    a1: document.querySelector(".a1"), a2: document.querySelector(".a2"), a3: document.querySelector(".a3"),
    b1: document.querySelector(".b1"), b2: document.querySelector(".b2"),
    s1: $("s1"), s2: $("s2"), s3: $("s3"), s4: $("s4"), s5: $("s5"),
    s9: $("s9"), s6: $("s6"), s7: $("s7"), s8: $("s8")
  };

  const SCENES = [
    { t0: 0, t1: 2, name: "HOOK" },
    { t0: 2, t1: 6, name: "BRAND" },
    { t0: 6, t1: 10, name: "INTERFACE" },
    { t0: 10, t1: 14, name: "PREVIEW" },
    { t0: 14, t1: 18, name: "FORMATS" },
    { t0: 18, t1: 22, name: "ICONS" },
    { t0: 22, t1: 26, name: "CONTEXT" },
    { t0: 26, t1: 30, name: "SPECS" },
    { t0: 30, t1: 34, name: "OUTRO" }
  ];
  const CUTS = [2, 6, 10, 14, 18, 22, 26, 30];

  function gate(el, t, t0, t1, inDur = 0.62, outDur = 0.40) {
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

  /* ---------- global ---------- */
  function renderGlobal(t) {
    // 背景缓慢漂移
    els.a1.style.transform = `translate3d(${Math.sin(t * 0.21) * 60}px,${Math.cos(t * 0.17) * 50}px,0)`;
    els.a2.style.transform = `translate3d(${Math.cos(t * 0.16) * 70}px,${Math.sin(t * 0.23) * 60}px,0)`;
    els.a3.style.transform = `translate3d(${Math.sin(t * 0.13 + 1) * 80}px,${Math.cos(t * 0.19) * 40}px,0)`;
    els.b1.style.opacity = 0.16 + 0.12 * (0.5 + 0.5 * Math.sin(t * 0.6));
    els.b2.style.opacity = 0.10 + 0.10 * (0.5 + 0.5 * Math.sin(t * 0.5 + 2));

    // HUD
    const hudOn = ramp(t, 0.7, 1.5) * (1 - ramp(t, 33.2, 34));
    els.hud.style.opacity = easeOutCubic(hudOn);
    els.progress.style.width = (888 * clamp01(t / T)) + "px";

    // 章节标签
    let idx = 0;
    for (let i = 0; i < SCENES.length; i++) if (t >= SCENES[i].t0) idx = i;
    els.chapterIndex.textContent = String(idx + 1).padStart(2, "0");
    els.chapterName.textContent = SCENES[idx].name;

    // 转场扫光
    let sweepOp = 0, sweepX = 0;
    for (const c of CUTS) {
      const p = (t - (c - 0.18)) / 0.72;
      if (p > 0 && p < 1) {
        sweepOp = Math.sin(p * Math.PI) * 0.55;
        sweepX = mix(-1500, 1500, easeInOutCubic(p));
      }
    }
    els.sweep.style.opacity = sweepOp;
    els.sweep.style.transform = `translate3d(${sweepX}px,0,0)`;
  }

  /* ---------- 01 hook ---------- */
  function renderS1(t) {
    const on = gate(els.s1, t, 0, 2, 0.01, 0.45);
    if (!on) return;
    const k = easeOutCubic(ramp(t, -0.25, 0.5));
    const k1 = easeOutExpo(ramp(t, -0.3, 0.72));
    const k2 = easeOutExpo(ramp(t, 0.12, 1.05));
    const kr = easeOutCubic(ramp(t, 0.62, 1.2));
    const km = easeOutCubic(ramp(t, 0.8, 1.5));

    const kicker = $("s1-kicker");
    kicker.style.opacity = 0.55 + 0.45 * k;
    kicker.style.transform = `translate3d(0,${mix(14, 0, k)}px,0)`;

    const l1 = $("s1-l1").firstElementChild;
    l1.style.transform = `translate3d(0,${mix(105, 0, k1)}%,0)`;

    const l2wrap = $("s1-l2");
    l2wrap.firstElementChild.style.transform = `translate3d(0,${mix(112, 0, k2)}%,0) scale(${mix(1.05, 1, k2)})`;

    const rule = $("s1-rule");
    rule.style.width = (160 * kr) + "px";
    rule.style.opacity = kr;
    rule.style.left = `calc(50% - ${80 * kr}px)`;

    const micro = $("s1-micro");
    micro.style.opacity = km * 0.9;
    micro.style.top = `${1066 + mix(12, 0, km)}px`;
  }

  /* ---------- 02 brand ---------- */
  function renderS2(t) {
    const on = gate(els.s2, t, 2, 6, 0.42, 0.4);
    if (!on) return;
    const ring = $("s2-ring");
    const pr = easeOutCubic(ramp(t, 2.0, 2.6));
    ring.style.opacity = pr * 0.9;
    ring.querySelector("svg").style.transform = `rotate(${(t - 2) * 7}deg)`;

    const halo = $("s2-halo");
    halo.style.opacity = pr * (0.7 + 0.3 * Math.sin((t - 2) * 1.4));

    const pl = ramp(t, 2.0, 2.6);
    const logo = $("s2-logo");
    logo.style.opacity = easeOutCubic(pl);
    logo.style.transform =
      `translate3d(0,${mix(46, 0, easeOutExpo(pl)) + Math.sin((t - 2) * 1.5) * 5}px,0)` +
      ` scale(${mix(0.78, 1, easeOutExpo(pl))})`;
    logo.style.filter = `blur(${mix(18, 0, easeOutCubic(pl))}px)`;

    const sh = ramp(t, 2.9, 3.65);
    const sheen = $("s2-sheen");
    sheen.style.backgroundPosition = `${mix(160, -60, easeInOutCubic(sh))}% 0`;
    sheen.style.opacity = sh > 0 && sh < 1 ? 0.6 : 0;

    const pw = ramp(t, 2.3, 3.05);
    $("s2-word").firstElementChild.style.transform = `translate3d(0,${mix(115, 0, easeOutExpo(pw))}%,0)`;

    const pc = easeOutCubic(ramp(t, 2.85, 3.5));
    const cn = $("s2-cn");
    cn.style.opacity = pc;
    cn.style.transform = `translate3d(0,${mix(10, 0, pc)}px,0)`;

    const pt = easeOutCubic(ramp(t, 3.1, 3.85));
    const tag = $("s2-tag");
    tag.style.opacity = pt;
    tag.style.transform = `translate3d(0,${mix(12, 0, pt)}px,0)`;

    const pm = easeOutCubic(ramp(t, 3.5, 4.15));
    const micro = $("s2-micro");
    micro.style.opacity = pm * 0.85;
    micro.style.transform = `translate3d(0,${mix(8, 0, pm)}px,0)`;

    // 整体轻微呼吸
    els.s2.style.transform = `scale(${mix(1, 1.015, ramp(t, 2, 6))})`;
  }

  /* ---------- 03 interface ---------- */
  function renderS3(t) {
    const on = gate(els.s3, t, 6, 10, 0.42, 0.36);
    if (!on) return;
    const head = $("s3-head");
    const ph = easeOutCubic(ramp(t, 6.05, 6.6));
    head.style.opacity = ph;
    head.style.transform = `translate3d(0,${mix(28, 0, ph)}px,0)`;

    const pw = ramp(t, 6.05, 7.5);
    const win = $("s3-win");
    const e = easeOutExpo(pw);
    win.style.transform =
      `rotateX(${mix(24, 5, easeOutCubic(pw))}deg) rotateY(${mix(-13, -2, e)}deg)` +
      ` translate3d(0,${mix(90, 0, e)}px,0) scale(${mix(0.86, 1, e) + 0.012 * ramp(t, 7.5, 10)})`;
    win.style.opacity = easeOutCubic(ramp(t, 6.05, 6.8));

    $("s3-pool").style.opacity = easeOutCubic(ramp(t, 6.2, 7.4)) * 0.9;

    // 主页 → 新建压缩包：短促切换，避免两张界面叠影
    const pswap = easeOutCubic(ramp(t, 8.82, 9.02));
    const alt = $("s3-alt");
    alt.style.opacity = pswap;
    alt.style.transform = `scale(${mix(1.03, 1, pswap)})`;

    const pc = easeOutCubic(ramp(t, 7.5, 8.15));
    const callout = $("s3-callout");
    callout.style.opacity = pc;
    callout.style.transform = `translate3d(${mix(-24, 0, pc)}px,0,0)`;
    callout.querySelector(".leader").style.width = (90 * pc) + "px";
    callout.querySelector("b").style.opacity = easeOutCubic(ramp(t, 7.85, 8.35));

    els.s3.style.transform = `scale(${mix(1, 1.01, ramp(t, 6, 10))})`;
  }

  /* ---------- 04 preview ---------- */
  function renderS4(t) {
    const on = gate(els.s4, t, 10, 14, 0.42, 0.36);
    if (!on) return;
    const head = $("s4-head");
    const ph = easeOutCubic(ramp(t, 10.05, 10.6));
    head.style.opacity = ph;
    head.style.transform = `translate3d(0,${mix(28, 0, ph)}px,0)`;

    const pw = ramp(t, 10.05, 11.4);
    const win = $("s4-win");
    win.style.transform =
      `rotateX(${mix(15, 5, easeOutCubic(pw))}deg) rotateY(${mix(10, 2, easeOutExpo(pw))}deg)` +
      ` translate3d(0,${mix(70, 0, easeOutExpo(pw)) - ramp(t, 11.4, 14) * 10}px,0) scale(${mix(0.9, 1, easeOutExpo(pw))})`;
    win.style.opacity = easeOutCubic(ramp(t, 10.05, 10.9));

    const pk = ramp(t, 10.9, 11.85);
    const card = $("s4-card");
    card.style.opacity = easeOutCubic(ramp(t, 10.9, 11.4));
    card.style.transform =
      `rotate(${mix(-5, -2.4, easeOutCubic(pk))}deg) translate3d(0,${mix(90, 0, easeOutBack(pk)) + Math.sin((t - 11) * 1.7) * 5}px,0)` +
      ` scale(${mix(0.9, 1, easeOutBack(pk))})`;

    els.s4.style.transform = `scale(${mix(1, 1.012, ramp(t, 10, 14))})`;
  }

  /* ---------- 05 formats ---------- */
  function renderS5(t) {
    const on = gate(els.s5, t, 14, 18, 0.4, 0.34);
    if (!on) return;
    const head = $("s5-head");
    const ph = easeOutCubic(ramp(t, 14.05, 14.6));
    head.style.opacity = ph;
    head.style.transform = `translate3d(0,${mix(26, 0, ph)}px,0)`;

    const pg = ramp(t, 14.1, 15.2);
    const ghost = $("s5-ghost");
    ghost.style.opacity = easeOutCubic(pg) * 0.9;
    ghost.style.transform = `translate3d(${mix(50, 0, easeOutCubic(pg))}px,0,0)`;

    grid.querySelectorAll(".fmt").forEach((el, i) => {
      const start = 14.35 + i * 0.075;
      const p = ramp(t, start, start + 0.62);
      const e = easeOutBack(p);
      el.style.opacity = easeOutCubic(ramp(t, start, start + 0.3));
      el.style.transform =
        `perspective(700px) rotateY(${mix(-55, 0, easeOutExpo(p))}deg)` +
        ` translate3d(0,${mix(26, 0, easeOutCubic(p)) + Math.sin((t - start) * 1.8 + i) * 3}px,0)` +
        ` scale(${mix(0.6, 1, e)})`;
    });

    const pl = easeOutCubic(ramp(t, 15.35, 16.05));
    const line = $("s5-line");
    line.style.opacity = pl;
    line.style.transform = `translate3d(0,${mix(10, 0, pl)}px,0)`;

    const pn = easeOutCubic(ramp(t, 15.75, 16.45));
    const note = $("s5-note");
    note.style.opacity = pn * 0.95;
    note.style.transform = `translate3d(0,${mix(10, 0, pn)}px,0)`;

    els.s5.style.transform = `scale(${mix(1, 1.01, ramp(t, 14, 18))})`;
  }

  /* ---------- 06 icons（自定义图标） ---------- */
  const HUES = [-120, -70, 80];
  function renderS9(t) {
    const on = gate(els.s9, t, 18, 22, 0.4, 0.34);
    if (!on) return;
    const head = $("s9-head");
    const ph = easeOutCubic(ramp(t, 18.05, 18.6));
    head.style.opacity = ph;
    head.style.transform = `translate3d(0,${mix(26, 0, ph)}px,0)`;

    $("s9-cards").querySelectorAll(".icon-card").forEach((card, i) => {
      const st = 18.3 + i * 0.12;
      const p = ramp(t, st, st + 0.65);
      card.style.opacity = easeOutCubic(ramp(t, st, st + 0.4));
      card.style.transform =
        `translate3d(0,${mix(40, 0, easeOutExpo(p)) + Math.sin((t - st) * 1.6 + i) * 3}px,0)` +
        ` scale(${mix(0.94, 1, easeOutBack(p))})`;

      const fs = 19.15 + i * 0.32;
      const fp = easeInOutCubic(ramp(t, fs, fs + 0.55));
      const flip = card.querySelector(".flip");
      flip.style.setProperty("--hue", HUES[i] + "deg");
      flip.style.transform = `rotateY(${180 * fp}deg)`;

      const chip = card.querySelector(".chip");
      chip.querySelector(".stock").style.opacity = 1 - ramp(fp, 0.32, 0.5);
      chip.querySelector(".custom").style.opacity = ramp(fp, 0.5, 0.72);
    });

    const pn = easeOutCubic(ramp(t, 20.45, 21.15));
    const note = $("s9-note");
    note.style.opacity = pn;
    note.style.transform = `translate3d(0,${mix(10, 0, pn)}px,0)`;

    const pp = easeOutCubic(ramp(t, 20.75, 21.4));
    const pill = $("s9-pill");
    pill.style.opacity = pp * 0.9;
    pill.style.transform = `translateX(-50%) translateY(${mix(12, 0, pp)}px)`;

    els.s9.style.transform = `scale(${mix(1, 1.008, ramp(t, 18, 22))})`;
  }

  /* ---------- 07 context menu ---------- */
  function renderS6(t) {
    const on = gate(els.s6, t, 22, 26, 0.4, 0.34);
    if (!on) return;
    const head = $("s6-head");
    const ph = easeOutCubic(ramp(t, 22.05, 22.6));
    head.style.opacity = ph;
    head.style.transform = `translate3d(0,${mix(26, 0, ph)}px,0)`;

    const pm = ramp(t, 22.3, 23.15);
    const menu = $("s6-menu");
    menu.style.opacity = easeOutCubic(ramp(t, 22.3, 22.95));
    menu.style.transform = `translate3d(0,${mix(44, 0, easeOutExpo(pm))}px,0) scale(${mix(0.95, 1, easeOutExpo(pm))})`;

    menu.querySelectorAll(".menu-item").forEach((el, i) => {
      const p = ramp(t, 22.55 + i * 0.075, 23.1 + i * 0.075);
      el.style.opacity = easeOutCubic(p);
      el.style.transform = `translate3d(${mix(-16, 0, easeOutCubic(p))}px,0,0)`;
    });

    // 光标入场并选中「解压到“项目素材\”」
    const pc = ramp(t, 23.35, 24.15);
    const cursor = $("s6-cursor");
    cursor.style.opacity = easeOutCubic(ramp(t, 23.35, 23.6));
    const cx = mix(660, 392, easeInOutCubic(pc));
    const cy = mix(600, 214, easeInOutCubic(pc));
    cursor.style.transform = `translate3d(${cx}px,${cy}px,0) scale(${mix(1.1, 1, pc)})`;

    const hl = menu.querySelectorAll(".hl")[1];
    const phl = ramp(t, 24.05, 24.35) * (1 - 0.35 * ramp(t, 25.2, 25.8));
    hl.style.opacity = phl;

    // 点击涟漪：高亮短暂加深
    const press = Math.sin(clamp01((t - 24.18) / 0.28) * Math.PI);
    hl.style.boxShadow = `inset 0 0 0 1px rgba(120,180,255,${0.22 + 0.4 * press})`;

    const pn = easeOutCubic(ramp(t, 24.5, 25.2));
    const note = $("s6-note");
    note.style.opacity = pn * 0.9;
    note.style.transform = `translate3d(0,${mix(10, 0, pn)}px,0)`;

    els.s6.style.transform = `scale(${mix(1, 1.008, ramp(t, 22, 26))})`;
  }

  /* ---------- 08 specs ---------- */
  function renderS7(t) {
    const on = gate(els.s7, t, 26, 30, 0.4, 0.34);
    if (!on) return;
    const head = $("s7-head");
    const ph = easeOutCubic(ramp(t, 26.05, 26.6));
    head.style.opacity = ph;
    head.style.transform = `translate3d(0,${mix(26, 0, ph)}px,0)`;

    $("s7-list").querySelectorAll(".spec-row").forEach((el, i) => {
      const start = 26.45 + i * 0.14;
      const p = ramp(t, start, start + 0.7);
      el.style.opacity = easeOutCubic(ramp(t, start, start + 0.45));
      el.style.transform = `translate3d(${mix(-36, 0, easeOutExpo(p))}px,0,0)`;
    });

    els.s7.style.transform = `scale(${mix(1, 1.008, ramp(t, 26, 30))})`;
  }

  /* ---------- 09 outro ---------- */
  function renderS8(t) {
    const on = gate(els.s8, t, 30, 34, 0.4, 0.01);
    if (!on) return;
    const pg = easeOutCubic(ramp(t, 30.0, 30.55));
    const glow = $("s8-glow");
    glow.style.opacity = pg;
    glow.style.transform = `scale(${mix(0.9, 1.05, pg)})`;

    const pl = ramp(t, 30.0, 30.55);
    const logo = $("s8-logo");
    logo.style.opacity = easeOutCubic(pl);
    logo.style.transform = `translate3d(0,${mix(26, 0, easeOutExpo(pl))}px,0) scale(${mix(0.84, 1, easeOutExpo(pl))})`;
    logo.style.filter = `blur(${mix(12, 0, easeOutCubic(pl))}px)`;

    const sh = ramp(t, 31.7, 32.5);
    const sheen = $("s8-sheen");
    sheen.style.backgroundPosition = `${mix(160, -60, easeInOutCubic(sh))}% 0`;
    sheen.style.opacity = sh > 0 && sh < 1 ? 0.6 : 0;

    const pw = ramp(t, 30.35, 31.0);
    $("s8-word").style.opacity = easeOutCubic(ramp(t, 30.35, 30.85));
    $("s8-word").style.transform = `translate3d(0,${mix(20, 0, easeOutExpo(pw))}px,0)`;

    const pline = easeOutCubic(ramp(t, 30.95, 31.55));
    const line = $("s8-line");
    line.style.opacity = pline;
    line.style.transform = `translate3d(0,${mix(12, 0, pline)}px,0)`;

    const pu = easeOutCubic(ramp(t, 31.35, 31.95));
    const url = $("s8-url");
    url.style.opacity = pu;
    url.style.transform = `translateX(-50%) translateY(${mix(14, 0, pu)}px)`;

    const pm = easeOutCubic(ramp(t, 31.75, 32.35));
    const micro = $("s8-micro");
    micro.style.opacity = pm * 0.9;
    micro.style.transform = `translate3d(0,${mix(8, 0, pm)}px,0)`;

    els.s8.style.transform = `scale(${mix(1.03, 1, ramp(t, 30, 34))})`;
  }

  /* ---------- master ---------- */
  window.renderAt = function renderAt(t) {
    renderGlobal(t);
    renderS1(t); renderS2(t); renderS3(t); renderS4(t);
    renderS5(t); renderS9(t); renderS6(t); renderS7(t); renderS8(t);
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
