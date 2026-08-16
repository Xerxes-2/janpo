import { defineConfig } from "vite";

// base 默认 "./" —— `pnpm build` 的产物要能被静态托管在任意子路径下，不假设部署在域名根；
// 无头验收脚本（`scripts/verify-*.mjs`、`scripts/shoot-table.mjs`）也都按这个默认起 vite。
//
// **要部署到一个固定子路径时用 `JANPO_BASE` 覆盖它**，例如 GitHub Pages：
//   JANPO_BASE=/janpo/ pnpm run build
// 那个值由 `.github/workflows/pages.yml` 注入（仓库改名就只改那一行）。
// dev / preview 也读同一个环境变量，因此本地能原样复现 Pages 上的路径。
const base = process.env.JANPO_BASE?.trim() || "./";

// target es2022 —— Fable 的输出用了 class fields 与 optional chaining，别让 esbuild 降级。
export default defineConfig({
  base,
  build: { target: "es2022", outDir: "dist" },
  server: { port: 5173 },
  preview: { port: 4173 },
});
