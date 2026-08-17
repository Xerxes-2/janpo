// Agent 层用例固件的**录制器**（票 23，票 24 加了 Assisted 档那一条）。
// **手动跑，不进 CI**：CI 里绝不调真实 API。
//
// 它把真模型的回答录成 `tests/fixtures/agent/ask-*.json`（`AskResult` 的形状），
// 用例回放它们，因此「合法输出 / 越界 id / 格式跑偏 / 超时」几条路径在 CI 里是确定性的。
//
// 跑法（key 从文件读，**绝不写进代码或提交**）：
//   JANPO_KEY_FILE=/tmp/deepseek_key node scripts/record-agent-fixtures.mjs
//   JANPO_KEY_FILE=/tmp/deepseek_key node scripts/record-agent-fixtures.mjs ask-assisted
//                                        ^ 只重录点名的那几份，别的原样留着
//
// **最后两份不要 key**（票 47）：它们录的是限流与服务端故障，这两种响应**问真 provider 是要不来的**，
// 由本机假端点（`fake-endpoint.mjs --fail`）回一个真的 HTTP 响应，仍然走 `piAsk` 与 pi-ai 的适配器。
//   node scripts/record-agent-fixtures.mjs ask-error-rate-limited ask-error-server ask-error-no-model
//
// 重录之后请逐个看 diff：这些文件是「模型真的这么答过」的证据，不是随手编的。

import { spawn } from "node:child_process";
import { existsSync, readFileSync, writeFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { createModels } from "@earendil-works/pi-ai";
import { deepseekProvider } from "@earendil-works/pi-ai/providers/deepseek";
import { CUSTOM_PROVIDER } from "../src/agent/endpoint.ts";
import { piAsk } from "../src/agent/piai.ts";
import { promptMessages } from "../src/agent/prompt.ts";

const here = dirname(fileURLToPath(import.meta.url));
const fixtures = resolve(here, "../tests/fixtures/agent");

const keyFile = process.env.JANPO_KEY_FILE ?? "/tmp/deepseek_key";
// 要 key 的那几份才读它：本机假端点那两份（票 47）一个字节都不要，
// 没 key 的机器上也该重录得了。
const apiKey = existsSync(keyFile) ? readFileSync(keyFile, "utf8").trim() : "";
const MODEL = process.env.JANPO_MODEL ?? "deepseek-v4-flash";

const decision = JSON.parse(readFileSync(resolve(fixtures, "decision-dahai.json"), "utf8"));

/** 带危险度的那一手（票 25）：对家有副露，排序里现物 / 筋 / 无依据三档都有。 */
const dangerDecision = JSON.parse(readFileSync(resolve(fixtures, "decision-danger.json"), "utf8"));

const seat = (overrides) => ({
  provider: "deepseek",
  model: MODEL,
  api_key: apiKey,
  timeout_ms: 30000,
  thinking: "off",
  tier: "bare",
  // 默认模板、没有人格（票 31）：固件录的是「模型对**默认那一份 prompt** 怎么答」。
  persona: "",
  template: "",
  ...overrides,
});

/** 两条消息摊进 `piAsk` 的请求（它的字段名是 `system` 与 `prompt`）。 */
const messages = (rendered) => ({ system: rendered.system, prompt: rendered.user });

/** 不要 key 的那几份（票 47）：本机假端点回的真 HTTP 错误。 */
const FROM_FAKE_ENDPOINT = ["ask-error-rate-limited", "ask-error-server", "ask-error-no-model"];

/** 点名了就只录点名的那几份；一份都没点名就全录。 */
const named = process.argv.slice(2).filter((argument) => !argument.startsWith("-"));
const wanted = (name) => named.length === 0 || named.includes(name);

async function record(name, note, run) {
  if (!wanted(name)) return;
  if (apiKey === "" && !FROM_FAKE_ENDPOINT.includes(name)) {
    console.log(`${name}: 跳过（${keyFile} 不在，这一份要真 key）`);
    return;
  }
  const result = await run();
  const path = resolve(fixtures, `${name}.json`);
  writeFileSync(path, `${JSON.stringify({ _note: note, ...result }, null, 2)}\n`);
  console.log(`${name}: stopReason=${result.stopReason} ${result.latencyMs}ms`);
}

// 两条消息（票 31）：固定 preamble 进 system，历史 + 现况进 user。
// **录制走的与生产同一个渲染出口**，否则录下来的答案答的不是真会发出去的那份 prompt。
const bare = promptMessages(decision, "bare", null);
const actionIds = decision.actions.map((option) => String(option.id));

// 1) 合法输出：模型调 choose_action，给一个包里有的 id。
await record(
  "ask-legal",
  `真实录制：DeepSeek ${MODEL} 对 decision-dahai.json 的单轮 tool call。`,
  () => piAsk({ seat: seat(), ...messages(bare), actionIds }),
);

// 2) Assisted 档（票 24）：**同一份决策包、同一个模型**，prompt 换成带脚手架的那一档。
// 它与 ask-legal 是一对：两档的差别只有 prompt，回答却各是各的。
await record(
  "ask-assisted",
  `真实录制：DeepSeek ${MODEL} 对 decision-dahai.json 的 Assisted 档 prompt（带向听数、有效牌与逐张试打的进退向）的单轮 tool call。`,
  () =>
    piAsk({
      seat: seat({ tier: "assisted" }),
      ...messages(promptMessages(decision, "assisted", null)),
      actionIds,
    }),
);

// 3) Assisted 档的危险度（票 25）：同一档、另一手。选这一手是因为它有威胁的家（对家副露），
// 因此 prompt 里多一节安全度排序——录它是为了看**模型用不用得上那一节**。
await record(
  "ask-danger",
  `真实录制：DeepSeek ${MODEL} 对 decision-danger.json 的 Assisted 档 prompt（带危险度排序）的单轮 tool call。`,
  () =>
    piAsk({
      seat: seat({ tier: "assisted" }),
      ...messages(promptMessages(dangerDecision, "assisted", null)),
      actionIds: dangerDecision.actions.map((option) => String(option.id)),
    }),
);

// 4) 格式跑偏：同一段 prompt，但**不注册工具**，模型只能回一段话。
await record(
  "ask-text-only",
  "真实录制：不注册工具时模型只回文字（Agent 层判为格式跑偏）。",
  async () => {
    const started = performance.now();
    const models = createModels();
    models.setProvider(deepseekProvider());
    const model = models.getModel("deepseek", MODEL);
    const message = await models.completeSimple(
      model,
      {
        systemPrompt: bare.system,
        messages: [{ role: "user", content: bare.user, timestamp: Date.now() }],
      },
      { apiKey },
    );
    return {
      stopReason: message.stopReason,
      toolCall: null,
      text: message.content
        .filter((block) => block.type === "text")
        .map((block) => block.text)
        .join(""),
      // 这一条的座位 thinking 是 off，因此它恒为 null（票 26）。
      thinking: null,
      errorMessage: message.errorMessage ?? null,
      latencyMs: Math.round(performance.now() - started),
      usage: { input: message.usage.input, output: message.usage.output },
    };
  },
);

// 5) 超时：把座位的超时设成 300 ms，abort 掉一次真实请求。
await record(
  "ask-aborted",
  "真实录制：timeout_ms=300 时的 abort（票 18 实测：它是值不是异常）。",
  () => piAsk({ seat: seat({ timeout_ms: 300 }), ...messages(bare), actionIds }),
);

// 6) provider 报错：故意用一把坏 key（**断电演习**在浏览器里的等价物）。
await record("ask-error-bad-key", "真实录制：坏 key 的 401（stopReason=error，不抛异常）。", () =>
  piAsk({ seat: seat({ api_key: "sk-invalid-key-for-fixture" }), ...messages(bare), actionIds }),
);

/**
 * 本机假端点起一个，固定回 `status`，拿 `piAsk` 真问一次（票 47）。
 *
 * **这仍然是录制，不是手编**：真的 HTTP 响应、真的 pi-ai `openai-completions` 适配器、
 * 真的 `piAsk`——录下来的 `errorMessage` 就是这条链路拼出来的那一句。换成手编的话，
 * 「pi-ai 把状态码写成 `401: {…}`」这个分类器唯一的输入假设就变成了无人看着的东西。
 */
async function fromFakeEndpoint(status) {
  const port = 4290 + (status % 100);
  const endpoint = spawn(
    process.execPath,
    [
      resolve(here, "fake-endpoint.mjs"),
      "--port",
      String(port),
      "--fail",
      String(status),
      "--quiet",
    ],
    { stdio: ["ignore", "pipe", "inherit"] },
  );
  await new Promise((done) => endpoint.stdout.once("data", done));

  try {
    return await piAsk({
      seat: seat({
        provider: CUSTOM_PROVIDER,
        model: "no-such-model",
        api_key: "",
        base_url: `http://127.0.0.1:${port}/v1`,
      }),
      ...messages(bare),
      actionIds,
    });
  } finally {
    endpoint.kill();
  }
}

// 7) 限流（票 47）：**「值得重试」那一类里唯一拿不到真样本的一档**——限流不听人调度。
// 它与 401 是一对：两者都是 `stopReason: "error"` 的 4xx，分类器却必须把它俩分开。
await record(
  "ask-error-rate-limited",
  "真实录制：本机假端点固定回 429（`fake-endpoint.mjs --fail 429`），走真的 piAsk 与 pi-ai 适配器。",
  () => fromFakeEndpoint(429),
);

// 8) 服务端故障（票 47）：5xx 同样问不来。它守的是分类器那条「4xx / 5xx 分两边」的另一边。
await record(
  "ask-error-server",
  "真实录制：本机假端点固定回 503（`fake-endpoint.mjs --fail 503`），走真的 piAsk 与 pi-ai 适配器。",
  () => fromFakeEndpoint(503),
);

// 9) 模型名不存在（票 47）：官方八家这一档在**发请求之前**就被模型目录拦下了，
// 拿不到 provider 的话；自定义端点（票 30）没有目录，因此这一档是真的从端点回来的。
await record(
  "ask-error-no-model",
  "真实录制：本机假端点固定回 404「没这个模型」（`fake-endpoint.mjs --fail 404`），走真的 piAsk 与 pi-ai 适配器。",
  () => fromFakeEndpoint(404),
);
