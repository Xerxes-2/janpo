/**
 * Demo Paifu 的取用（票 71）：**首页那份随应用一起分发的牌谱**（ADR-0003）。
 *
 * F# 侧的对应物是 `src/Janpo.Web/Demo.fs`（ADR-0005：跨界只有「F# 调 TS」一个方向，
 * 且只传字符串）。**这一层不认识牌谱**——进来出去都只是文本，牌谱的形状全在引擎那边
 * （`Paifu.decoder`）；这里只管「去哪儿拿」与「拿不到时说人话」这两件事。
 *
 * **它是 `fetch` 拿的，不打进 JS bundle**：主人事后会把这份资产换成真 LLM 对局的那一份
 * （票 79），那一份带 thinking（实测约 10 KB/手），打进 bundle 会把首屏拖死。
 * 换资产 = 换 `web/public/demo-paifu.json` 这一个文件，代码一行都不动。
 */

/**
 * 资产在站点里的相对位置。`web/public/` 下的东西 Vite 原样拷进 `dist/`，
 * 因此它就是站点根下的一个文件。
 */
const DEMO_FILE = "demo-paifu.json";

/**
 * **按 `document.baseURI` 解析而不是写死斜杠开头**：站点部署在子路径下
 * （GitHub Pages 是 `/janpo/`，由 `JANPO_BASE` 注入，见 `vite.config.ts`），
 * 写死 `/demo-paifu.json` 在那里会 404。`baseURI` 就是这一页自己的地址，
 * 相对解析出来的必然与页面同一层——`?table=1`、`#载荷` 都不影响它。
 */
function demoUrl(): string {
  return new URL(DEMO_FILE, document.baseURI).toString();
}

/** 出错时给人看的那一句里，把浏览器的原话截短——它是诊断，不是给机器解析的。 */
function detail(error: unknown): string {
  const message = error instanceof Error ? `${error.name}: ${error.message}` : String(error);
  const flat = message.trim().replace(/\s+/g, " ");
  return flat.length > 80 ? `${flat.slice(0, 80)}…` : flat;
}

/**
 * 拉那份 Demo 牌谱的**原文**。**回的是一个信封 JSON**，与 `payload.ts` / `decide.ts` 同一种做法：
 * `{"text":"…"}` 或者 `{"error":"Demo 牌谱拉不到：…"}`。
 *
 * **它不抛，也不静静地回空手**：首页拉不到资产时页面上要说得出一句人话
 * （白屏是最坏的那一种）。F# 那侧靠「Demo 牌谱拉不到：」这个前缀分得清是**没拿到文件**
 * 还是**拿到了但读不动**（后者是 `Paifu.decoder` 的诊断，措辞在 `Demo.paifu`）。
 */
export async function loadDemoPaifu(): Promise<string> {
  const failure = (why: string) => JSON.stringify({ error: `Demo 牌谱拉不到：${why}` });
  const url = demoUrl();

  let response: Response;
  try {
    response = await fetch(url);
  } catch (error) {
    return failure(`请求 ${url} 时出错（${detail(error)}）`);
  }

  if (!response.ok) return failure(`${url} 回了 HTTP ${response.status}`);

  try {
    return JSON.stringify({ text: await response.text() });
  } catch (error) {
    return failure(`${url} 的正文读不下来（${detail(error)}）`);
  }
}
