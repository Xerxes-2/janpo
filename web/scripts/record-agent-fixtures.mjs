// Agent 层用例固件的**录制器**（票 23）。**手动跑，不进 CI**：CI 里绝不调真实 API。
//
// 它把真模型的四种回答录成 `tests/fixtures/agent/ask-*.json`（`AskResult` 的形状），
// 用例回放它们，因此「合法输出 / 越界 id / 格式跑偏 / 超时」四条路径在 CI 里是确定性的。
//
// 跑法（key 从文件读，**绝不写进代码或提交**）：
//   JANPO_KEY_FILE=/tmp/deepseek_key node scripts/record-agent-fixtures.mjs
//
// 重录之后请逐个看 diff：这些文件是「模型真的这么答过」的证据，不是随手编的。

import { readFileSync, writeFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { createModels } from "@earendil-works/pi-ai";
import { deepseekProvider } from "@earendil-works/pi-ai/providers/deepseek";
import { piAsk } from "../src/agent/piai.ts";
import { renderPrompt } from "../src/agent/prompt.ts";

const here = dirname(fileURLToPath(import.meta.url));
const fixtures = resolve(here, "../tests/fixtures/agent");

const keyFile = process.env.JANPO_KEY_FILE ?? "/tmp/deepseek_key";
const apiKey = readFileSync(keyFile, "utf8").trim();
const MODEL = process.env.JANPO_MODEL ?? "deepseek-v4-flash";

const decision = JSON.parse(readFileSync(resolve(fixtures, "decision-dahai.json"), "utf8"));

const seat = (overrides) => ({
  provider: "deepseek",
  model: MODEL,
  api_key: apiKey,
  timeout_ms: 30000,
  thinking: "off",
  tier: "bare",
  ...overrides,
});

function save(name, note, result) {
  const path = resolve(fixtures, `${name}.json`);
  writeFileSync(path, `${JSON.stringify({ _note: note, ...result }, null, 2)}\n`);
  console.log(`${name}: stopReason=${result.stopReason} ${result.latencyMs}ms`);
}

const prompt = renderPrompt(decision, "bare", null);
const actionIds = decision.actions.map((option) => String(option.id));

// 1) 合法输出：模型调 choose_action，给一个包里有的 id。
save(
  "ask-legal",
  `真实录制：DeepSeek ${MODEL} 对 decision-dahai.json 的单轮 tool call。`,
  await piAsk({ seat: seat(), prompt, actionIds }),
);

// 2) 格式跑偏：同一段 prompt，但**不注册工具**，模型只能回一段话。
{
  const started = performance.now();
  const models = createModels();
  models.setProvider(deepseekProvider());
  const model = models.getModel("deepseek", MODEL);
  const message = await models.completeSimple(
    model,
    { messages: [{ role: "user", content: prompt, timestamp: Date.now() }] },
    { apiKey },
  );
  save("ask-text-only", "真实录制：不注册工具时模型只回文字（Agent 层判为格式跑偏）。", {
    stopReason: message.stopReason,
    toolCall: null,
    text: message.content
      .filter((block) => block.type === "text")
      .map((block) => block.text)
      .join(""),
    errorMessage: message.errorMessage ?? null,
    latencyMs: Math.round(performance.now() - started),
    usage: { input: message.usage.input, output: message.usage.output },
  });
}

// 3) 超时：把座位的超时设成 300 ms，abort 掉一次真实请求。
save(
  "ask-aborted",
  "真实录制：timeout_ms=300 时的 abort（票 18 实测：它是值不是异常）。",
  await piAsk({ seat: seat({ timeout_ms: 300 }), prompt, actionIds }),
);

// 4) provider 报错：故意用一把坏 key（**断电演习**在浏览器里的等价物）。
save(
  "ask-error-bad-key",
  "真实录制：坏 key 的 401（stopReason=error，不抛异常）。",
  await piAsk({ seat: seat({ api_key: "sk-invalid-key-for-fixture" }), prompt, actionIds }),
);
