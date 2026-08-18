/**
 * **渲染版本号覆盖渲染器代码**（票 43）。
 *
 * 票 31 的版本号只哈希模板内容：改模板换一个值，**改渲染器代码不换**——而改渲染器同样
 * 换掉了前缀的字节。这一份文件把补上的那一半钉住，三件事：
 *
 * 1. **新鲜度**：`RENDERER_DIGEST`（生成物）必须等于此刻从源码算出来的那个值。
 *    改了渲染器却没重算，这一条当场红，`pnpm run render-digest` 是修法。
 * 2. **反向自证**：渲染器那几个文件**逐个**改一行 → 版本号必须变；
 *    渲染器之外的文件（`loop.ts` / `types.ts` / README）改了 → 版本号必须不变。
 * 3. **两半各管各的**：`模板 id@模板哈希.渲染器哈希`——换模板只动前半，换渲染器只动后半。
 *    M2 做三档对照实验时，靠这两半分得开「换了措辞」与「换了排版的那几行代码」。
 */

import assert from "node:assert/strict";
import { test } from "node:test";
import {
  RENDERER_ROOT,
  type ReadSource,
  readSource,
  rendererDigest,
  rendererFiles,
} from "../../scripts/renderer-digest.ts";
import { renderVersion } from "../../src/agent/render-version.ts";
import { RENDERER_DIGEST } from "../../src/agent/renderer-digest.ts";
import { DEFAULT_TEMPLATE, readTemplate } from "../../src/agent/template.ts";

/** 把某一个文件的字节改一改，其余原样。**只在内存里改**，工作区一个字节不动。 */
function edited(target: string): ReadSource {
  return (path) => (path === target ? `${readSource(path)}\n// 改了一行\n` : readSource(path));
}

/** 一棵假源码树：走查只认 import 语句，因此几行就够试出它认不认得出。 */
function tree(files: Record<string, string>): ReadSource {
  return (path) => {
    const source = files[path];
    if (source === undefined) throw new Error(`没有这个文件：${path}`);
    return source;
  };
}

// ---- 新鲜度 ----

test("生成的渲染器摘要与此刻的源码一致（改了渲染器就必须重算）", () => {
  assert.equal(
    RENDERER_DIGEST,
    rendererDigest(),
    "渲染器的源码变了而 src/agent/renderer-digest.ts 没重算：跑 `pnpm run render-digest`",
  );
});

test("摘要覆盖的就是渲染器那几个文件：从 prompt.ts 沿值导入可达的那些", () => {
  const files = rendererFiles();

  assert.deepEqual(files, [
    "src/agent/action-label.ts",
    "src/agent/history.ts",
    "src/agent/prompt.ts",
    "src/agent/template.ts",
    "src/agent/wording.ts",
  ]);
  assert.ok(files.includes(RENDERER_ROOT), "根就在里面");
});

// ---- 反向自证 ----

test("渲染器那几个文件逐个改一行：版本号每一次都跟着变", () => {
  const base = rendererDigest();

  for (const file of rendererFiles()) {
    assert.notEqual(
      rendererDigest(edited(file)),
      base,
      `改了 ${file} 而摘要没变——那这个版本号就漏得掉`,
    );
  }
});

test("渲染器之外的文件改了：版本号一个字符都不动", () => {
  const base = rendererDigest();

  for (const file of [
    "src/agent/loop.ts",
    "src/agent/types.ts",
    "src/agent/retry.ts",
    "src/agent/render-version.ts",
    "README.md",
  ]) {
    assert.equal(
      rendererDigest(edited(file)),
      base,
      `${file} 排不出 prompt 的字节，改它不该换一个版本号`,
    );
  }
});

// ---- 走查认不出来就报错，不静静漏掉 ----

test("把渲染拆进新文件：它自动进摘要，多行 import 也跟得住", () => {
  const files = rendererFiles(
    tree({
      [RENDERER_ROOT]: 'import {\n  historyLines,\n} from "./extracted.ts";\n',
      "src/agent/extracted.ts": "export const historyLines = () => [];\n",
    }),
  );

  assert.deepEqual(files, ["src/agent/extracted.ts", RENDERER_ROOT]);
});

test("纯类型导入不跟：编译后它一个字节都不剩", () => {
  const files = rendererFiles(
    tree({ [RENDERER_ROOT]: 'import type { DecisionPackage } from "./types.ts";\n' }),
  );

  assert.deepEqual(files, [RENDERER_ROOT]);
});

test("认不出来的 import 当场抛：一个漏读的文件就是一个静默的口子", () => {
  const dynamic = tree({
    [RENDERER_ROOT]: 'const words = await import("./wording.ts");\n',
  });
  assert.throws(() => rendererFiles(dynamic), /动态 import/);

  const unterminated = tree({ [RENDERER_ROOT]: "import {\n  historyLines,\n// 没接完\n" });
  assert.throws(() => rendererFiles(unterminated), /没接完/);

  const puzzling = tree({ [RENDERER_ROOT]: "import {\n  historyLines,\n};\n" });
  assert.throws(() => rendererFiles(puzzling), /读不懂/);
});

// ---- 两半各管各的 ----

test("版本号是「模板 id@模板哈希.渲染器摘要」，两半分得开", () => {
  const version = renderVersion(DEFAULT_TEMPLATE);

  assert.match(version, /^janpo-default@[0-9a-f]{8}\.[0-9a-f]{8}$/);
  // 后半截就是渲染器摘要：改了渲染器，上面那道新鲜度闸门逼着它重算，版本号跟着变。
  assert.ok(version.endsWith(`.${RENDERER_DIGEST}`));

  // 换模板只动前半：后半截是同一个渲染器。
  const styled = renderVersion(readTemplate('{"persona":"你打得很凶。"}'));
  assert.notEqual(styled.split(".")[0], version.split(".")[0]);
  assert.equal(styled.split(".")[1], version.split(".")[1]);
});
