/**
 * prompt 渲染（票 23 的 Bare 档 + 票 24 的 Assisted 档）。四件事：**该有的都在**、
 * **Bare 档不该有的一样都没有**（他家暗牌、脚手架数值）、**Assisted 档多出来的就是
 * 那一节数值**、**重试时告诉模型上一次错在哪**。
 */

import assert from "node:assert/strict";
import { test } from "node:test";
import { promptSections, renderPrompt } from "../../src/agent/prompt.ts";
import { DEFAULT_TEMPLATE, preambleOf } from "../../src/agent/template.ts";
import type { DecisionPackage } from "../../src/agent/types.ts";
import { DEFAULT_WORDING, wordsFor } from "../../src/agent/wording.ts";
import { dahaiPackage, dangerPackage, responsePackage, sequencePackages } from "./fixtures.ts";

const bare = renderPrompt(dahaiPackage, "bare", null);

test("局面该有的都在：场况、自家手牌、他家、合法动作", () => {
  const observation = dahaiPackage.observation;

  assert.match(bare, /东1局 0 本场/);
  assert.match(bare, /宝牌指示牌：2s/);
  assert.match(bare, /牌山剩余可摸 66 张/);

  for (const pai of observation.self.tehai) {
    assert.ok(bare.includes(pai), `自家手牌里的 ${pai} 该出现在 prompt 里`);
  }

  for (const option of dahaiPackage.actions) {
    assert.ok(bare.includes(`id=${option.id}：${option.label}`), `动作 ${option.id} 该列出来`);
  }
});

test("副露与摸切标记：都是公开信息，都写出来", () => {
  // 自家有一组碰（3m），他家河里有摸切。**措辞与前缀那一段同一套**（票 29b）：
  // 种类写中文、来源写相对位置、牌照 mjai 记法。
  assert.match(bare, /副露：碰 3m（来自对家，亮出 3m 3m）/);
  assert.match(bare, /5p\*/, "摸切的那张后面缀 `*`");
});

test("三段：固定 preamble → 【到目前为止你看到的】 → 【现在】", () => {
  // 形态是票 29b 定死的：前两段可缓存，第三段每手付全价。
  // **三段是分开取得出来的**（票 31 要在这上面做槽位注入），拼起来才是整份 prompt。
  const sections = promptSections(dahaiPackage, "bare", null);

  assert.equal(sections.preamble, preambleOf(DEFAULT_TEMPLATE), "preamble 逐字不变，与局面无关");
  assert.ok(sections.history.startsWith("【到目前为止你看到的】\n"));
  assert.ok(sections.present.startsWith("【现在】\n"));
  assert.equal([sections.preamble, sections.history, sections.present].join("\n\n"), bare);

  // 历史那一段渲的就是决策包里那条掩蔽事件流，一条一行。
  assert.equal(
    sections.history.split("\n").length,
    dahaiPackage.history.length + 1,
    "标题一行 + 每条事件一行",
  );

  // 尾部的先后：【现在】→【可选动作】→（脚手架）→（重试原因）。
  const assistedRetry = promptSections(dahaiPackage, "assisted", "上次那个 id 不在包里").present;
  assert.ok(
    assistedRetry.indexOf("【可选动作】") < assistedRetry.indexOf("【引擎算好的数】"),
    "算好的数排在可选动作之后",
  );
  assert.ok(
    assistedRetry.indexOf("【引擎算好的数】") < assistedRetry.indexOf("【上一次的回答没有被采用】"),
    "重试原因排在最后",
  );
});

test("事件流带来的东西真的进了 prompt：巡目、鸣的来源与时序", () => {
  // 快照里河是一串牌、副露是另一串组，两条串接不上；这一段把它们接上了。
  assert.match(bare, /你 第1巡 打 2z/);
  assert.match(bare, /你 第1巡 碰 对家打的 3m（亮出 3m 3m）/);
  assert.match(bare, /上家 第1巡 摸牌/, "他家摸了看得见，摸的是什么看不见");
});

test("他家的暗牌在 prompt 里根本无从写起——决策包里就没有", () => {
  const others = dahaiPackage.observation.others;

  for (const other of others) {
    assert.equal(
      "tehai" in other,
      false,
      "MaskedSeat 类型里没有 tehai：隐藏信息的保护在结构上成立",
    );
    assert.ok(bare.includes(`手里 ${other.tehai_count} 张`), "他家只给得出张数");
  }
});

test("Bare 档不给任何算好的数：向听、有效牌、危险度一个都没有", () => {
  // 这一档存在的意义就是量「模型自己会不会数牌」。
  // **决策包里有这些数**（包恒带脚手架），档位决定的是写不写进 prompt。
  for (const word of ["向听", "有效牌", "进退向", "危险", "shanten", "ukeire"]) {
    assert.equal(bare.includes(word), false, `Bare 档的 prompt 不该出现「${word}」`);
  }
  assert.ok(
    Object.keys(dahaiPackage.scaffold).length > 0,
    "决策包本身带着脚手架，Bare 档只是不渲染它",
  );
});

test("响应那一手也渲染得出来：没摸牌，动作只有碰与过", () => {
  const prompt = renderPrompt(responsePackage, "bare", null);

  assert.match(prompt, /刚摸进：无（这一手不是你摸牌）/);
  assert.match(prompt, /id=1：过/);
});

test("重试时把上一次错在哪接在末尾", () => {
  const retry = renderPrompt(dahaiPackage, "bare", "action_id=99 不在这一手的合法动作集里");

  assert.ok(retry.startsWith(bare), "重试的 prompt 是原来那份加一段尾巴");
  assert.match(retry, /上一次的回答没有被采用/);
  assert.match(retry, /action_id=99/);
});

test("ToolSearch 暂时照 Bare 渲染：它的工具是 M3 的事", () => {
  assert.equal(renderPrompt(dahaiPackage, "tool_search", null), bare);
});

// ---- Assisted 档（票 24） ----

const assisted = renderPrompt(dahaiPackage, "assisted", null);

test("两档只差一节：局面、他家与可选动作逐字相同", () => {
  // 「同一局面两档 prompt 肉眼可比」是本票的验收：差异必须就是那一节数值，
  // 不能顺手把别的措辞也改了，否则两档的对照就不是一个变量。
  const extra = assisted.split("\n\n").filter((block) => !bare.includes(block));

  assert.equal(extra.length, 1, "Assisted 档只多出一段");
  assert.match(extra[0], /【引擎算好的数】/);
  assert.ok(assisted.length > bare.length);
});

test("Assisted 档：向听数与逐张试打的进退向都写出来", () => {
  // 决策包（`janpo decide 2088 --steps 6`）：3 向听，手切 3 筒（id=3）退向。
  assert.match(assisted, /当前向听数：3 向听/);
  assert.match(assisted, /id=0（手切2万）：打完 3 向听，进退向 0，有效牌 66 枚/);
  assert.match(assisted, /id=3（手切3筒）：打完 4 向听，退向 \+1，有效牌 76 枚/);

  // 有效牌是「牌种 + 剩余枚数」，牌照 mjai 记法（与手牌那一行同一种写法）。
  assert.match(assisted, /3m\(1\) 4m\(4\)/);
});

test("Assisted 档：每一条打牌动作都有它自己那一行", () => {
  for (const option of dahaiPackage.actions) {
    assert.ok(
      assisted.includes(`- id=${option.id}（${option.label}）：打完 `),
      `动作 ${option.id} 该有一条试打`,
    );
  }
});

test("Assisted 档：打不了牌的那一手直接给有效牌", () => {
  // 响应那一手是等摸形（没摸牌），有效牌算得出来，也没有逐张试打。
  const prompt = renderPrompt(responsePackage, "assisted", null);

  assert.match(prompt, /当前向听数：3 向听/);
  assert.match(prompt, /有效牌 28 枚 8 种：1m\(4\)/);
  assert.equal(prompt.includes("逐张试打"), false, "这一手没牌可打");
});

test("Assisted 档：重试的尾巴照样接得上", () => {
  const retry = renderPrompt(dahaiPackage, "assisted", "action_id=99 不在这一手的合法动作集里");

  assert.ok(retry.startsWith(assisted));
  assert.match(retry, /上一次的回答没有被采用/);
});

// ---- 危险度（票 25） ----

const danger = renderPrompt(dangerPackage, "assisted", null);

test("Assisted 档：危险度排序逐张写出来，理由标签照术语表", () => {
  // 决策包（`janpo decide 99 --steps 6 --seat 3`）：对家有副露、河里有 4m。
  assert.match(danger, /危险度排序（有威胁的家：对家有副露）/);
  assert.match(danger, /- 第1位 id=0（手切4万）：现物 —— 对家现物/);
  assert.match(danger, /- 第2位 id=1（手切7万）：筋 —— 对家 4m 筋/);
  assert.match(danger, /- 第3位 id=2（手切3筒）：无依据$/m);
  assert.match(danger, /- 第11位 id=3（手切5筒）：无依据 —— 宝牌 7p 周边/);

  // 赤 5 与正 5 是两条动作、一个牌种：两条各占一行，名次相同。
  assert.match(danger, /- 第11位 id=4（手切赤5筒）：无依据 —— 宝牌 7p 周边/);

  // **它是启发式不是概率**（票里明写）：说清楚这一点，且不写任何会被读成统计结论的数。
  assert.match(danger, /不是概率/);
  for (const banned of ["放铳率", "%", "百分"]) {
    assert.equal(danger.includes(banned), false, `危险度那一节不该出现「${banned}」`);
  }
});

test("Assisted 档：名次跟着引擎走，排序从安全到危险", () => {
  const lines = danger.split("\n").filter((line) => /^- 第\d+位 id=/.test(line));
  const ranks = lines.map((line) => Number(/^- 第(\d+)位/.exec(line)?.[1]));

  assert.equal(lines.length, dangerPackage.actions.length, "每一条打牌动作各一行");
  assert.deepEqual(
    ranks,
    [...ranks].sort((left, right) => left - right),
    "名次不下降",
  );
  assert.equal(ranks[0], 1);
});

test("没人立直也没人副露时不写危险度", () => {
  // 开局第几巡那份包（`decide 2088 --steps 6`）里 `threats` 是空的，
  // 引擎本来就不给排序（DECISIONS 25-1）。
  assert.equal(assisted.includes("危险度排序"), false);
  assert.match(assisted, /逐张试打/, "其余那几项照旧");
});

test("24 号票那份形状的包仍然渲染得出来：少一节不该让整包读不动", () => {
  // 把 `threats` 与逐条 `danger` 摸掉（就是 24 号票当时的形状）。
  const scaffold = dangerPackage.scaffold as { dahai: Record<string, unknown>[] };
  const legacy = {
    ...dangerPackage,
    scaffold: {
      ...dangerPackage.scaffold,
      threats: undefined,
      dahai: scaffold.dahai.map((entry) => ({ ...entry, danger: undefined })),
    },
  };

  const rendered = renderPrompt(legacy, "assisted", null);

  assert.equal(rendered.includes("危险度排序"), false);
  assert.match(rendered, /逐张试打/);
});

test("脚手架读不动时 Assisted 退回 Bare，而不是崩", () => {
  // 23 号票时代录下来的包（`scaffold` 是空对象）仍然渲染得出来：
  // 宁可少给几个数，不能把这一手卡死。
  const legacyPackage = { ...dahaiPackage, scaffold: {} };

  assert.equal(
    renderPrompt(legacyPackage, "assisted", null),
    renderPrompt(legacyPackage, "bare", null),
  );
});

// ---- 副露那一句的参照系（票 40） ----
//
// **相对方位在多主体渲染里必须显式声明参照系，否则必错**（票 38 挖出来的判据）。
// 副露的「来自谁」说的是**副露方**的第几家（`Naki.Target` 的语义、牌桌那边的画法），
// 而【他家】那一节的抬头（`下家（座位 2・…）`）说的是**观测者**的第几家。
// 同一份 prompt 里两个参照系各管一半，因此谁是谁必须**在那句话里就看得出来**。
//
// 下面这几条钉的不是措辞而是**这句中文说的是不是真的**：黄金用例逐字段钉的是决策包
// （里面是绝对座位，永远没错），前缀属性测试钉的是字节单调，两者都不检查这一层。

/** 五种副露的中文（默认模板的 `wording.naki`）。 */
const KINDS = ["碰", "吃", "暗杠", "大明杠", "加杠"] as const;

interface CalledGroup {
  /** 这一组副露挂在谁名下：`你` 或【他家】那一节的抬头（`下家` / `对家` / `上家`）。 */
  owner: string;
  kind: string;
  /** 那句「来自X」里的 X；暗杠没有这一项。 */
  from: string | null;
}

const OWNER = /^(?:你是座位 \d+|(下家|对家|上家|座位 \d+)（座位 \d+・)/;
const GROUPS = new RegExp(
  `(${KINDS.join("|")})(?: [^（]+)?（(?:来自([^，]+)，)?亮出 [^）]+）`,
  "g",
);

/** 把渲染出来的 prompt 拆回「谁的哪一组副露、写着来自谁」。 */
function calledGroups(prompt: string): CalledGroup[] {
  const groups: CalledGroup[] = [];
  let owner = "";

  for (const line of prompt.split("\n")) {
    const head = OWNER.exec(line);
    if (head !== null) owner = head[1] ?? "你";

    const naki = /^\s*副露：(.+)$/.exec(line);
    if (naki === null) continue;

    for (const [, kind, from] of naki[1].matchAll(GROUPS)) {
      groups.push({ owner, kind, from: from ?? null });
    }
  }

  return groups;
}

/** 决策包里那几家：座位号、观测者眼里的称呼、以及各自的副露。 */
function seatsOf(decision: DecisionPackage) {
  const words = wordsFor(
    DEFAULT_WORDING,
    decision.observation.self.seat,
    decision.observation.others,
  );
  const self = decision.observation.self;

  return [
    { seat: self.seat, name: "你", naki: self.naki },
    ...decision.observation.others.map((other) => ({
      seat: other.seat,
      name: words.who(other.seat),
      naki: other.naki,
    })),
  ];
}

/**
 * 从 `owner` 起沿**下家方向**数到 `seat` 是第几家。
 *
 * 用例这一侧独立走一遍座位环（决策包给的 `relative` 排出来的那个圈），
 * 不去调渲染器用的那份——两边都错成同一个样子的话这条就白写了。
 */
function distance(decision: DecisionPackage, owner: number, seat: number): number {
  const ring = [
    { seat: decision.observation.self.seat, relative: 0 },
    ...decision.observation.others,
  ]
    .sort((left, right) => left.relative - right.relative)
    .map((each) => each.seat);

  const start = ring.indexOf(owner);
  return [...ring.slice(start), ...ring.slice(0, start)].indexOf(seat);
}

const CORPUS: { name: string; decision: DecisionPackage }[] = [
  { name: "decision-dahai", decision: dahaiPackage },
  { name: "decision-response", decision: responsePackage },
  { name: "decision-danger", decision: dangerPackage },
  ...sequencePackages.map((decision, hand) => ({ name: `decision-sequence[${hand}]`, decision })),
];

test("他家的副露：「来自谁」以副露方为参照系，并就地声明是「他的」", () => {
  // `decision-sequence[11]`（座位 1 的视角）里三家都有副露，且**两个参照系给的词不一样**：
  // 座位 0（上家）吃的是座位 3 打的——座位 3 相对**它**是上家，相对**我**是对家。
  const prompt = renderPrompt(sequencePackages[11], "bare", null);

  assert.ok(
    prompt.includes("副露：吃 2p（来自他的上家，亮出 3p 4p）"),
    "上家那一组吃：来源相对副露方是上家（相对观测者才是对家）",
  );
  assert.ok(
    prompt.includes("吃 3m（来自他的上家，亮出 4m 5mr），吃 6m（来自他的上家，亮出 4m 5m）"),
    "对家那两组吃：来源相对副露方是上家（相对观测者才是下家）",
  );
  assert.ok(
    prompt.includes("副露：碰 2z（来自你，亮出 2z 2z）"),
    "鸣的是观测者打的那张时写「你」：那是身份不是方位，不必声明参照系",
  );
});

test("自家的副露一个字没变：它的参照系本来就是观测者自己", () => {
  const prompt = renderPrompt(sequencePackages[11], "bare", null);

  assert.match(bare, /副露：碰 3m（来自对家，亮出 3m 3m）/);
  assert.ok(
    prompt.includes("副露：吃 7p（来自上家，亮出 8p 9p），大明杠 9m（来自对家，亮出 9m 9m 9m）"),
    "自家那几组不写「他的」——副露方就是观测者，两个参照系重合",
  );
});

/** 一份固件里渲出来的每一组副露，配上包里那一组的绝对座位。 */
function paired(name: string, decision: DecisionPackage) {
  const rendered = calledGroups(renderPrompt(decision, "bare", null));
  const expected = seatsOf(decision).flatMap((seat) =>
    seat.naki.map((group) => ({ seat: seat.seat, name: seat.name, group })),
  );

  assert.equal(rendered.length, expected.length, `${name}：渲出来的副露组数该与包里一样`);

  return rendered.map((group, index) => {
    const each = expected[index];
    assert.equal(group.owner, each.name, `${name} 的那组「${group.kind}」挂错了人`);

    return {
      where: `${name} 的${each.name}那组「${group.kind}」`,
      viewer: decision.observation.self.seat,
      owner: each.seat,
      target: each.group.target,
      /** 从副露方起沿下家方向数到来源那家是第几家；暗杠没有来源，记 0。 */
      step: each.group.target === null ? 0 : distance(decision, each.seat, each.group.target),
      from: group.from,
      kind: group.kind,
    };
  });
}

const CALLED = CORPUS.flatMap(({ name, decision }) => paired(name, decision));

test("吃恒来自上家——规则那一条：写出来的那句话在日麻里得成立", () => {
  // 这条与实现无关，钉的是**规则**：吃只吃得了上家（`Naki.chi` 的不变量，`Action.fs:34`）。
  // 参照系一旦漂到观测者，它当场报「吃 来自对家」这种日麻里不成立的句子。
  const chi = CALLED.filter((group) => group.kind === "吃");

  for (const group of chi) {
    assert.equal(group.step, 3, `${group.where}：包里的来源座位就不是它的上家，固件坏了`);
    assert.ok(
      group.from === "你" || group.from?.endsWith("上家"),
      `${group.where}写着「来自${group.from}」：吃只吃得了上家`,
    );
  }

  // 防空转：语料里真有吃（自家的与他家的都要有），上面那一条才验到了东西。
  assert.ok(chi.length >= 4, `吃只走到 ${chi.length} 组，这条闸门在空转`);
  assert.ok(
    chi.some((group) => group.owner !== group.viewer),
    "语料里没有**他家**的吃，这条闸门在空转",
  );
});

test("每一组副露的「来自谁」都与决策包里的绝对座位对得上（全部固件）", () => {
  const words = ["下家", "对家", "上家"];
  let others = 0;

  for (const group of CALLED) {
    if (group.target === null) {
      assert.equal(group.from, null, `${group.where}：暗杠没有来源`);
      continue;
    }

    // 写出来的词必须就是「从副露方起数到那一家」的第几家；鸣的是观测者打的那张时写「你」。
    const relative = words[group.step - 1];
    const expected =
      group.target === group.viewer
        ? "你"
        : group.owner === group.viewer
          ? relative
          : `他的${relative}`;

    assert.equal(group.from, expected, `${group.where}：来源的参照系漂了`);
    if (group.owner !== group.viewer) others += 1;
  }

  assert.ok(others >= 6, `他家的副露只走到 ${others} 组，这条闸门在空转`);
});

test("危险度那一节的参照系是**观测者**：威胁名单按你自己的方位说", () => {
  // 与副露那一句不同，威胁名单说的是「谁在威胁**你**」，参照系本来就该是观测者。
  // 引擎渲染好的 `display` 用的是 `Threat.who`（`MaskedSeat.Relative`，相对观测者）。
  const threats = (dangerPackage.scaffold as { threats: { seat: number; display: string }[] })
    .threats;
  const words = wordsFor(
    DEFAULT_WORDING,
    dangerPackage.observation.self.seat,
    dangerPackage.observation.others,
  );

  assert.ok(threats.length > 0, "这份固件本来就该有有威胁的家");
  for (const threat of threats) {
    assert.ok(
      threat.display.startsWith(words.who(threat.seat)),
      `威胁名单里「${threat.display}」该按观测者的方位称呼座位 ${threat.seat}`,
    );
  }
  assert.match(danger, /有威胁的家：对家有副露/);
});
