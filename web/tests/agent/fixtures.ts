/**
 * 用例共用的固件读取与「回放录制响应」的假 `ask`（票 23）。
 *
 * **CI 里绝不调真实 API**（M1 增量约束 6）。`ask-*.json` 是真模型答过的话，
 * 由 `scripts/record-agent-fixtures.mjs` 录制；这里只是把它们喂回决策循环。
 */

import { readFileSync } from "node:fs";
import type { Ask, AskResult } from "../../src/agent/ask.ts";
import { CUSTOM_PROVIDER } from "../../src/agent/endpoint.ts";
import type { DecideRequest, DecisionPackage, SeatConfig } from "../../src/agent/types.ts";

function load<T>(name: string): T {
  return JSON.parse(
    readFileSync(new URL(`../fixtures/agent/${name}.json`, import.meta.url), "utf8"),
  );
}

/** 打牌那一手的决策包（`janpo decide 2088 --steps 6`），11 条动作。 */
export const dahaiPackage = load<DecisionPackage>("decision-dahai");

/** 响应那一手的决策包（`janpo decide 2088 --steps 5`），只有「碰」与「过」两条。 */
export const responsePackage = load<DecisionPackage>("decision-response");

/**
 * 带危险度的决策包（`janpo decide 99 --steps 6 --seat 3`，票 25）：
 * 对家有副露，同一手里现物 / 筋 / 无依据三档都出得来，赤 5 与正 5 还共用一条试打。
 */
export const dangerPackage = load<DecisionPackage>("decision-danger");

/**
 * **桁上有暗杠**那一手（`janpo decide 106 --seat 1 --steps 70`，票 41）：
 * 上家暗杠、下家大明杠、对家吃与碰，宝牌指示牌 3 张。
 *
 * 语义闸门里有三条只有**杠**才验得到（暗杠不带来源 / 宝牌指示牌数与杠数 / 河牌数与巡目里那一项），
 * 而 29b 那一局里一个暗杠都没有。
 */
export const ankanPackage = load<DecisionPackage>("decision-ankan");

/**
 * **桁上有加杠**那一手（`janpo decide 92 --seat 1 --steps 49`，票 41）：
 * 自家一组加杠、上家一组加杠，宝牌指示牌 3 张。
 *
 * 加杠亮出来的头一张**仍然躺在别人的河里**（`Naki.fromKawa`），
 * 「同一牌种最多 4 张」那一条得知道这件事，否则每一组加杠都会被数出一张幻牌。
 */
export const kakanPackage = load<DecisionPackage>("decision-kakan");

/**
 * **自家立直已成立**那一手（`janpo decide 10 --seat 3 --steps 80 --opinionated`，票 49）：
 * 历史里有一行「你 第4巡 宣言立直」，之后还有十一张打牌。
 *
 * 「立直后全摸切」那一条不变量在均匀随机的语料上**永远空转**（票 42 实测：
 * 1..2000 号种子里只有 15 场立直成立），而一条永远执行不到的断言与一条从不失败的
 * 断言危害相同。这份固件来自**有主见的选手**（票 42），它让那一条在 `pnpm test` 里也真的开口。
 */
export const riichiPackage = load<DecisionPackage>("decision-riichi");

/**
 * **同一局里连续的 12 手**，同一个座位（`janpo decide 7 --seat 1 --sequence --steps 12`，票 29b）。
 *
 * 前缀的字节稳定性只有连续的几手才验得了：一手一份包看不出前缀在不在长。
 * 这一局里碰、吃、大明杠与杠宝牌都出得来，因此历史那一段的每种行都被真数据走过一遍。
 */
export const sequencePackages = load<DecisionPackage[]>("decision-sequence");

/** 合法输出：模型调 choose_action 选了 id=2。 */
export const legal = load<AskResult>("ask-legal");

/** Assisted 档（票 24）：**同一份决策包**的带脚手架 prompt 问出来的那一次。 */
export const assistedAnswer = load<AskResult>("ask-assisted");

/** 危险度那一手（票 25）：**同一档、另一手**，prompt 里多了安全度排序那一节。 */
export const dangerAnswer = load<AskResult>("ask-danger");

/** 格式跑偏：模型没调工具，只回了一段话。 */
export const textOnly = load<AskResult>("ask-text-only");

/** 超时：abort 之后 `stopReason: "aborted"`。 */
export const aborted = load<AskResult>("ask-aborted");

/** provider 报错：坏 key 的 401。 */
export const badKey = load<AskResult>("ask-error-bad-key");

export const seat: SeatConfig = {
  provider: "deepseek",
  model: "deepseek-v4-flash",
  api_key: "sk-用例里的假 key（没有请求会发出去）",
  timeout_ms: 30000,
  thinking: "off",
  tier: "bare",
  // 官方八家用不上 baseUrl（票 30）。
  base_url: "",
  // 不填就是默认模板、没有人格（票 31）。
  persona: "",
  template: "",
};

/** 同一个座位，**只把脚手架档位拨到 Assisted**（票 24：档位是座位级配置）。 */
export const assistedSeat: SeatConfig = { ...seat, tier: "assisted" };

/** 接本地 Ollama 的座位（票 30）：**provider 是 `custom`、没有 key、有 baseUrl**。 */
export const localSeat: SeatConfig = {
  ...seat,
  provider: CUSTOM_PROVIDER,
  model: "qwen3:8b",
  api_key: "",
  base_url: "http://localhost:11434/v1",
};

export function request(
  decision: DecisionPackage,
  overrides: Partial<DecideRequest> = {},
): DecideRequest {
  return { decision, seat, retry_limit: 2, ...overrides };
}

/**
 * 回放录制的响应：第 N 次问话拿第 N 条，问超了就一直拿最后一条
 * （「每次都失败」的用例靠这个）。
 *
 * `prompts` 记下每次那两条消息拼起来的全文（与 `renderPrompt` 同一份），
 * `messages` 记下它们各自的那一半——票 31 把固定 preamble 挑进了 system 消息。
 */
export function replay(...results: AskResult[]): {
  ask: Ask;
  prompts: string[];
  messages: { system: string; user: string }[];
} {
  const prompts: string[] = [];
  const messages: { system: string; user: string }[] = [];
  let index = 0;

  const ask: Ask = async (asked) => {
    prompts.push(`${asked.system}\n\n${asked.prompt}`);
    messages.push({ system: asked.system, user: asked.prompt });
    const result = results[Math.min(index, results.length - 1)];
    index += 1;
    return result;
  };

  return { ask, prompts, messages };
}
