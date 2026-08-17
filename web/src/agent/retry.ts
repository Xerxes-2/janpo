/**
 * **这个错误重试有没有意义**（票 47）。
 *
 * 票 23 让四类失败一视同仁地重试 2 次，理由是「分支越少越好解释」，并明写
 * 「真要省，判据应当是**这个错误重试有没有意义**，那需要 provider 错误的分类，不是一个 if」
 * （决策 23-6）。27 号验收量出了代价：断电演习一整场 **85 手 × 3 = 255 次请求全部 401，
 * 其中 170 次是纯白烧的** —— 第一次 401 就已经注定后两次也是。这个文件就是那条判据。
 *
 * **判据是意义，不是状态码。** 落到 HTTP 上只剩一句话，而且用的是 HTTP 自己的语义：
 *
 * - **4xx 说的是「你这份请求不对」**。而重试发的是**同一份请求** —— 重问那一轮只在 prompt
 *   尾部多一句「上一次没被采用」，key、模型名、端点地址、工具 schema 一个字节都没变。
 *   同一份请求问同一个端点，答案当然也一样。
 * - **5xx 说的是「端点这一刻不行」**。那是 provider 那边的事，与我们这份请求无关，
 *   再问一遍完全可能就好了。
 * - **4xx 里有三个例外**（408 / 425 / 429）：它们说的不是「请求不对」而是「这一刻不行」，
 *   因此跟 5xx 一伙。**429 的取舍另见 DECISIONS「## 47」**（值得重试，但不做退避）。
 *
 * 认不出状态码的（`Connection error.`、`Failed to fetch`、provider 自己的话）一律
 * **当作值得重试**：判据是「再问一遍**可能**不一样」，分不清的时候「可能」就成立。
 * 错向这一边的代价是多两个请求，错向另一边的代价是把本来救得回来的一手直接判死。
 *
 * **顺序：分类在打码（票 36）之前。** 打码那道在 `decideWith` 的**出口**，分类在循环**里面**。
 * 两者不耦合：分类只看状态码与几个类别词，而打码换掉的是 key 与 baseUrl 的字面量 ——
 * 打没打码它都认得出。反过来做才会出事（`[API key 已打码]` 反而更难认）。
 *
 * **这一层不改兜底的行为**：交不出动作仍然由引擎的 `Fallback.action` 代打。变的只有
 * 「交不出来之前问了几次」，以及**交不出来那句话怎么说**。
 */

/**
 * 一次失败**值不值得再问一遍**。
 *
 * 不值得的那一支带着 `because`——**「没重试」与「少试了」在页面上必须一眼分得开**：
 * 这句判据会连着报错原文一起印在牌桌上那一手的兜底原因里，也会进牌谱的 `fallback`。
 */
export type Retry = { worth: true } | { worth: false; because: string };

/** 值得重试。**它是默认的那一边**：判不出来就归这里。 */
export const WORTH: Retry = { worth: true };

/**
 * 4xx 里说的不是「你这份请求不对」、而是「这一刻不行」的那三个。**例外要点名**，
 * 否则「4xx = 请求的事」这条规则会把它们一起误伤。
 */
const TIMING_4XX = new Set([
  408, // Request Timeout —— 端点等我们等超时了
  425, // Too Early —— 早了，等等再来
  429, // Too Many Requests —— 限流
]);

/**
 * 点名那几档的**判据原话**：为什么再问一遍还是一样。它是给人看的，会印在牌桌与牌谱里。
 *
 * 没点名的 4xx 走 `OTHER_4XX` —— 判据是同一条（「重问发的是同一份请求」），
 * 只是说不出更具体的话。**不为了凑齐 HTTP 的状态码表而编话**。
 */
const NAMED_4XX = new Map<number, string>([
  [400, "这份请求端点不接受（参数或 schema 不对），重问发的还是同一份"],
  [401, "这个端点不认这把 key"],
  [402, "这个账户余额不够，得有人去充值"],
  [403, "这把 key 没有这个模型或这个地区的权限"],
  [404, "这个模型名或这个端点地址不存在"],
  [413, "这份 prompt 超过了这个模型收得下的长度"],
  [422, "这份请求端点读懂了但不接受，重问发的还是同一份"],
]);

const OTHER_4XX = "端点说这份请求它不接受，而重问发的是同一份请求";

/**
 * **一个请求都还没发出去就判定失败的那一档**：`piai.ts` 的 `wire` 在接线阶段认不出
 * provider、或者那一家的模型目录里没有这个模型名。再问一遍读的还是**同一份座位配置**。
 *
 * 这里按那两句话的特征认它们，而**不从 `piai.ts` import** —— 那个文件一 import 就把
 * pi-ai 的 SDK 拖进来，而这一层要能在几十毫秒里被用例过一遍（同 `endpoint.ts` 的理由）。
 * `retry.test.ts` 里有两条**真的调 `piAsk`**（这两条路零网络）把那两句话钉住，
 * 因此措辞漂了会有人当场知道。
 */
const UNWIRED: [RegExp, string][] = [
  [/^不认识的 provider/, "这个 provider 不在名单里，重问读的还是同一份座位配置"],
  [/的模型目录里没有/, "这一家的模型目录里没有这个模型名，重问还是没有"],
];

/**
 * 报错原文里的 HTTP 状态码。pi-ai 把它写成三种形态之一（`utils/error-body.ts` 的
 * `formatProviderError`）：`401: {…}`（它自己拼的，DeepSeek 走这条）、`401 {…}`
 * （SDK 的 message 自带正文，Anthropic / Google 走这条）、`OpenAI API error (401): …`
 * （带前缀的那种）。自定义端点还会被 `explainFailure` 在前面**加一句中文**，
 * 因此**不许锚在开头**（`ask-error-no-model` 那份固件就是这个形状）。
 *
 * 只认「前后都是分隔符」的三位数，且只收 400–599：地址里的 `127.0.0.1` 与端口
 * `:11434` 因此认不成状态码。取第一个够格的 —— pi-ai 把状态码放在最前面。
 */
function statusOf(message: string): number | null {
  for (const found of message.matchAll(/(?:^|[\s(（：:])(\d{3})(?=[\s):）：,，。]|$)/g)) {
    const status = Number.parseInt(found[1], 10);
    if (status >= 400 && status <= 599) return status;
  }
  return null;
}

/**
 * 一条 provider 报错**值不值得再问一遍**。
 *
 * `null`（provider 报了错却没给原因）当作值得重试：什么都不知道的时候，「可能不一样」成立。
 */
export function retryOf(errorMessage: string | null): Retry {
  if (errorMessage === null) return WORTH;

  const unwired = UNWIRED.find(([pattern]) => pattern.test(errorMessage));
  if (unwired !== undefined) return { worth: false, because: unwired[1] };

  const status = statusOf(errorMessage);
  if (status === null || status >= 500 || TIMING_4XX.has(status)) return WORTH;

  return { worth: false, because: NAMED_4XX.get(status) ?? OTHER_4XX };
}
