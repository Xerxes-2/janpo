/**
 * 渲染器那几个文件的**摘要**（票 43）：渲染版本号的后半截从这里来。
 *
 * ## 为什么要它
 *
 * 票 31 的渲染版本号是「模板 id + 模板内容的哈希」，它认得出「换了模板」，
 * **认不出「换了渲染器代码」**——而改一句 `prompt.ts` 里的排版同样换掉了前缀的字节、
 * 同样废掉全部 provider 缓存。裁决 29b-A 要的是「算得出来、漏不掉」，那时只兑现了一半。
 *
 * ## 渲染器＝从 `prompt.ts` 出发、沿**值导入**可达的那些文件
 *
 * 不是一份手写名单（手写的名单在有人把函数拆进新文件时会静静漏掉），也不是整个
 * `src/agent/` 或整个 bundle（那样改一句 `loop.ts` 的重试逻辑都会换一个版本号，
 * 版本号天天跳等于没有）。走查从根出发跟着 import 走：
 *
 * - `import type … from "./types.ts"` 这类**纯类型导入不跟**：类型编译后就没了，
 *   改它排不出一个不同的字节；
 * - 拆出新文件、把措辞挪进另一个模块，**自动跟着进来**；
 * - `loop.ts` / `retry.ts` / `render-version.ts` 反着依赖这一层，走查够不到它们。
 *
 * 认不出来的语句**当场抛**（多行 import 没接完、动态 `import()`、相对路径解析不到）：
 * 一个漏读的文件就是一个静默的口子，而这份摘要的全部价值就是不静默。
 *
 * ## 它只在 node 侧算
 *
 * 浏览器里跑的是生成出来的那个常量（`src/agent/renderer-digest.ts`），**不是这段代码**：
 * 于是浏览器、`node --test`、离线脚本（`print-prompt.mjs`）与 CI 读到的是同一个字面量，
 * 不存在「构建期算一份、跑脚本时算另一份」。生成物会不会过期由
 * `tests/agent/render-version.test.ts` 的新鲜度闸门看着。
 *
 * 重算：`pnpm run render-digest`（这份文件带 `--write` 直接跑）。
 */

import { readFileSync, writeFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { fnv1a } from "../src/agent/hash.ts";

/** 走查的根：真发出去的那两条消息就是它排出来的。 */
export const RENDERER_ROOT = "src/agent/prompt.ts";

/** 生成物落在哪。**它自己不在走查里**（没人从 `prompt.ts` 走得到它）。 */
const GENERATED = "src/agent/renderer-digest.ts";

/** 读一份源码。路径相对 `web/`，注入它就能在内存里试「改了这个文件会怎样」。 */
export type ReadSource = (path: string) => string;

/**
 * 从 `web/` 起算的那一份源码。**换行一律按 LF 算**：
 * 谁在 CRLF 的工作区上 checkout 出同一份代码，也该算出同一个摘要。
 */
export const readSource: ReadSource = (path) =>
  readFileSync(new URL(`../${path}`, import.meta.url), "utf8").replaceAll("\r\n", "\n");

/**
 * 一条 import / export 语句里的模块说明符；不是模块语句（`export function …`）就是 null。
 *
 * `type` 那一支返回 null 是**判断不是遗漏**：纯类型导入编译后一个字节都不剩。
 * 反过来，看着像模块语句却抽不出说明符的，**抛**：那正是会静静漏掉一个文件的情形。
 */
function specifierOf(statement: string, path: string): string | null {
  if (/^(?:import|export)\s+type\b/.test(statement)) return null;
  // `import "./x.ts"`（只求副作用）与 `… from "./x.ts"` 都算；
  // `export const … = "…"` 这种声明两条都不中，它不是模块语句。
  if (!/\bfrom\s*["']/.test(statement) && !/^import\s*["']/.test(statement)) return null;

  const quoted = statement.match(/["']([^"']+)["']/);
  if (quoted === null) {
    throw new Error(`${path}: 这条 import 抽不出它引的是哪个文件：${statement}`);
  }
  return quoted[1];
}

/** 这一行开了个多行 import / export 的头（`import {` 之后换行接名字）。 */
function opensClause(line: string): boolean {
  return /^(?:import|export)\s+(?:type\s+)?\{[^}]*$/.test(line);
}

/**
 * 一份源码里的模块语句，多行的接成一条。
 *
 * **接不完就抛**：宁可把走查弄红，也不要静静少读一个文件。
 */
function moduleStatements(source: string, path: string): string[] {
  const statements: string[] = [];
  let pending: string | null = null;

  for (const raw of source.split("\n")) {
    const line = raw.trim();

    if (pending !== null) {
      pending = `${pending} ${line}`;
      if (!line.includes("}")) continue;
      if (!/\bfrom\s*["'][^"']+["']\s*;?$/.test(pending)) {
        throw new Error(`${path}: 这条 import 走查读不懂，摘要不敢往下算：${pending}`);
      }
      statements.push(pending);
      pending = null;
      continue;
    }

    if (opensClause(line)) {
      pending = line;
      continue;
    }
    if (/^(?:import|export)\b/.test(line)) statements.push(line);
  }

  if (pending !== null) throw new Error(`${path}: 有一条 import 没接完：${pending}`);
  return statements;
}

/** 相对说明符 → 相对 `web/` 的路径。`./` 与 `../` 都走得通，其余（裸包名）不跟。 */
function resolveFrom(path: string, specifier: string): string | null {
  if (!specifier.startsWith("./") && !specifier.startsWith("../")) return null;

  const segments = path.split("/").slice(0, -1);
  for (const step of specifier.split("/")) {
    if (step === "." || step === "") continue;
    if (step === "..") segments.pop();
    else segments.push(step);
  }
  return segments.join("/");
}

/**
 * 渲染器是哪几个文件：从 `RENDERER_ROOT` 出发沿值导入走一遍，按路径排序。
 *
 * 排序是为了**同样的一组文件恒是同一段字节**——走查的先后顺序不该进版本号。
 */
export function rendererFiles(read: ReadSource = readSource): string[] {
  const seen = new Set<string>();
  walk(RENDERER_ROOT, read, seen);
  return [...seen].sort();
}

/** 从一个文件往下走。读不到的相对路径由 `read` 自己抛（ENOENT 就是一声报错）。 */
function walk(path: string, read: ReadSource, seen: Set<string>): void {
  if (seen.has(path)) return;
  seen.add(path);

  const source = read(path);
  if (/\bimport\s*\(\s*["']\.{1,2}\//.test(source)) {
    throw new Error(`${path}: 动态 import 不在走查覆盖内，摘要会漏掉它指的那个文件`);
  }

  for (const statement of moduleStatements(source, path)) {
    const specifier = specifierOf(statement, path);
    if (specifier === null) continue;

    const target = resolveFrom(path, specifier);
    if (target !== null && target !== GENERATED) walk(target, read, seen);
  }
}

/**
 * 渲染器那几个文件的摘要，8 位十六进制。
 *
 * 哈希与模板那一半共用 `fnv1a`（**同一份实现，只有一处**）：要求同样是
 * 「改一个字就换一个值、跨机器稳定」，不是密码学用途。**路径也进哈希**：
 * 把一个文件的内容整份挪到另一个文件里，摘要照样得变。
 */
export function rendererDigest(read: ReadSource = readSource): string {
  const canonical = rendererFiles(read)
    .map((path) => `${path}\u0000${read(path)}`)
    .join("\u0001");

  return fnv1a(canonical);
}

/** 生成物的正文。**列出覆盖了哪几个文件**：读的人不必再去跑一遍走查。 */
function generated(files: string[], digest: string): string {
  return [
    "/**",
    " * **生成物，别手改**（票 43）：渲染器那几个文件的摘要，渲染版本号的后半截。",
    " *",
    " * 重算：`pnpm run render-digest`。改了渲染器却忘了重算，",
    " * `tests/agent/render-version.test.ts` 的新鲜度闸门当场红。",
    " *",
    " * 覆盖的文件（从 `src/agent/prompt.ts` 出发、沿值导入可达的那些）：",
    ...files.map((path) => ` * - \`${path}\``),
    " */",
    `export const RENDERER_DIGEST = "${digest}";`,
    "",
  ].join("\n");
}

// `--write` 直接跑这份文件时重算生成物；被 import 时什么也不做。
if (process.argv[1] === fileURLToPath(import.meta.url)) {
  const files = rendererFiles();
  const digest = rendererDigest();

  if (process.argv.includes("--write")) {
    writeFileSync(new URL(`../${GENERATED}`, import.meta.url), generated(files, digest));
    console.log(`${GENERATED} 重算好了：${digest}`);
  } else {
    console.log(`渲染器摘要 ${digest}\n${files.map((path) => `  ${path}`).join("\n")}`);
  }
}
