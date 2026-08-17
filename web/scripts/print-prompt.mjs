// 两档 prompt 的**调试出口**（票 24）：同一个局面，Bare 与 Assisted 各打一份出来肉眼比。
//
// 脚手架强度是个实验变量，不是写死的行为——所以「换一档看看 prompt 变成什么样」不该要改代码。
//
// 跑法（在 web/ 下）：
//   pnpm run prompt                                  默认种子 1177 的第 8 手，两档都打
//   pnpm run prompt -- --seed 42 --steps 0           换局面
//   pnpm run prompt -- --tier assisted               只看一档
//   pnpm run prompt -- --package tests/fixtures/agent/decision-dahai.json
//                                                    直接读一份决策包 JSON（不跑 dotnet）
//   pnpm run prompt -- --diff                        只打两档的差异（Assisted 多出来那一节）
//   pnpm run prompt -- --persona "你打得很凶。"           换一个人格（票 31）
//   pnpm run prompt -- --template my-template.json   换一份模板（一段 JSON，或直接写 JSON 本身）
//
// 决策包由 `janpo decide` 生成，**与浏览器里跨界的那一份是同一个 encoder 的产物**；
// 人格与模板走的也是座位配置那条路（`resolveTemplate`）——**页面上填什么，这里就看得到什么**。

import { spawnSync } from "node:child_process";
import { existsSync, readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { renderPrompt } from "../src/agent/prompt.ts";
import { renderVersion } from "../src/agent/render-version.ts";
import { resolveTemplate } from "../src/agent/template.ts";

const here = dirname(fileURLToPath(import.meta.url));
const repo = resolve(here, "../..");

const TIERS = ["bare", "assisted", "tool_search"];

function parseArguments(argv) {
  const parsed = {
    seed: "1177",
    steps: "8",
    seat: null,
    tier: "both",
    package: null,
    persona: "",
    template: "",
    diff: false,
  };
  for (let index = 0; index < argv.length; index += 1) {
    const flag = argv[index];
    if (flag === "--") {
      // pnpm 会把分隔符自己也传下来。
    } else if (flag === "--diff") {
      parsed.diff = true;
    } else if (flag.startsWith("--")) {
      const key = flag.slice(2);
      if (!(key in parsed)) throw new Error(`不认识的参数：${flag}`);
      index += 1;
      parsed[key] = argv[index];
    } else {
      throw new Error(`不认识的参数：${flag}`);
    }
  }
  if (parsed.tier !== "both" && !TIERS.includes(parsed.tier)) {
    throw new Error(`--tier 只认 both 与 ${TIERS.join(" / ")}`);
  }
  return parsed;
}

/** 决策包：给了 --package 就读文件，否则跑 `janpo decide`。 */
function decisionPackage(parsed) {
  if (parsed.package !== null) {
    return JSON.parse(readFileSync(resolve(process.cwd(), parsed.package), "utf8"));
  }

  const args = [
    "run",
    "--project",
    "src/Janpo.Cli",
    "--configuration",
    "Release",
    "--",
    "decide",
    parsed.seed,
    "--steps",
    parsed.steps,
  ];
  if (parsed.seat !== null) args.push("--seat", parsed.seat);

  const run = spawnSync("dotnet", args, { cwd: repo, encoding: "utf8" });
  if (run.status !== 0) {
    throw new Error(`janpo decide 跑不出来：${run.stderr || run.stdout}`);
  }
  return JSON.parse(run.stdout);
}

const parsed = parseArguments(process.argv.slice(2));
const decision = decisionPackage(parsed);
const source =
  parsed.package === null
    ? `janpo decide ${parsed.seed} --steps ${parsed.steps}${parsed.seat === null ? "" : ` --seat ${parsed.seat}`}`
    : parsed.package;

/** `--template` 可以是一个文件名，也可以直接写那段 JSON。 */
function templateText(given) {
  if (given === "") return "";
  const path = resolve(process.cwd(), given);
  return existsSync(path) ? readFileSync(path, "utf8") : given;
}

// 模板从座位配置来（票 31）：这里造的就是页面上那两格填完之后的样子。
const template = resolveTemplate({
  persona: parsed.persona,
  template: templateText(parsed.template),
});
const prompts = new Map(TIERS.map((tier) => [tier, renderPrompt(decision, tier, null, template)]));

if (parsed.diff) {
  // 两档只差一节（`prompt.ts` 的 `frame`），把那一节单独打出来。
  const bare = prompts.get("bare");
  const only = prompts
    .get("assisted")
    .split("\n\n")
    .filter((block) => !bare.includes(block));
  console.log(`决策包：${source}（座位 ${decision.seat}，${decision.actions.length} 条动作）`);
  console.log(`两档的差异（Assisted 多出来 ${only.join("\n\n").length} 字）：\n`);
  console.log(only.join("\n\n"));
} else {
  for (const tier of parsed.tier === "both" ? ["bare", "assisted"] : [parsed.tier]) {
    const prompt = prompts.get(tier);
    console.log(`${"=".repeat(78)}`);
    console.log(
      `档位 ${tier}　渲染版本 ${renderVersion(template)}　决策包：${source}　座位 ${decision.seat}　${prompt.length} 字`,
    );
    console.log(`${"=".repeat(78)}`);
    console.log(prompt);
    console.log();
  }
}
