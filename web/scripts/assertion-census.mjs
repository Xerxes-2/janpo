// **断言执行次数普查（页面侧）**——票 113。与 `scripts/fsi/assertion-census.fsx` 是一对：
// 那一份数 dotnet 侧的断言，这一份数**跑在 node 里的那些闸门与用例**——
// `web/scripts/verify-*.mjs` 与 `web/tests/**/*.test.ts` 里，每一条断言各被求值了多少次。
//
// **量的是「断言被求值」，不是行覆盖率**。计数器是 **V8 的块级精确计数**
// （`NODE_V8_COVERAGE`，node 自带，零依赖）：它给每个代码块一个**执行次数**，
// 因此 `if (条件) problems.push(…)` 这一句里，**条件被求值了几次**是直接读得出来的。
//
// ## 三种断言点（`kind` 那一列）
//
//   * `gate`：`if (条件) …push(…)` / `return failure(…)` ——闸门的主力形状。
//     数的是**条件被求值的次数**（不是它红了几次；红了几次那是 `push` 那一块，绿的时候恒 0）。
//   * `assert`：`node:assert` 的一次调用（`web/tests/**/*.test.ts` 里那种）。
//   * `throw`：`throw new Error(…)` ——同样是失败支，零次是好消息。
//
// ## 两个已知的盲区（判据 4：抓不住的写出来）
//
//   1. **`page.evaluate(…)` 里的断言 node 侧数不到**：那段代码序列化进浏览器里跑，
//      在 node 的覆盖数据里它恒是「函数没被调用」。这份工具会把它们**单独一栏**列出来
//      （`in-browser`），**不混进零次那张表**——混进去就是一堆假零。
//   2. **shell 闸门（`scripts/check-*.sh`）不在这里**：它们是 grep 管道，
//      「一条断言」的口径与这里不是一回事，报告 §6 列了名单与理由。
//
// ## 跑法（在 web/ 下）
//
//   node scripts/assertion-census.mjs                  # 跑全套（含浏览器那条跑道，约 2 分钟）
//   node scripts/assertion-census.mjs --skip-browser   # 只跑 node --test 与语义不变量（约 40 秒）
//   node scripts/assertion-census.mjs --from /tmp/janpo-assertion-census/v8   # 拿现成的覆盖数据重算
//   node scripts/assertion-census.mjs --json /tmp/census-web.json             # 机器可读的全表
//
// **它不进 `ci.sh`**：理由与 dotnet 那一份同——它答的是「断言够不够硬」，
// 那是收尾时问一次的问题。账算在报告 `113-assertion-census.md` §7。

import { spawnSync } from "node:child_process";
import { existsSync, mkdirSync, readdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { stripTypeScriptTypes } from "node:module";
import { dirname, relative, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { parseAst } from "vite";

const webRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const repoRoot = resolve(webRoot, "..");

const args = process.argv.slice(2);

function option(name, fallback) {
  const at = args.indexOf(name);
  return at === -1 ? fallback : args[at + 1];
}

const skipBrowser = args.includes("--skip-browser");
const coverageRoot = option("--from", null) ?? resolve("/tmp/janpo-assertion-census/v8");
const reuse = args.includes("--from");
const jsonOut = option("--json", null);
const top = Number(option("--top", "20"));

/**
 * 一趟 `ci-web.sh` 里跑在 node 里的那几条命令。**这份名单就是「这一票量到了哪些」**：
 * 不在这里的（`biome ci`、`tsc --noEmit`、`vite build` 与那几道 shell 闸门）没量，理由见报告 §6。
 */
const entries = [
  { name: "node--test", command: ["--test", "tests/**/*.test.ts"] },
  { name: "verify-invariants", command: ["scripts/verify-invariants.mjs"] },
  { name: "verify-browser", command: ["scripts/verify-browser.mjs"], browser: true },
  { name: "verify-baseline", command: ["scripts/verify-baseline.mjs"], browser: true },
];

/** 扫这些源码找断言点。`verify-*.mjs` 全扫（**包括没被 CI 跑到的那几份**——那正是要找的东西）。 */
function sourceFiles() {
  const gates = readdirSync(resolve(webRoot, "scripts"))
    .filter((name) => name.startsWith("verify-") && name.endsWith(".mjs"))
    .map((name) => resolve(webRoot, "scripts", name));

  const tests = [];
  const walk = (directory) => {
    for (const entry of readdirSync(directory, { withFileTypes: true })) {
      const path = resolve(directory, entry.name);
      if (entry.isDirectory()) walk(path);
      else if (entry.name.endsWith(".ts") && !entry.name.endsWith(".d.ts")) tests.push(path);
    }
  };
  walk(resolve(webRoot, "tests"));

  return [...gates, ...tests].sort();
}

// ---- 一、跑起来，收 V8 的计数 ----

function measure() {
  rmSync(coverageRoot, { recursive: true, force: true });
  mkdirSync(coverageRoot, { recursive: true });

  for (const entry of entries) {
    if (entry.browser && skipBrowser) {
      console.log(`跳过 ${entry.name}（--skip-browser）`);
      continue;
    }

    const directory = resolve(coverageRoot, entry.name);
    mkdirSync(directory, { recursive: true });
    const started = Date.now();
    const result = spawnSync("node", entry.command, {
      cwd: webRoot,
      env: { ...process.env, NODE_V8_COVERAGE: directory },
      encoding: "utf8",
      maxBuffer: 256 * 1024 * 1024,
    });

    const seconds = ((Date.now() - started) / 1000).toFixed(1);
    if (result.status !== 0) {
      console.error(result.stdout?.slice(-4000) ?? "");
      console.error(result.stderr?.slice(-4000) ?? "");
      throw new Error(`${entry.name} 没跑绿（退出码 ${result.status}）：普查要的是一趟**绿**的数`);
    }
    console.log(`  ${entry.name.padEnd(18)} ${seconds.padStart(6)} 秒`);
  }
}

/**
 * 把一个目录下所有 V8 覆盖文件读进来，按源文件归拢成**一进程一份**的区间表。
 *
 * **按进程分开存是有讲究的**：同一份源码在两个进程里的块划分不一定一样
 * （没被调用过的函数 V8 只给一条函数级区间），混成一堆之后「最内层那一个」就选错了。
 * 分开存，各自取各自的最内层，再相加——**一趟 ci 里跑了几次就是几次**。
 */
function readCoverage(directory) {
  const perUrl = new Map();

  const walk = (at) => {
    for (const entry of readdirSync(at, { withFileTypes: true })) {
      const path = resolve(at, entry.name);
      if (entry.isDirectory()) {
        walk(path);
        continue;
      }
      if (!entry.name.endsWith(".json")) continue;

      const payload = JSON.parse(readFileSync(path, "utf8"));
      for (const script of payload.result ?? []) {
        if (!script.url.startsWith("file://")) continue;
        const file = fileURLToPath(script.url);
        if (!file.startsWith(webRoot)) continue;
        const ranges = script.functions.flatMap((each) => each.ranges);
        perUrl.set(file, [...(perUrl.get(file) ?? []), ranges]);
      }
    }
  };

  walk(directory);
  return perUrl;
}

/**
 * 某个偏移处被执行了几次：**每个进程各取包住它的最小那个区间**，再把各进程的数相加。
 * V8 的区间是嵌套的（函数一个、每个分支块一个），最内层那个才是这一句自己的次数。
 */
function hitsAt(rangeSets, offset) {
  let seen = false;
  let total = 0;

  for (const ranges of rangeSets) {
    let narrowest = null;
    for (const range of ranges) {
      if (range.startOffset > offset || offset >= range.endOffset) continue;
      const width = range.endOffset - range.startOffset;
      if (narrowest === null || width < narrowest.endOffset - narrowest.startOffset)
        narrowest = range;
    }

    if (narrowest !== null) {
      seen = true;
      total += narrowest.count;
    }
  }

  return seen ? total : null;
}

// ---- 二、静态认出断言点 ----

const ASSERT_NAMES = new Set([
  "push",
  "failure",
  "assert",
  "ok",
  "equal",
  "deepEqual",
  "strictEqual",
  "notEqual",
  "match",
  "fail",
  "throws",
]);

const EVALUATE_NAMES = new Set([
  "evaluate",
  "evaluateHandle",
  "$eval",
  "$$eval",
  "waitForFunction",
  "addInitScript",
  "exposeFunction",
]);

function callName(node) {
  if (node.type !== "CallExpression") return null;
  const target = node.callee;
  if (target.type === "Identifier") return target.name;
  if (target.type === "MemberExpression" && target.property.type === "Identifier")
    return target.property.name;
  return null;
}

/** 这一段是不是坐在 `page.evaluate(…)` 的实参里——那它是在浏览器里跑的，node 侧数不到。 */
function insideEvaluate(ancestors) {
  return ancestors.some(
    (each) => each.type === "CallExpression" && EVALUATE_NAMES.has(callName(each) ?? ""),
  );
}

/** 它坐在谁里头：最近的那个具名函数 / `test("…")` 的标题。 */
function enclosingName(ancestors) {
  for (let at = ancestors.length - 1; at >= 0; at -= 1) {
    const node = ancestors[at];
    if (node.type === "FunctionDeclaration" && node.id) return node.id.name;
    if (node.type === "VariableDeclarator" && node.id.type === "Identifier") return node.id.name;
    if (node.type === "CallExpression") {
      const name = callName(node);
      const [first] = node.arguments;
      const titled = name === "test" || name === "describe" || name === "it";
      if (titled && first?.type === "Literal" && typeof first.value === "string")
        return first.value;
    }
  }
  return "（顶层）";
}

/** 遍历一棵 ESTree（带祖先栈）。 */
function walk(node, ancestors, visit) {
  if (node === null || typeof node !== "object") return;

  if (Array.isArray(node)) {
    for (const each of node) walk(each, ancestors, visit);
    return;
  }

  if (typeof node.type !== "string") return;

  visit(node, ancestors);
  ancestors.push(node);
  for (const [key, value] of Object.entries(node)) {
    if (key === "type" || key === "start" || key === "end") continue;
    walk(value, ancestors, visit);
  }
  ancestors.pop();
}

function containsAssertCall(node) {
  let found = false;
  walk(node, [], (child) => {
    const name = callName(child);
    if (name !== null && ASSERT_NAMES.has(name)) found = true;
  });
  return found;
}

/** 源码的换行位置表（字符偏移 → 行号 / 那一行的原文）。 */
function lineIndex(text) {
  const starts = [0];
  for (let at = 0; at < text.length; at += 1) if (text[at] === "\n") starts.push(at + 1);

  const lineOf = (offset) => {
    let low = 0;
    let high = starts.length - 1;
    while (low < high) {
      const middle = Math.ceil((low + high) / 2);
      if (starts[middle] <= offset) low = middle;
      else high = middle - 1;
    }
    return low + 1;
  };

  const textOf = (offset) => {
    const line = lineOf(offset) - 1;
    const end = text.indexOf("\n", starts[line]);
    return text.slice(starts[line], end === -1 ? text.length : end).trim();
  };

  return { lineOf, textOf };
}

/** `.ts` 先过 node 自带的类型擦除（`strip` 模式把类型换成等长空白，**偏移量一个不差**）。 */
function parse(file) {
  const raw = readFileSync(file, "utf8");
  const text = file.endsWith(".ts") ? stripTypeScriptTypes(raw, { mode: "strip" }) : raw;
  return { raw, tree: parseAst(text, {}) };
}

/** 扫一份源码里的断言点。 */
function sitesIn(file) {
  const { raw, tree } = parse(file);
  const index = lineIndex(raw);
  const sites = [];

  const record = (offset, kind, ancestors) => {
    sites.push({
      file,
      offset,
      kind,
      line: index.lineOf(offset),
      owner: enclosingName(ancestors),
      inBrowser: insideEvaluate(ancestors),
      text: index.textOf(offset).slice(0, 120),
    });
  };

  walk(tree, [], (node, ancestors) => {
    if (node.type === "IfStatement" && containsAssertCall(node.consequent)) {
      // 判据点是**条件**：它被求值了几次才是「这条断言开了几次口」。
      record(node.test.start, "gate", ancestors);
      return;
    }

    if (node.type === "ThrowStatement") {
      record(node.start, "throw", ancestors);
      return;
    }

    // `if` 里那句 push 已经由上面那一条记过（记的是条件）；这里只收裸的 `assert.*`。
    const name = callName(node);
    const target = node.type === "CallExpression" ? node.callee : null;
    const isAssert =
      name !== null &&
      ASSERT_NAMES.has(name) &&
      target?.type === "MemberExpression" &&
      target.object.type === "Identifier" &&
      target.object.name === "assert";

    if (isAssert) record(node.start, "assert", ancestors);
  });

  return sites;
}

// ---- 三、报数 ----

if (reuse) {
  if (!existsSync(coverageRoot)) throw new Error(`${coverageRoot} 不在，先不带 --from 跑一次`);
  console.log(`拿 ${coverageRoot} 里现成的覆盖数据重算（没重跑闸门）`);
} else {
  console.log(`跑 ${entries.length - (skipBrowser ? 2 : 0)} 条命令，收 V8 的块级计数：`);
  measure();
}

const coverage = readCoverage(coverageRoot);
const files = sourceFiles();
const sites = files.flatMap(sitesIn);

const where = (site) => `${relative(repoRoot, site.file)}:${site.line}`;

const measured = sites.map((site) => {
  const ranges = coverage.get(site.file);
  const hits = ranges === undefined ? null : hitsAt(ranges, site.offset);
  return { ...site, loaded: ranges !== undefined, hits: hits ?? 0 };
});

const loadedFiles = files.filter((file) => coverage.has(file));
const coldFiles = files.filter((file) => !coverage.has(file));
const inBrowser = measured.filter((site) => site.inBrowser);
const counted = measured.filter((site) => !site.inBrowser && site.loaded);
const cold = measured.filter((site) => !site.loaded);
const zero = counted.filter((site) => site.hits === 0);

console.log("");
console.log("== 断言执行次数普查（页面侧）==");
console.log(
  `源码 ${files.length} 份（跑到的 ${loadedFiles.length} 份）；断言点 ${sites.length} 条：` +
    `数得出来 ${counted.length} 条、浏览器里跑 ${inBrowser.length} 条、整份没跑 ${cold.length} 条`,
);

console.log("");
console.log(`== 零次：这一趟一次也没被求值 —— ${zero.length} 条 ==`);
console.log(`${"kind".padEnd(7)} ${"位置".padEnd(40)} 所属 / 那一行`);
for (const site of zero.sort((a, b) => where(a).localeCompare(where(b))))
  console.log(`${site.kind.padEnd(7)} ${where(site).padEnd(40)} [${site.owner}] ${site.text}`);

console.log("");
console.log(
  `== 整份闸门没跑：${coldFiles.length} 份（里头 ${cold.length} 条断言点一次都没求值）==`,
);
for (const file of coldFiles) {
  const count = cold.filter((site) => site.file === file).length;
  console.log(`   ${String(count).padStart(4)} 条  ${relative(repoRoot, file)}`);
}

console.log("");
console.log(`== 非零那一段最靠前的 ${top} 条（升序）==`);
for (const site of counted
  .filter((each) => each.hits > 0)
  .sort((a, b) => a.hits - b.hits)
  .slice(0, top))
  console.log(
    `${String(site.hits).padStart(8)}  ${site.kind.padEnd(7)} ${where(site).padEnd(40)} [${site.owner}] ${site.text}`,
  );

console.log("");
console.log(`== 浏览器里求值、node 侧数不到的 ${inBrowser.length} 条（判据 4：抓不住的写出来）==`);
const byFile = new Map();
for (const site of inBrowser) byFile.set(site.file, (byFile.get(site.file) ?? 0) + 1);
for (const [file, count] of [...byFile].sort((a, b) => b[1] - a[1]))
  console.log(`   ${String(count).padStart(4)} 条  ${relative(repoRoot, file)}`);

// ---- 四、恒真式嫌疑（与 dotnet 那一份同一套判据）----

console.log("");
console.log("== 恒真式嫌疑 ==");

const tautologies = [];
for (const file of files) {
  const { raw, tree } = parse(file);
  const index = lineIndex(raw);

  const note = (offset, why, text) =>
    tautologies.push({ file, line: index.lineOf(offset), why, text: text.slice(0, 90) });

  walk(tree, [], (node) => {
    // 甲：条件写死成字面量（`if (true) …push(…)`）。
    if (
      node.type === "IfStatement" &&
      containsAssertCall(node.consequent) &&
      node.test.type === "Literal"
    )
      note(node.test.start, "条件是字面量", `if (${String(node.test.value)}) …`);

    // 乙：比较的两侧是同一段源码（`a === a`、`assert.equal(x, x)`）。
    if (node.type === "BinaryExpression" && ["===", "==", "!==", "!="].includes(node.operator)) {
      const left = raw.slice(node.left.start, node.left.end);
      const right = raw.slice(node.right.start, node.right.end);
      if (left === right) note(node.start, "两侧同一个表达式", `${left} ${node.operator} ${right}`);
    }

    if (
      node.type === "CallExpression" &&
      ASSERT_NAMES.has(callName(node) ?? "") &&
      node.arguments.length >= 2
    ) {
      const [left, right] = node.arguments;
      const leftText = raw.slice(left.start, left.end);
      const rightText = raw.slice(right.start, right.end);
      if (leftText === rightText)
        note(node.start, "两侧同一个表达式", raw.slice(node.start, node.end));
    }
  });
}

console.log(`共 ${tautologies.length} 条`);
for (const each of tautologies)
  console.log(`   ${relative(repoRoot, each.file)}:${each.line} —— ${each.why}：${each.text}`);

if (jsonOut !== null) {
  writeFileSync(
    jsonOut,
    `${JSON.stringify(
      measured.map((site) => ({
        file: relative(repoRoot, site.file),
        line: site.line,
        kind: site.kind,
        owner: site.owner,
        inBrowser: site.inBrowser,
        loaded: site.loaded,
        hits: site.hits,
        text: site.text,
      })),
      null,
      1,
    )}\n`,
  );
  console.log("");
  console.log(`全表写到 ${jsonOut}（${measured.length} 行）`);
}
