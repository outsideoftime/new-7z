/* 渲染器：Playwright 逐帧截图 → ffmpeg 编码
   用法：
     node promo/render.cjs --still=8.2        # 输出单帧检查
     node promo/render.cjs --fps=60           # 渲染整片
     node promo/render.cjs --fps=30 --out=xxx.mp4
*/
const path = require("path");
const fs = require("fs");
const http = require("http");
const { spawn } = require("child_process");
const { chromium } = require("playwright");

const ROOT = __dirname;
const OUT = path.join(ROOT, "out");
const MIME = { ".html": "text/html; charset=utf-8", ".css": "text/css; charset=utf-8", ".js": "text/javascript; charset=utf-8", ".png": "image/png", ".jpg": "image/jpeg", ".svg": "image/svg+xml" };

function serve() {
  return new Promise((resolve) => {
    const server = http.createServer((req, res) => {
      const rel = decodeURIComponent(req.url.split("?")[0]).replace(/^\/+/, "") || "index.html";
      const file = path.join(ROOT, rel);
      if (!file.startsWith(ROOT) || !fs.existsSync(file)) { res.writeHead(404); res.end(); return; }
      res.writeHead(200, { "content-type": MIME[path.extname(file)] || "application/octet-stream" });
      fs.createReadStream(file).pipe(res);
    });
    server.listen(0, "127.0.0.1", () => resolve(server));
  });
}

function arg(name, def) {
  const hit = process.argv.find((a) => a.startsWith(`--${name}=`));
  return hit ? hit.slice(name.length + 3) : def;
}

(async () => {
  fs.mkdirSync(OUT, { recursive: true });
  const fps = Number(arg("fps", 60));
  const still = arg("still", null);
  const cover = arg("cover", null);
  const quality = Number(arg("quality", 96));
  const lang = arg("lang", "zh");
  const duration = Number(arg("duration", 34));
  const outFile = path.resolve(OUT, arg("out", "new7z-promo-douyin-silent.mp4"));

  const server = await serve();
  const port = server.address().port;
  const browser = await chromium.launch({
    args: ["--force-color-profile=srgb", "--hide-scrollbars", "--disable-lcd-text"]
  });
  const page = await browser.newPage({
    viewport: cover ? { width: 1080, height: 1440 } : { width: 1080, height: 1920 },
    deviceScaleFactor: 1
  });
  page.on("console", (m) => { if (m.type() === "error") console.error("[page]", m.text()); });
  page.on("pageerror", (e) => console.error("[pageerror]", e.message));

  await page.goto(`http://127.0.0.1:${port}/${cover ? "cover.html" : "index.html"}?lang=${lang}`, { waitUntil: "load" });

  if (cover) {
    await page.evaluate(async () => {
      await document.fonts.ready;
      await Promise.all(Array.from(document.images).map((img) => (img.complete ? 0 : new Promise((r) => { img.onload = img.onerror = r; }))));
    });
    const file = path.resolve(OUT, cover === "true" ? `cover-douyin-1080x1440${lang === "en" ? "-en" : ""}.png` : cover);
    await page.screenshot({ path: file });
    console.log("saved", file);
    await browser.close();
    server.close();
    return;
  }

  await page.waitForFunction("window.PROMO_READY === true", null, { timeout: 30000 });

  if (still !== null) {
    const t = Number(still);
    await page.evaluate((tt) => window.renderAt(tt), t);
    const file = path.join(OUT, `still-${t.toFixed(2)}.png`);
    await page.screenshot({ path: file });
    console.log("saved", file);
    await browser.close();
    server.close();
    return;
  }

  const total = Math.round(fps * duration);
  console.log(`rendering ${total} frames @ ${fps}fps -> ${outFile}`);

  const ff = spawn("ffmpeg", [
    "-y", "-loglevel", "error",
    "-f", "image2pipe", "-framerate", String(fps), "-vcodec", "mjpeg", "-i", "-",
    "-c:v", "libx264", "-preset", "slow", "-crf", "16",
    "-pix_fmt", "yuv420p", "-r", String(fps), "-movflags", "+faststart",
    outFile
  ], { stdio: ["pipe", "inherit", "inherit"] });

  const write = (buf) => new Promise((resolve, reject) => {
    if (ff.stdin.write(buf)) return resolve();
    ff.stdin.once("drain", resolve);
    ff.once("error", reject);
  });

  const t0 = Date.now();
  for (let i = 0; i < total; i++) {
    const t = i / fps;
    await page.evaluate((tt) => window.renderAt(tt), t);
    const buf = await page.screenshot({ type: "jpeg", quality });
    await write(buf);
    if (i % 60 === 0 || i === total - 1) {
      const el = (Date.now() - t0) / 1000;
      const pct = ((i + 1) / total * 100).toFixed(1);
      console.log(`  ${i + 1}/${total} (${pct}%)  ${el.toFixed(1)}s`);
    }
  }
  ff.stdin.end();
  await new Promise((res, rej) => ff.on("close", (c) => (c === 0 ? res() : rej(new Error("ffmpeg exit " + c)))));
  await browser.close();
  server.close();
  console.log("done", outFile);
})().catch((e) => { console.error(e); process.exit(1); });
