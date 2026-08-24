// 强 AI 基线那份产物**在 JS 那几个脚本里的唯一一处路径**（票 106）。
//
// 从前 `verify-baseline.mjs`、`verify-review.mjs` 与 `probe/akagi-wasm/candidates-shape.mjs`
// 各写一份，注释里互相叮嘱「与 `web/src/baseline/wasm.ts` 的 `ASSET_FILE` 逐字相同」
// ——**那句叮嘱没有执行体**（判据 2）。现在这三处读这一份，
// 而这一份与另外几处（浏览器侧的 `wasm.ts`、造它的 `build-baseline-wasm.sh`、
// 核发布件的 `check-pages-dist.sh`、Pages 那条流水线的 `BASELINE_WASM`）
// 由 `scripts/check-single-source.sh` 逐处按字面对齐。
//
// **为什么这几处收不成一处**：它们跨三种语言、三个加载时机——浏览器里的那份要打进 bundle
// （读不到仓库里的文件），bash 那两份要能在没有 node 的时候单跑，YAML 那份是 workflow
// 解析期就要的值。收法只剩「让 gate 把它们钉在一起」，钉法就是那道闸门。

import { existsSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const webRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");

/** 那份产物在**站点里**的相对地址（`web/public/` 下的东西 Vite 原样拷进 `dist/`）。 */
export const ASSET = "baseline/janpo-baseline.wasm";

/** 那份产物在**仓库里**的位置。造它的是 `scripts/build-baseline-wasm.sh`。 */
export const PUBLIC_ASSET = resolve(webRoot, "public", ASSET);

/** 本机到底有没有那份产物（它不入版本控制，ADR-0006 边界 6）。 */
export const assetPresent = () => existsSync(PUBLIC_ASSET);
