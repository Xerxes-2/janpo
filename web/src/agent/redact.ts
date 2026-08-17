/**
 * 打码（票 36）：**座位配置里的两样东西，一个字节都不许离开这一层** ——
 * API key，与自定义端点的 baseUrl。
 *
 * 起因是票 34 实测出来的一条真通道：provider 的报错原文**原样**流进
 * `DecisionRecord.fallback`、`output.error_message`、以及**下一次重试的 prompt**
 * （牌谱存最后一次的那一份）。官方那八家都打码（DeepSeek 只回末 4 位），
 * 但票 30 放开的**自定义端点是用户自建的网关**，回显什么完全由它决定 ——
 * 原样把收到的 `Authorization` 抄进 401 是完全可能的。而牌谱是**唯一的可分享物**
 * （ADR-0002），README 又对用户明写着「导出的牌谱里不含 key」。
 *
 * **只在一处做**：`loop.ts` 的 `decideWith` 出口，回执整个过一遍。不在每个 sink
 * 各打一遍 —— 那种做法漏一个就等于没做，而 sink 是会长出来的（三处是今天数的，
 * 明天页面上多显示一个字段就是第四处）。
 *
 * **抹的是字面量，不是「像 key 的正则」**：我们手里有确切的 key 与 baseUrl，
 * 精确替换即可。猜的那种写法既会漏（自建网关的 key 长什么样谁也不知道），
 * 又会误伤（牌谱里全是模型说的话）。
 */

import type { SeatConfig } from "./types.ts";

/**
 * 打码留下的那两个记号。**它俩是产品面的字**：会出现在页面上那句报错里，
 * 也会进牌谱（而牌谱是唯一的可分享物），因此要读得懂。
 *
 * **`web/scripts/verify-redaction.mjs` 里另写了一份一字不差的，改一处必须改另一处**。
 * 那不是顺手抄的重复代码，是故意的：闸门与被验的实现各写各的，
 * 闸门从实现里 `import` 常量的话，换成空串它照样绿。就让它在改措辞时红一次
 * ——那正是要人看一眼的时刻（这两个字串会被人读到）。
 */
export const KEY_MASK = "[API key 已打码]";

/** 自定义端点地址的替身（提案 34-B）。**同上：闸门里那份要一块改**。 */
export const ENDPOINT_MASK = "[端点地址已打码]";

/** 一条要抹掉的字面量，与它换上的替身。 */
interface Secret {
  literal: string;
  mask: string;
}

/**
 * 一个配置项的几种**写法**：原样、去掉前后空白、去掉末尾斜杠。
 *
 * 三种都要，因为报错原文里出现的可能是其中任何一种：header 带出去的是原样那份
 * （`keyFor` 不 trim），而 pi-ai 请求的地址是 `readBaseUrl` 规整过的那份。
 *
 * 每种再加一份 **JSON 转义后的形态**：`output` 字段是 `JSON.stringify` 出来的，
 * key 里若带引号或反斜杠，它在那串里长的是另一个样子。
 */
function spellings(raw: string): string[] {
  const trimmed = raw.trim();
  const forms = [raw, trimmed, trimmed.replace(/\/+$/, "")];
  const escaped = forms.map((form) => JSON.stringify(form).slice(1, -1));

  return [...new Set([...forms, ...escaped])].filter((form) => form !== "");
}

/**
 * 这个座位上要抹的东西。**空的那一格什么都不抹**：空串当字面量匹配会把记号插进每个
 * 字符之间，那是把报错毁掉，不是打码。
 *
 * **不按 provider 分叉**：官方八家用不上 baseUrl，但那一格里填着什么就抹什么 ——
 * 少一个「只有 custom 才抹」的开关，就少一处会写错的地方。
 */
function secretsOf(seat: SeatConfig): Secret[] {
  const secrets = [
    ...spellings(seat.api_key).map((literal) => ({ literal, mask: KEY_MASK })),
    ...spellings(seat.base_url).map((literal) => ({ literal, mask: ENDPOINT_MASK })),
  ];

  // 长的先抹：同一样东西的几种写法互为前缀（`…/v1/` 与 `…/v1`），短的先抹会剩下尾巴。
  return secrets.sort((left, right) => right.literal.length - left.literal.length);
}

/** 一段文本里的那几个字面量，逐个换成替身。 */
function scrub(text: string, secrets: Secret[]): string {
  return secrets.reduce((done, secret) => done.replaceAll(secret.literal, secret.mask), text);
}

/**
 * 一份东西里的每一个字符串都过一遍打码。**深走、不认字段名**：今天回执上有八个字符串字段，
 * 明天多一个也自动盖住 —— 「记得给新字段也打一遍」不该是靠人记的事。
 *
 * 数、null、数组结构原样保留（`action_ids` 与 `usage` 因此一字不变）。
 */
export function redactSecrets<T>(value: T, seat: SeatConfig): T {
  const secrets = secretsOf(seat);
  if (secrets.length === 0) return value;

  const walk = (node: unknown): unknown => {
    if (typeof node === "string") return scrub(node, secrets);
    if (Array.isArray(node)) return node.map(walk);
    if (node !== null && typeof node === "object") {
      return Object.fromEntries(Object.entries(node).map(([name, field]) => [name, walk(field)]));
    }
    return node;
  };

  return walk(value) as T;
}
