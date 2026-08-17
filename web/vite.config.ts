import { defineConfig, type Plugin } from "vite";
import { rendererDigest } from "./scripts/renderer-digest.ts";
import { RENDERER_DIGEST } from "./src/agent/renderer-digest.ts";

// base 默认 "./" —— `pnpm build` 的产物要能被静态托管在任意子路径下，不假设部署在域名根；
// 无头验收脚本（`scripts/verify-*.mjs`、`scripts/shoot-table.mjs`）也都按这个默认起 vite。
//
// **要部署到一个固定子路径时用 `JANPO_BASE` 覆盖它**，例如 GitHub Pages：
//   JANPO_BASE=/janpo/ pnpm run build
// 那个值由 `.github/workflows/pages.yml` 注入（仓库改名就只改那一行）。
// dev / preview 也读同一个环境变量，因此本地能原样复现 Pages 上的路径。
const base = process.env.JANPO_BASE?.trim() || "./";

/**
 * 打包之前核一遍渲染器摘要新不新鲜（票 43）。
 *
 * 用例那边已经有一道同样的闸门，这里再拦一次是因为**发出去的是这份产物**：
 * 有人改了 `prompt.ts` 就直接 `pnpm run build`，编进 bundle 的那个常量就会落后一个版本
 * ——牙读牌谱的人会把两个不同的渲染器当成同一个。**假的自动归因比没有归因更坏**，
 * 因此宁可构建当场失败。
 */
function freshRendererDigest(): Plugin {
  return {
    name: "janpo-renderer-digest",
    buildStart() {
      const fresh = rendererDigest();
      if (fresh !== RENDERER_DIGEST) {
        throw new Error(
          `渲染器的源码变了（现在是 ${fresh}）而 src/agent/renderer-digest.ts 还写着 ${RENDERER_DIGEST}：` +
            "跑 `pnpm run render-digest` 重算，否则牌谱里的渲染版本号会指错人。",
        );
      }
    },
  };
}

// target es2022 —— Fable 的输出用了 class fields 与 optional chaining，别让 esbuild 降级。
export default defineConfig({
  base,
  plugins: [freshRendererDigest()],
  build: { target: "es2022", outDir: "dist" },
  server: { port: 5173 },
  preview: { port: 4173 },
});
