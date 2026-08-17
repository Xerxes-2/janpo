/**
 * **渲染版本号**（票 31 收 29b-A，票 43 补上漏掉的那一半）。
 *
 * ```
 * 模板 id @ 模板内容的哈希 . 渲染器代码的摘要
 * janpo-default@08fcaec3.4f0d9c1e
 * ```
 *
 * **它是算出来的，不是手填的**——手填的数字没有任何东西保证有人改措辞、改排版时会 +1。
 * 它进 `DecisionRecord`，并且是牌谱里「这一手的 preamble 是哪一份」的键
 * （`Paifu.Prompting.Preambles` 按它索引）。
 *
 * ## 为什么是两截
 *
 * 一份 prompt 的字节由两样东西决定：**措辞**（模板：人格、规则说明、抬头、五张措辞表）
 * 与**排版的那几行代码**（`prompt.ts` / `history.ts` / `wording.ts` / `template.ts`）。
 * 票 31 只哈希了前者，于是改渲染器代码同样废掉全部缓存却**不换版本号**（M1 收官记账点的那个口子）。
 * 两截分开写而不是揉成一个哈希，是因为 M2 要拿它归因：命中率掉下来时，
 * **前半变了＝换了措辞，后半变了＝换了排版的代码**，一眼分得开。
 *
 * 后半截是**生成物**（`renderer-digest.ts`，由 `scripts/renderer-digest.ts` 算），
 * 不是在浏览器里现算：现算要么把源码打进 bundle，要么让离线脚本与浏览器各算一份。
 * 生成物过期由 `tests/agent/render-version.test.ts` 的新鲜度闸门看着。
 *
 * ## 它不覆盖什么
 *
 * - **工具定义的形状**（`tools.ts`）：它整场逐字存进牌谱的 `Prompting.Tools`，
 *   要归因直接 diff 那一段，不需要一个哈希替它说话；
 * - **决策包的内容**（引擎那侧）：那是「这一手发生了什么」，不是「怎么写成中文」；
 * - **`loop.ts` 问了几次、档位选了哪一档**：前者看 `attempts`，后者看尾部有没有那一节。
 *
 * **版本号本身不进 prompt**（裁决 31-6）：写进去等于每换一次版本号就主动废一次缓存，
 * 而版本号变的时候缓存本来就已经废了。
 */

import { fnv1a } from "./hash.ts";
import { RENDERER_DIGEST } from "./renderer-digest.ts";
import type { PromptTemplate } from "./template.ts";

/** 一张表的规范写法：键排过序，因此同样的内容恒是同一段字节。 */
function canonicalTable(table: Record<string, string>): string {
  return Object.keys(table)
    .sort()
    .map((key) => `${key}=${table[key]}`)
    .join("\u0001");
}

/**
 * 模板**内容**的哈希：人格、规则说明、八个抬头与五张措辞表。
 *
 * 内容含人格：换人格就是换一份可缓存前缀，账单上看得出来，版本号也该看得出来。
 * **不含 `id`**——id 是给人看的那一半，另一个名字下的同一份措辞，后面这一截仍相同。
 */
export function templateDigest(template: PromptTemplate): string {
  const labels = template.labels;

  return fnv1a(
    [
      template.persona,
      template.system,
      labels.history,
      labels.board,
      labels.hand,
      labels.others,
      labels.actions,
      labels.scaffold,
      labels.retry,
      labels.retryTail,
      canonicalTable(template.wording.kaze),
      canonicalTable(template.wording.riichi),
      canonicalTable(template.wording.relative),
      canonicalTable(template.wording.naki),
      canonicalTable(template.wording.ryuukyoku),
    ].join("\u0000"),
  );
}

/**
 * 这一手的渲染版本号：`模板 id@模板哈希.渲染器摘要`。
 *
 * **渲染器那一截只有一个来源**（生成物里的常量）：浏览器、`node --test`、
 * 离线脚本与 CI 读到的是同一个字面量，不存在「这边算一份、那边算另一份」。
 */
export function renderVersion(template: PromptTemplate): string {
  return `${template.id}@${templateDigest(template)}.${RENDERER_DIGEST}`;
}
