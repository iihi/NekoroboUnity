// 上の帯（新しい並び）の絵を PNG にする。HTML版 index.html の H2_ICONS・H2_COIN・H2_WRENCH をそのまま描く。
//   node --experimental-websocket hud_icons.mjs <HTML版の index.html> <出力フォルダ>
// アイテムは 24×24 の絵を 64×64 に、金貨とスパナは 17×15 を 68×60 にする。地は透明。
import { spawn } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import os from "node:os";

const [src, outDir] = process.argv.slice(2);
const html = fs.readFileSync(src, "utf8");
// const H2_ICONS = { ... }; を取り出して評価する（中身は文字列の足し算だけ）
const grab = name => {
  const a = html.indexOf("const " + name + " =");
  if (a < 0) throw new Error(name + " が見つからない");
  const m = /;\r?\n/g; m.lastIndex = a; const b = m.exec(html).index;      // 改行は CRLF のこともある
  return Function('"use strict";' + html.slice(a, b + 1).replace("const " + name + " =", "return"))();
};
const icons = grab("H2_ICONS"), coin = grab("H2_COIN"), wrench = grab("H2_WRENCH");
const cells = [];
let x = 0;
const parts = ['<!doctype html><html><head><meta charset="utf-8"><style>',
  'html,body{margin:0;background:transparent}.c{position:absolute;top:0}.c svg{display:block;width:100%;height:100%}',
  '</style></head><body>'];
for (const k of Object.keys(icons)) {
  parts.push(`<div class="c" style="left:${x}px;width:64px;height:64px"><svg viewBox="0 0 24 24" fill="#1d2433">${icons[k]}</svg></div>`);
  cells.push(["h2_" + k, x, 64, 64]); x += 68;
}
for (const [k, s] of [["h2_coin", coin], ["h2_wrench", wrench]]) {
  parts.push(`<div class="c" style="left:${x}px;width:68px;height:60px">${s}</div>`);
  cells.push([k, x, 68, 60]); x += 72;
}
parts.push("</body></html>");
const tmp = path.join(os.tmpdir(), "nk_hud_icons.html");
fs.writeFileSync(tmp, parts.join("\n"));

const CHROME = "C:/Program Files/Google/Chrome/Application/chrome.exe";
const port = 9336, prof = path.join(os.tmpdir(), "nk_hud_icons_prof");
const ch = spawn(CHROME, ["--headless=new", "--remote-debugging-port=" + port, "--user-data-dir=" + prof, "about:blank"], { stdio: "ignore" });
const sleep = ms => new Promise(r => setTimeout(r, ms));
let target = null;
for (let i = 0; i < 100 && !target; i++) {
  await sleep(200);
  try { target = (await (await fetch(`http://127.0.0.1:${port}/json`)).json()).find(t => t.type === "page"); } catch {}
}
if (!target) { console.error("chrome が起きない"); ch.kill(); process.exit(1); }
const ws = new WebSocket(target.webSocketDebuggerUrl);
await new Promise(r => ws.addEventListener("open", r));
let id = 0; const wait = new Map();
ws.addEventListener("message", e => { const m = JSON.parse(e.data); if (m.id && wait.has(m.id)) { wait.get(m.id)(m); wait.delete(m.id); } });
const send = (method, params = {}) => new Promise(r => { const i = ++id; wait.set(i, r); ws.send(JSON.stringify({ id: i, method, params })); });
await send("Page.enable");
await send("Emulation.setDeviceMetricsOverride", { width: x, height: 64, deviceScaleFactor: 1, mobile: false });
await send("Emulation.setDefaultBackgroundColorOverride", { color: { r: 0, g: 0, b: 0, a: 0 } });
await send("Page.navigate", { url: "file:///" + tmp.replace(/\\/g, "/") });
await sleep(1500);
fs.mkdirSync(outDir, { recursive: true });
for (const [k, cx, w, h] of cells) {
  const r = await send("Page.captureScreenshot", { format: "png", clip: { x: cx, y: 0, width: w, height: h, scale: 1 } });
  fs.writeFileSync(path.join(outDir, k + ".png"), Buffer.from(r.result.data, "base64"));
  console.log(k);
}
ws.close(); ch.kill();
process.exit(0);
