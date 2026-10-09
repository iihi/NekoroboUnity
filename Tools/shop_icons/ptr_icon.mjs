// 指カーソルの絵を PNG にする。HTML版 index.html の FINGER_SVG をそのまま描く。
//   node --experimental-websocket ptr_icon.mjs <HTML版の index.html> <出力フォルダ>
// ptr.png     … スティックで動かす指（#ptr）。影つき、2倍の細かさ。44×52 の枠の (4,2) に指を置く → 指先は枠の (13,5)
// ptr_cur.png … マウスのカーソル。32×32 に収める（指は 0.8 倍、指先は (7,2)）。影なし
import { spawn } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import os from "node:os";

const [src, outDir] = process.argv.slice(2);
const html = fs.readFileSync(src, "utf8");
const grab = name => {
  const a = html.indexOf("const " + name + " =");
  if (a < 0) throw new Error(name + " が見つからない");
  const m = /;\r?\n/g; m.lastIndex = a; const b = m.exec(html).index;
  return Function('"use strict";' + html.slice(a, b + 1).replace("const " + name + " =", "return (").replace(/;\s*$/, ");"))();   // 次の行から始まっても返るように括弧で包む
};
const svg = grab("FINGER_SVG");
const parts = ['<!doctype html><html><head><meta charset="utf-8"><style>',
  'html,body{margin:0;background:transparent}.c{position:absolute;top:0}',
  '</style></head><body>',
  // 2倍で描くので、枠も中身も2倍
  `<div class="c" style="left:0;width:88px;height:104px"><div style="position:absolute;left:8px;top:4px;width:64px;height:80px;filter:drop-shadow(0 4px 6px rgba(0,0,0,.55))">${svg.replace('width="32" height="40"', 'width="64" height="80"')}</div></div>`,
  `<div class="c" style="left:100px;width:32px;height:32px"><div style="position:absolute;left:0;top:0">${svg.replace('width="32" height="40"', 'width="25.6" height="32"')}</div></div>`,
  "</body></html>"];
const cells = [["ptr", 0, 88, 104], ["ptr_cur", 100, 32, 32]];
const x = 140;
const tmp = path.join(os.tmpdir(), "nk_ptr_icon.html");
fs.writeFileSync(tmp, parts.join("\n"));

const CHROME = "C:/Program Files/Google/Chrome/Application/chrome.exe";
const port = 9337, prof = path.join(os.tmpdir(), "nk_ptr_icon_prof");
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
await send("Emulation.setDeviceMetricsOverride", { width: x, height: 110, deviceScaleFactor: 1, mobile: false });
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
