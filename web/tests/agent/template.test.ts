/**
 * **prompt 是数据**（票 31）：措辞与人格从座位配置注入，**改配置不改代码**。
 *
 * 三件事在这里钉住：
 * 1. 默认模板仍在代码里，且它渲出来的字节与 29b 那一版逐字相同（换法不许悄悄改默认）；
 * 2. **档位与人格是两个独立维度**——换人格只动 system，换档位只动尾部那一节；
 * 3. 渲染版本号里**模板那一半**是算出来的：改一个字就换一个值，同样的内容恒是同一个值。
 *    （渲染器那一半在 `render-version.test.ts`。）
 */

import assert from "node:assert/strict";
import { test } from "node:test";
import { promptMessages, promptSections, renderPrompt } from "../../src/agent/prompt.ts";
import { renderVersion, templateDigest } from "../../src/agent/render-version.ts";
import {
  DEFAULT_TEMPLATE,
  preambleOf,
  readTemplate,
  resolveTemplate,
} from "../../src/agent/template.ts";
import type { SeatConfig } from "../../src/agent/types.ts";
import { DEFAULT_WORDING } from "../../src/agent/wording.ts";
import { dahaiPackage, seat } from "./fixtures.ts";

/** 一个主持人真会填的例子：人格一格自由文本，模板一段 JSON（**都在配置里**）。 */
const persona = "你是一位以防守见长的雀士，宁可少和一把，也不点炮。说话简短。";

const custom = JSON.stringify({
  id: "shibori",
  labels: { history: "【战况回放】", board: "【此刻】" },
  wording: { naki: { pon: "碰！" }, relative: { "1": "下家老弟" } },
});

function withConfig(overrides: Partial<SeatConfig>): SeatConfig {
  return { ...seat, ...overrides };
}

// ---- 默认模板 ----

test("默认模板仍在代码里，且它就是 29b 那一份 preamble（一个字节都没动）", () => {
  assert.equal(DEFAULT_TEMPLATE.id, "janpo-default");
  assert.equal(DEFAULT_TEMPLATE.persona, "", "默认不带人格");
  assert.equal(preambleOf(DEFAULT_TEMPLATE), DEFAULT_TEMPLATE.system);
  assert.deepEqual(DEFAULT_TEMPLATE.wording, DEFAULT_WORDING);

  // 不给模板的座位拿到的就是它——「默认仍是能跑的那一个」。
  assert.deepEqual(resolveTemplate(seat), DEFAULT_TEMPLATE);
  assert.match(renderPrompt(dahaiPackage, "bare", null), /^你在打日本立直麻将/);
});

// ---- 人格是一个独立维度 ----

test("换人格：只有 system 那一条变，user 那一条一个字节都不动", () => {
  const template = resolveTemplate(withConfig({ persona }));
  const plain = promptMessages(dahaiPackage, "bare", null);
  const styled = promptMessages(dahaiPackage, "bare", null, template);

  assert.equal(styled.user, plain.user, "人格不该漏进历史或尾部");
  assert.ok(styled.system.startsWith(persona), "人格排在 system 的最前面");
  assert.ok(styled.system.endsWith(DEFAULT_TEMPLATE.system), "规则说明照旧接在后面");
});

test("人格 × 档位：两个维度各管各的，四种组合可分解", () => {
  const template = resolveTemplate(withConfig({ persona }));
  const bare = promptMessages(dahaiPackage, "bare", null);
  const assisted = promptMessages(dahaiPackage, "assisted", null);
  const styledBare = promptMessages(dahaiPackage, "bare", null, template);
  const styledAssisted = promptMessages(dahaiPackage, "assisted", null, template);

  // 换档位只动 user（多一节算好的数），换人格只动 system。
  assert.equal(bare.system, assisted.system);
  assert.equal(styledBare.system, styledAssisted.system);
  assert.equal(styledBare.user, bare.user);
  assert.equal(styledAssisted.user, assisted.user);
  assert.notEqual(bare.user, assisted.user);
  assert.notEqual(bare.system, styledBare.system);
});

test("两个座位可以同模型不同人格：模板是座位级的", () => {
  const left = resolveTemplate(withConfig({ persona: "你打得很凶。" }));
  const right = resolveTemplate(withConfig({ persona: "你打得很稳。" }));

  assert.notEqual(
    promptMessages(dahaiPackage, "bare", null, left).system,
    promptMessages(dahaiPackage, "bare", null, right).system,
  );
  assert.notEqual(renderVersion(left), renderVersion(right));
});

// ---- 措辞从配置注入 ----

test("换模板：抬头与措辞表都换得动，没给的那几项沿用默认", () => {
  const template = resolveTemplate(withConfig({ template: custom }));
  const sections = promptSections(dahaiPackage, "bare", null, template);

  assert.ok(sections.history.startsWith("【战况回放】"), "历史那一段的抬头换了");
  assert.ok(sections.present.startsWith("【此刻】"), "场况那一节的抬头换了");
  assert.match(sections.present, /碰！ 3m/, "副露的说法换了，前缀与尾部同一套");
  assert.match(sections.history, /碰！/);
  assert.match(sections.present, /下家老弟（座位 2/, "相对座位的叫法换了");

  // 没给的那几项照旧：手牌那一节的抬头、规则说明、其余四张表。
  assert.match(sections.present, /【你的手牌】/);
  assert.equal(sections.preamble, DEFAULT_TEMPLATE.system);
  assert.match(sections.present, /东1局/, "风的说法没给，沿用默认");
});

test("人格那一格压过模板 JSON 里的人格：它离人更近", () => {
  const both = resolveTemplate(
    withConfig({ persona, template: JSON.stringify({ persona: "模板里写的那个人格" }) }),
  );

  assert.equal(both.persona, persona);
});

test("模板读不动就退回默认，而不是把这一手卡死", () => {
  for (const broken of ["{这不是 JSON", "[1,2,3]", '"一段字符串"']) {
    assert.deepEqual(readTemplate(broken), DEFAULT_TEMPLATE, `「${broken}」该退回默认模板`);
  }

  // 空字符串是「没填」，不是「填坏了」。
  assert.deepEqual(readTemplate(""), DEFAULT_TEMPLATE);
  // 类型不对的单项也按「没给」处理，其余项照收。
  assert.equal(readTemplate('{"id":42,"persona":"甲"}').id, DEFAULT_TEMPLATE.id);
  assert.equal(readTemplate('{"id":42,"persona":"甲"}').persona, "甲");
});

// ---- 渲染版本号是算出来的 ----

test("渲染版本号 = 模板 id + 内容哈希（+ 渲染器摘要），同样的内容恒是同一个值", () => {
  assert.match(renderVersion(DEFAULT_TEMPLATE), /^janpo-default@[0-9a-f]{8}\.[0-9a-f]{8}$/);
  assert.equal(renderVersion(DEFAULT_TEMPLATE), renderVersion(readTemplate("{}")));

  // **跨进程、跨机器稳定**：它是牌谱里的键，换一台机器读出来必须还是同一个。
  // 钉的是**这里写死的那份模板**而不是默认模板：改默认 prompt 的措辞不该让 CI 变红
  // （票面明写），而“同样的内容恒是同一个值”这条性质跟它是哪份模板无关。
  // 钉的也只是**模板那一半**：渲染器摘要本就该随渲染器改而变，钉它等于钉死代码。
  const pinned = readTemplate(
    JSON.stringify({ id: "pinned", persona: "甲", system: "乙", labels: { history: "丙" } }),
  );

  assert.equal(templateDigest(pinned), "f6eb3b4c");
  assert.ok(renderVersion(pinned).startsWith("pinned@f6eb3b4c."));
});

test("改任何一个字都换一个版本号——手填的数字漏得掉，算出来的漏不掉", () => {
  const base = renderVersion(DEFAULT_TEMPLATE);
  const changed = [
    { ...DEFAULT_TEMPLATE, persona: "你很凶" },
    { ...DEFAULT_TEMPLATE, system: `${DEFAULT_TEMPLATE.system}。` },
    {
      ...DEFAULT_TEMPLATE,
      labels: { ...DEFAULT_TEMPLATE.labels, history: "【看过的】" },
    },
    {
      ...DEFAULT_TEMPLATE,
      wording: { ...DEFAULT_WORDING, naki: { ...DEFAULT_WORDING.naki, pon: "碰！" } },
    },
  ].map(renderVersion);

  assert.equal(new Set([base, ...changed]).size, 5, "五份不同的模板该有五个不同的版本号");
  // id 换了、内容没换：`@` 前面那一截变，后面两截不变
  // ——「这是同一份措辞的另一个名字」看得出来。
  const renamed = renderVersion({ ...DEFAULT_TEMPLATE, id: "another" });
  assert.equal(renamed.split("@")[1], base.split("@")[1]);
});
