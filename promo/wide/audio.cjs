/* 程序化配乐（横屏 50 秒版）：120 BPM / A 小调氛围电子，48kHz 立体声
   输出 promo/out/score-wide.wav（无版权音乐，可商用）
   与抖音版共用同一套音色与和声，段落延长并重新对齐 10 个章节。 */
const fs = require("fs");
const path = require("path");

const SR = 48000;
const DUR = 55;
const N = SR * DUR;
const BPM = 120;
const BEAT = 60 / BPM;          // 0.5s
const BAR = BEAT * 4;           // 2s

const L = new Float32Array(N);
const R = new Float32Array(N);

const midi = (m) => 440 * Math.pow(2, (m - 69) / 12);
const clamp = (x, a, b) => Math.min(b, Math.max(a, x));
const rand = (() => { let s = 20261113; return () => { s = (s * 1664525 + 1013904223) >>> 0; return s / 4294967296; }; })();

function add(buf, i, v) { if (i >= 0 && i < N) buf[i] += v; }
function pan(gain, p) { const a = (p + 1) * Math.PI / 4; return [gain * Math.cos(a), gain * Math.sin(a)]; }

/* ---------- voices ---------- */
function padNote(t0, dur, m, amp, p = 0) {
  const f = midi(m);
  const [gl, gr] = pan(amp, p);
  const n = Math.floor(dur * SR);
  const start = Math.floor(t0 * SR);
  const atk = 0.55 * SR, rel = 0.9 * SR;
  for (let i = 0; i < n; i++) {
    const t = i / SR;
    let env = 1;
    if (i < atk) env = i / atk;
    if (i > n - rel) env *= clamp((n - i) / rel, 0, 1);
    env = Math.pow(env, 1.4);
    const lfo = 1 + 0.08 * Math.sin(2 * Math.PI * 0.11 * (t0 + t) + m);
    let s = 0;
    for (let h = 1; h <= 5; h++) {
      const a = [1, 0.42, 0.2, 0.1, 0.05][h - 1];
      s += a * Math.sin(2 * Math.PI * f * h * t * (1 + 0.0009 * h) + h);
      s += a * 0.6 * Math.sin(2 * Math.PI * f * h * t * (1 - 0.0011 * h) + 2 + h);
    }
    const v = s * env * lfo;
    add(L, start + i, v * gl);
    add(R, start + i, v * gr);
  }
}

function pluck(t0, m, amp, p = 0, decay = 0.16, bright = 1) {
  const f = midi(m);
  const [gl, gr] = pan(amp, p);
  const n = Math.floor((decay * 4) * SR);
  const start = Math.floor(t0 * SR);
  for (let i = 0; i < n; i++) {
    const t = i / SR;
    const env = Math.exp(-t / decay) * (1 - Math.exp(-t / 0.004));
    const s = Math.sin(2 * Math.PI * f * t)
      + 0.35 * bright * Math.sin(4 * Math.PI * f * t)
      + 0.12 * bright * Math.sin(6 * Math.PI * f * t);
    const v = s * env;
    add(L, start + i, v * gl);
    add(R, start + i, v * gr);
  }
}

function bass(t0, m, amp, dur = 0.42) {
  const f = midi(m);
  const n = Math.floor(dur * SR);
  const start = Math.floor(t0 * SR);
  for (let i = 0; i < n; i++) {
    const t = i / SR;
    const env = Math.exp(-t / (dur * 0.55)) * (1 - Math.exp(-t / 0.006));
    const s = Math.sin(2 * Math.PI * f * t) + 0.22 * Math.sin(4 * Math.PI * f * t);
    const v = s * env * amp;
    add(L, start + i, v);
    add(R, start + i, v);
  }
}

function kick(t0, amp = 0.55) {
  const start = Math.floor(t0 * SR);
  const n = Math.floor(0.42 * SR);
  for (let i = 0; i < n; i++) {
    const t = i / SR;
    const f = 118 - 74 * (1 - Math.exp(-t / 0.045));
    const env = Math.exp(-t / 0.16) * (1 - Math.exp(-t / 0.002));
    const click = Math.exp(-t / 0.006) * 0.35;
    const v = (Math.sin(2 * Math.PI * f * t) * env + click * (rand() * 2 - 1)) * amp;
    add(L, start + i, v);
    add(R, start + i, v);
  }
}

function hat(t0, amp = 0.12, open = false) {
  const start = Math.floor(t0 * SR);
  const dur = open ? 0.14 : 0.045;
  const n = Math.floor(dur * SR);
  let prev = 0, prev2 = 0;
  for (let i = 0; i < n; i++) {
    const t = i / SR;
    const white = rand() * 2 - 1;
    const hp = white - 2 * prev + prev2;
    prev2 = prev; prev = white;
    const env = Math.exp(-t / (open ? 0.055 : 0.012)) * (1 - Math.exp(-t / 0.001));
    const v = hp * env * amp;
    add(L, start + i, v * 0.9);
    add(R, start + i, v * 0.7);
  }
}

function clap(t0, amp = 0.22) {
  const start = Math.floor(t0 * SR);
  const n = Math.floor(0.24 * SR);
  for (let i = 0; i < n; i++) {
    const t = i / SR;
    const burst = (x) => Math.exp(-x / 0.011);
    const env = burst(t) + burst(Math.abs(t - 0.012)) * 0.8 + burst(Math.abs(t - 0.024)) * 0.6 + Math.exp(-t / 0.09) * 0.35;
    const v = (rand() * 2 - 1) * env * amp;
    add(L, start + i, v * 0.95);
    add(R, start + i, v * 0.85);
  }
}

function riser(t0, dur, amp = 0.16) {
  const start = Math.floor(t0 * SR);
  const n = Math.floor(dur * SR);
  let lp = 0;
  for (let i = 0; i < n; i++) {
    const p = i / n;
    const t = i / SR;
    const white = rand() * 2 - 1;
    const cutoff = 0.02 + 0.5 * p * p;
    lp += cutoff * (white - lp);
    const env = Math.pow(p, 2.2) * (1 - Math.exp(-t / 0.05));
    const v = lp * env * amp;
    add(L, start + i, v * (1 - 0.4 * p));
    add(R, start + i, v * (0.6 + 0.4 * p));
  }
}

function impact(t0, amp = 0.5) {
  const start = Math.floor(t0 * SR);
  const n = Math.floor(1.2 * SR);
  for (let i = 0; i < n; i++) {
    const t = i / SR;
    const f = 62 - 30 * (1 - Math.exp(-t / 0.09));
    const body = Math.sin(2 * Math.PI * f * t) * Math.exp(-t / 0.42);
    const air = (rand() * 2 - 1) * Math.exp(-t / 0.22) * 0.18;
    const v = (body + air) * amp * (1 - Math.exp(-t / 0.003));
    add(L, start + i, v);
    add(R, start + i, v);
  }
}

/* ---------- 混响（Freeverb 简化版） ---------- */
function reverb(input, wet = 0.32) {
  const combs = [0.0297, 0.0371, 0.0411, 0.0437].map((d) => Math.floor(d * SR));
  const aps = [0.005, 0.0017].map((d) => Math.floor(d * SR));
  const out = new Float32Array(N);
  const combBufs = combs.map((len) => ({ buf: new Float32Array(len), idx: 0, len }));
  const apBufs = aps.map((len) => ({ buf: new Float32Array(len), idx: 0, len }));
  for (let i = 0; i < N; i++) {
    const x = input[i];
    let s = 0;
    for (const c of combBufs) {
      const y = c.buf[c.idx];
      c.buf[c.idx] = x * 0.25 + y * 0.78;
      c.idx = (c.idx + 1) % c.len;
      s += y;
    }
    s *= 0.25;
    for (const a of apBufs) {
      const y = a.buf[a.idx];
      a.buf[a.idx] = s + y * 0.5;
      a.idx = (a.idx + 1) % a.len;
      s = y - s;
    }
    out[i] = s;
  }
  return out;
}

/* ---------- 编曲 ---------- */
const CHORDS = [
  { pad: [57, 60, 64, 67, 71], root: 33 }, // Am9
  { pad: [53, 57, 60, 64, 69], root: 29 }, // Fmaj7
  { pad: [55, 59, 62, 64, 67], root: 36 }, // Cmaj9
  { pad: [55, 59, 62, 64, 67], root: 31 }  // G6
];
const chordAt = (bar) => CHORDS[bar % 4];

// 段落：[全奏区间]；36–41 安全段留白，只保留 Pad 与琶音
const GROOVE = [[7, 41], [46, 51]];
const inGroove = (t) => GROOVE.some(([a, b]) => t >= a && t < b);
const FULL = [[7, 19], [24, 41], [46, 51]];
const inFull = (t) => FULL.some(([a, b]) => t >= a && t < b);

// 铺底 Pad：每小节一层
for (let bar = 0; bar < 27; bar++) {
  const t0 = bar * BAR;
  const c = chordAt(bar);
  const amp = bar < 2 ? 0.020 : bar < 25 ? 0.016 : 0.019;
  c.pad.forEach((m, i) => padNote(t0, BAR + 0.35, m, amp, ((i / (c.pad.length - 1)) * 2 - 1) * 0.7));
}

// 琶音：16 分音符
for (let t = 3.0; t < 54.0; t += BEAT / 4) {
  const bar = Math.floor(t / BAR);
  const c = chordAt(bar);
  const step = Math.round((t - bar * BAR) / (BEAT / 4));
  const pattern = [0, 2, 1, 3, 2, 4, 3, 1];
  const m = c.pad[pattern[step % 8]] + 12;
  const section = inFull(t) ? 1 : 0.55;
  const amp = (t < 7 ? 0.030 : 0.042) * section;
  pluck(t, m, amp, step % 2 ? 0.45 : -0.45, 0.15, inFull(t) ? 1.1 : 0.7);
}

// 低音：8 分音符
for (let t = 7.0; t < 51.0; t += BEAT / 2) {
  if (!inGroove(t)) continue;
  const bar = Math.floor(t / BAR);
  const c = chordAt(bar);
  const off = Math.round(((t - bar * BAR) / BEAT) * 2) % 2;
  bass(t, c.root + (off ? 12 : 0), off ? 0.13 : 0.19, 0.36);
}

// 鼓
for (let t = 7.0; t < 51.0; t += BEAT) {
  if (!inGroove(t)) continue;
  const beatInBar = Math.round((t % BAR) / BEAT);
  kick(t, beatInBar === 0 ? 0.6 : 0.48);
}
for (let t = 7.0; t < 51.0; t += BEAT / 2) {
  if (!inGroove(t)) continue;
  const off = Math.round((t / BEAT)) % 2 === 1;
  hat(t, off ? 0.085 : 0.045, false);
}
for (let t = 13.0; t < 19.0; t += BEAT) {
  const b = Math.round(((t % BAR) / BEAT));
  if (b === 1 || b === 3) clap(t, 0.2);
}
for (let t = 24.0; t < 41.0; t += BEAT) {
  const b = Math.round(((t % BAR) / BEAT));
  if (b === 1 || b === 3) clap(t, 0.22);
  else hat(t, 0.05, true);
}
for (let t = 46.0; t < 51.0; t += BEAT) {
  const b = Math.round(((t % BAR) / BEAT));
  if (b === 1 || b === 3) clap(t, 0.2);
}

// 章节重击与过渡
impact(3.0, 0.3);
impact(7.0, 0.34);
impact(19.0, 0.3);
impact(24.0, 0.3);
impact(29.0, 0.36);
impact(34.0, 0.32);
impact(41.0, 0.36);
impact(46.0, 0.3);
impact(51.0, 0.55);
riser(0.8, 1.6, 0.10);
riser(5.2, 1.6, 0.09);
riser(18.0, 0.9, 0.12);
riser(28.2, 0.8, 0.12);
riser(33.2, 0.8, 0.12);
riser(40.0, 1.0, 0.14);
riser(45.0, 1.0, 0.14);
riser(50.0, 1.0, 0.16);

// 结尾长音
[57, 64, 69, 72, 76].forEach((m, i) => padNote(51.0, 3.9, m, 0.026, (i / 4) * 1.2 - 0.6));

/* ---------- 效果与总线 ---------- */
function pingpong(src, delaySec, feedback, wet) {
  const d = Math.floor(delaySec * SR);
  const bufL = new Float32Array(d);
  const bufR = new Float32Array(d);
  let i = 0;
  const out = new Float32Array(N);
  for (let n = 0; n < N; n++) {
    const dl = bufL[i];
    const dr = bufR[i];
    bufL[i] = src[n] * 0.6 + dr * feedback;
    bufR[i] = dl * feedback;
    i = (i + 1) % d;
    out[n] = (dl + dr) * wet;
  }
  return out;
}
const sendL = new Float32Array(N);
const sendR = new Float32Array(N);
for (let i = 0; i < N; i++) { sendL[i] = L[i] * 0.55; sendR[i] = R[i] * 0.55; }
const dly = pingpong(sendL, BEAT * 0.75, 0.38, 0.30);
const revL = reverb(sendL, 0.3);
const revR = reverb(sendR, 0.3);

const duck = new Float32Array(N).fill(1);
for (let t = 7.0; t < 51.0; t += BEAT) {
  if (!inGroove(t)) continue;
  const s = Math.floor(t * SR);
  const len = Math.floor(0.32 * SR);
  for (let i = 0; i < len && s + i < N; i++) {
    const p = i / len;
    const g = 1 - 0.22 * Math.exp(-p * 5);
    duck[s + i] = Math.min(duck[s + i], g);
  }
}

let peak = 0;
for (let i = 0; i < N; i++) {
  const fadeIn = clamp(i / (0.05 * SR), 0, 1);
  const fadeOut = clamp((N - i) / (1.0 * SR), 0, 1);
  const g = fadeIn * fadeOut;
  let l = (L[i] * 0.82 + revL[i] * 0.5 + dly[i] * 0.9) * duck[i] * g;
  let r = (R[i] * 0.82 + revR[i] * 0.5 + dly[i] * 0.35) * duck[i] * g;
  l = Math.tanh(l * 1.05);
  r = Math.tanh(r * 1.05);
  L[i] = l; R[i] = r;
  peak = Math.max(peak, Math.abs(l), Math.abs(r));
}
const norm = 0.89 / (peak || 1);
console.log("peak before normalize", peak.toFixed(3));

const outPath = path.join(__dirname, "..", "out", "score-wide.wav");
const buf = Buffer.alloc(44 + N * 4);
buf.write("RIFF", 0); buf.writeUInt32LE(36 + N * 4, 4); buf.write("WAVE", 8);
buf.write("fmt ", 12); buf.writeUInt32LE(16, 16); buf.writeUInt16LE(1, 20); buf.writeUInt16LE(2, 22);
buf.writeUInt32LE(SR, 24); buf.writeUInt32LE(SR * 4, 28); buf.writeUInt16LE(4, 32); buf.writeUInt16LE(16, 34);
buf.write("data", 36); buf.writeUInt32LE(N * 4, 40);
let o = 44;
for (let i = 0; i < N; i++) {
  buf.writeInt16LE(Math.round(clamp(L[i] * norm, -1, 1) * 32767), o); o += 2;
  buf.writeInt16LE(Math.round(clamp(R[i] * norm, -1, 1) * 32767), o); o += 2;
}
fs.writeFileSync(outPath, buf);
console.log("wrote", outPath, (buf.length / 1048576).toFixed(1) + "MB");
