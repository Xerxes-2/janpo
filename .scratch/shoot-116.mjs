// 票 116 的看图用（临时量具，不接闸门）。从 web/ 跑：node ../.scratch/shoot-116.mjs
import { openLane } from "/home/xerxes2/janpo/web/scripts/browser-lane.mjs";
const OUT = "/home/xerxes2/janpo/.scratch/proto/shots";
const lane = await openLane();
const url = await lane.previewUrl();
for (const [name, open] of [["116-shut", false], ["116-open", true]]) {
  const page = await lane.newPage({ viewport: { width: 1280, height: 800 } });
  await page.goto(`${url}/?table=1`, { waitUntil: "load" });
  await page.getByTestId("table-board").waitFor();
  if (open) await page.getByTestId("table-setup-summary").click();
  const m = await page.evaluate(() => {
    const at = (t) => { const n = document.querySelector(`[data-testid="${t}"]`);
      return n ? Math.round(n.getBoundingClientRect().top) : null; };
    const d = document.querySelector('[data-testid="table-setup"]');
    return { open: d.open, setupH: Math.round(d.getBoundingClientRect().height),
      board: at("table-board"), ops: at("table-ops"),
      digest: document.querySelector('[data-testid="table-setup-digest"]').textContent.trim(),
      docH: Math.round(document.documentElement.scrollHeight) };
  });
  await page.screenshot({ path: `${OUT}/${name}.png` });
  console.log(`${name}: open=${m.open} 配桌高=${m.setupH} 操作条顶=${m.ops} 牌桌顶=${m.board} 页面高=${m.docH}（${(m.docH/800).toFixed(2)} 屏）`);
  console.log(`  摘要行「${m.digest}」`);
  await page.close();
}
await lane.close();
