// 无头验收共用的「起一个页面服务器」（票 29b 顺手清的那笔债）。
//
// **默认不指定端口**：vite 因此从它自己的默认端口起往后找第一个空闲的（`strictPort: false`），
// 真实端口从 `server.resolvedUrls` 读回来——脚本自己不再拼 URL。此前四个脚本各写死一个端口
// （4179–4182）且 `strictPort: true`：两个工作区同时跑 `ci.sh` 必撞，而报错长得像用例挂了
// ——调度器 2026-08-16 集成票 30 时被它误导过一次（第一遍红、第二遍绿，实际是另一个工作区
// 在同时跑 CI）。跑批是并行的，这种假红迟早会掩盖一次真失败。
//
// 想要固定端口（调试时想用一个稳定的 URL）就 `JANPO_PORT=4179 node scripts/verify-tracer.mjs`；
// 那时才 `strictPort`，撞了当场说清是端口的事。

import { createServer, preview } from "vite";

/** 显式指定的端口；没指定就是 undefined（交给 vite 自己找一个空闲的）。 */
function wantedPort() {
  const raw = process.env.JANPO_PORT;
  if (raw === undefined || raw.trim() === "") return undefined;

  const port = Number.parseInt(raw, 10);
  if (Number.isNaN(port) || port < 1 || port > 65535) {
    throw new Error(`JANPO_PORT 要一个端口号，得到「${raw}」`);
  }
  return port;
}

/**
 * 端口那几项。**没指定就一个字都不写**（vite 自己往后找空的）；
 * 指定了才 `strictPort`——那时人是真要这个端口，撞了就该当场说清楚。
 */
function portOptions(port) {
  return port === undefined ? {} : { port, strictPort: true };
}

/**
 * 撞端口时说清楚它是端口的事，别让它看着像用例挂了。
 *
 * 两种形态都认：Node 抛的 `EADDRINUSE`，与 vite 自己包过一层的
 * “Port 4180 is already in use”（`strictPort` 那条路上抛的是后者，没有 `code`）。
 */
function explain(error, port) {
  const inUse = error?.code === "EADDRINUSE" || /already in use/i.test(String(error?.message));
  if (!inUse) return error;
  return new Error(
    `端口 ${port} 被占用（多半是另一个工作区在同时跑 CI）。` +
      "不设 JANPO_PORT 就让 vite 自己挑一个空闲端口——这不是用例失败。",
  );
}

/**
 * 服务器真正监听在哪个地址。**必须从这里读**：端口是内核给的，脚本自己拼不出来。
 * 末尾的斜杠去掉，调用方一律 `${pageUrl(server)}/`。
 */
export function pageUrl(server) {
  const local = server.resolvedUrls?.local?.[0];
  if (local === undefined) throw new Error("vite 没报出它监听在哪个地址");
  return local.replace(/\/$/, "");
}

/**
 * **主持人那一页**（票 71）。`origin` 是 `pageUrl(server)` 或 `lane.previewUrl()`。
 *
 * `/` 从此是首页：Demo Paifu **自动播**，且没有配桌与模型面板。
 * 因此**要点、要读牌桌的闸门一律开这一页**：它默认暂停（`Playback.initial`），
 * 是最安静的一页；留在 `/` 上会被自动播放与资产拉取干扰。
 *
 * 只有两道例外，各有各的理由：`verify-tracer` 开 `/`（它量的就是「首页里没有
 * 开发向内容」）与 `verify-home`（它量的就是首页本身）。
 */
export function hostPage(origin) {
  return `${origin}/?table=1`;
}

/** `vite preview`：托管打包后的 `dist/`（曳光弹与 LLM 座位那两道走它）。 */
export async function startPreview(root, options = {}) {
  const port = wantedPort();
  try {
    return await preview({
      root,
      preview: { ...portOptions(port), ...options },
      logLevel: "warn",
    });
  } catch (error) {
    throw explain(error, port);
  }
}

/**
 * `vite dev`：托管**源码形态**的 Fable 输出（黄金用例与牌谱导出那两道走它——
 * 页面里要点名 `import` 某个模块，而 `dist/` 里文件名带哈希）。
 */
export async function startDevServer(root, options = {}) {
  const port = wantedPort();
  const server = await createServer({
    root,
    server: { ...portOptions(port), ...options },
    logLevel: "warn",
  });

  try {
    await server.listen();
  } catch (error) {
    await server.close();
    throw explain(error, port);
  }

  return server;
}

/**
 * dev server 头一次加载时，vite 可能因为**依赖预打包**而让页面整体重载
 * （playwright 报 “Execution context was destroyed”）。它与用例对错无关，
 * 却长得像一次失败——重试一次，还不行才真的抛。
 *
 * 实测撞见过一次：`pnpm run fable` 重新生成输出之后的第一次 `verify-golden`。
 */
export async function retryOnReload(action) {
  try {
    return await action();
  } catch (error) {
    if (!String(error?.message).includes("Execution context was destroyed")) throw error;
    console.warn("（页面被 vite 重载了一次——依赖预打包，与用例无关；重试）");
    return await action();
  }
}
