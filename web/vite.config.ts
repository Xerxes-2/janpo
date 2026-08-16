import { defineConfig } from "vite";

// base "./" —— `pnpm build` 的产物要能被静态托管在任意子路径下，不假设部署在域名根。
// target es2022 —— Fable 的输出用了 class fields 与 optional chaining，别让 esbuild 降级。
export default defineConfig({
  base: "./",
  build: { target: "es2022", outDir: "dist" },
  server: { port: 5173 },
  preview: { port: 4173 },
});
