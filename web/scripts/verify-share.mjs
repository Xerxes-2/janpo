// URL 分享载荷的**无头闸门**（票 77）：在浏览器里真压一次、真编一次、真解一次，
// 再把解出来的那份牌谱交回引擎 fold（ADR-0002 的回放）。
//
// 验四件事：
//   1. **真往返**：牌谱 → 变换 → 压 → 编 → 解 → `Paifu.decoder` → `Replay`，
//      事件流逐条相同、终局点数与顺位相同；
//   2. **载荷放得进 URL hash**：`+` `/` `=` 一个都不出现（出现了就得再转义一层）；
//   3. **反向自证**：把载荷**逐个位置**各改坏一个字符，每一次要么当场红在「载荷读不动」，
//      要么解出来与原文**逐字相同**（那几位落在末尾不承载信息的填充位上）。
//      **绝不许出现「解得开，但是另一份牌谱」**——`deflate-raw` 会（实测 7,463 位里 5,666 位），
//      `deflate` 的 Adler-32 不会，这就是 `payload.ts` 里那个 `FORMAT` 的由来；
//   4. **载荷里没有审计那三样**（票 34 那道 key 闸门的同族）：拿一份带 thinking、带 prompt 尾部、
//      又拌了假 key 的牌谱做载荷，解出来的那份里三样一个都不在。**它自带阳性对照**：
//      上路前那份必须真的带着这三样，否则这条断言什么都没证明。
//
// 顺带把**长度记账**印出来：东风战与半庄各打一整场，压缩前 / 压缩后 / base64url 后三个数。
//
// 语料是引擎现打的（`ShareCheck.sample`：四家随机、同一种子必然同一场），**这一道不碰页面**
// ——地址栏与按钮是票 78 的地盘。全程本机，一个网络请求都不发，因此它进 CI。
//
// 跑法（它也是 `verify-browser.mjs` 里的两趟：正跑与反向自证）：
//   cd web && pnpm run fable && pnpm run verify:share
//   node scripts/verify-share.mjs --poison    # 反向自证：这一趟**必须**红
//
// 选项：--seed N（默认 2088）、--no-sweep（跳过逐位置那一遍，只做单点腐蚀）、
//       --paifu <路径>（拿一份真导出的牌谱跑，报告里那组真实数就是这么来的）、
//       --poison（把「载荷里没有审计三样」那条按红一次，见下面 `poison`）。

import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { failure, isEntry, runStandalone } from "./browser-lane.mjs";
import { hostPage, retryOnReload } from "./serve.mjs";

/**
 * 拌进牌谱的那把 key（票 34 那把的同族）。**它是假的，且看一眼就知道是假的**——
 * 这道闸门要的只是「牌谱里确实有一把 key 可以夹带」，因此它是**写死的字面量**，
 * **绝不从 /tmp/deepseek_key 之类的地方读**。全 ASCII 是故意的：断言按字节找它，
 * 而 JSON 编码器可以把非 ASCII 写成 `\uXXXX`——那样夹带了也找不着。
 */
const FAKE_KEY = "sk-janpo-fake-key-SHARE-URL-bing-1c7e05";

/** 拌进去的 thinking 与 prompt 尾部：两段**只出现在审计数据里**的字串，找得到就是漏了。 */
const THINKING = "先数向听：这手牌现在是 2 向听，切 9 万最不亏——SHARE-URL-THINKING-MARK";
const PROMPT_TAIL = "【现在】东1局 0 本场，你是座位 1……SHARE-URL-TAIL-MARK";

/**
 * 一份**带审计数据**的牌谱：`ShareCheck.sample` 打出来的那一场，外加一条决策记录与一段
 * prompt 前置，三样标记各摆一处（thinking / prompt 尾部 / 那把假 key）。
 *
 * key 摆在 `output` 里不是随手放的：票 36 核出的夹带通道就是「provider 的报错原文流进
 * `output` / `fallback` / 重试那一轮的 prompt」。**这一份只认牌谱的字段名，不认牌局内容**。
 */
const audit = {
  decisions: [
    {
      turn: 1,
      seat: 1,
      prompt_tail: PROMPT_TAIL,
      render_version: "janpo-default@08fcaec3.4b9e57c0",
      action_ids: [0, 1],
      output: `{"stop_reason":"error","error_message":"401 Unauthorized: key=${FAKE_KEY}"}`,
      reason: "9 万是孤张",
      thinking: THINKING,
      attempts: 2,
      latency_ms: 21916,
      applied: 1,
      fallback: `provider 报错：401（收到的 key 是 ${FAKE_KEY}）`,
    },
  ],
  prompting: {
    tools: '[{"name":"choose_action","parameters":{"properties":{"action_id":{"enum":[]}}}}]',
    preambles: [
      {
        seat: 1,
        render_version: "janpo-default@08fcaec3.4b9e57c0",
        text: "你在打日本立直麻将（天凤规则，四人东）。",
      },
    ],
  },
};

/** 逐位置腐蚀时那个「换成下一个字符」的字母表：换完仍在 base64url 字符集里（不许靠字符集那道判读接住）。 */
const ALPHABET = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";

/** 一趟：现打一场（或读一份真牌谱）→ 拌审计数据 → 编 → 解 → 与原牌谱对照。 */
async function shareRun(page, options) {
  return retryOnReload(() =>
    page.evaluate(async ({ lengthWire, seed, audit, paifu }) => {
      const share = await import("./src/generated/Share.js");
      let text = paifu ?? share.ShareCheck_sample(lengthWire, seed);

      if (audit !== null) {
        // **接在后面而不是盖掉**：`--paifu` 那一趟喂的是真导出件，它自己那几条带真 thinking
        // 的记录正是长度记账要比的那一半。
        const doc = JSON.parse(text);
        doc.decisions = [...(doc.decisions ?? []), ...audit.decisions];
        doc.prompting = {
          tools: doc.prompting?.tools || audit.prompting.tools,
          preambles: [...(doc.prompting?.preambles ?? []), ...audit.prompting.preambles],
        };
        text = JSON.stringify(doc);
      }

      const encoded = JSON.parse(await share.ShareCheck_encode(text));
      if (encoded.error) return { full: text, encoded, opened: null, checked: null };

      // 两条路各走一遍，因为票 78 两条都要用：`read` 停在**原文**那一站（闸门按字节查
      // 审计三样），`check` 走的是 `Share.ofPayload`（直接变成牌谱）。多解一次几 KB，不心疼。
      const opened = JSON.parse(await share.ShareCheck_read(encoded.payload));
      const checked = JSON.parse(await share.ShareCheck_check(text, encoded.payload));
      return { full: text, encoded, opened, checked };
    }, options),
  );
}

/** 单点腐蚀与截断**走整条 F# 那条路**：票 78 拿到的就是这两句话。 */
async function brokenPayloads(page, text, payload) {
  return retryOnReload(() =>
    page.evaluate(
      async ({ text, payload, alphabet }) => {
        const share = await import("./src/generated/Share.js");
        const middle = Math.floor(payload.length / 2);
        const swapped = alphabet[(alphabet.indexOf(payload[middle]) + 1) % alphabet.length];
        const cases = {
          改坏中间一个字符: payload.slice(0, middle) + swapped + payload.slice(middle + 1),
          末尾截掉八个字符: payload.slice(0, payload.length - 8),
          混进一个加号: `${payload.slice(0, middle)}+${payload.slice(middle + 1)}`,
          空的: "",
        };
        const reports = {};
        for (const [label, broken] of Object.entries(cases)) {
          reports[label] = JSON.parse(await share.ShareCheck_check(text, broken));
        }
        return reports;
      },
      { text, payload, alphabet: ALPHABET },
    ),
  );
}

/** 逐位置腐蚀：**这一遍直接问载荷那一层**（`payload.ts`），不必每次都把整份牌谱再解一遍。 */
async function sweep(page, payload) {
  return retryOnReload(() =>
    page.evaluate(
      async ({ payload, alphabet }) => {
        const { decodePayload } = await import("./src/share/payload.ts");
        const good = JSON.parse(await decodePayload(payload)).text;
        // `wrong` 是**不封顶的计数**，`samples` 才是报告里的那几条：两者分开，
        // 否则红的时候印出来的那个数字永远是 5（错的诊断比没有诊断更贵）。
        const report = {
          positions: payload.length,
          unreadable: 0,
          identical: 0,
          wrong: 0,
          samples: [],
        };

        for (let index = 0; index < payload.length; index += 1) {
          const swapped = alphabet[(alphabet.indexOf(payload[index]) + 1) % alphabet.length];
          const broken = payload.slice(0, index) + swapped + payload.slice(index + 1);
          const { text, error } = JSON.parse(await decodePayload(broken));

          if (error?.startsWith("载荷读不动：")) {
            report.unreadable += 1;
            continue;
          }
          if (error === undefined && text === good) {
            report.identical += 1;
            continue;
          }

          report.wrong += 1;
          if (report.samples.length < 5) {
            report.samples.push(
              error !== undefined
                ? `第 ${index} 位改坏之后红在了别处：${error}`
                : `第 ${index} 位改坏之后解出了另一份（${text.length} 字，头 48 字：${text.slice(0, 48)}）`,
            );
          }
        }

        return report;
      },
      { payload, alphabet: ALPHABET },
    ),
  );
}

/** 分享载荷那一道。返回的是失败清单（空 = 绿）。 */
export async function verifyShare(lane, options = {}) {
  const { seed = 2088, withSweep = true, poison = false, paifuPath = null } = options;

  const url = await lane.devUrl();
  const page = await lane.newPage();
  const problems = [];

  try {
    page.on("pageerror", (error) => problems.push(`[pageerror] ${error.message}`));
    page.on("console", (message) => {
      if (message.type() === "error") problems.push(`[console.error] ${message.text()}`);
    });

    await page.goto(hostPage(url), { waitUntil: "load" });

    const runs = [];
    for (const [label, lengthWire] of [
      ["东风战", "tonpuusen"],
      ["半庄", "hanchan"],
    ]) {
      const done = await shareRun(page, { lengthWire, seed, audit, paifu: null });
      // 引擎现打的那两场是**打完的**，因此「终局点数与顺位相同」那两条断言必须真的开口。
      runs.push({ label, mustEnd: true, ...done });
    }
    if (paifuPath !== null) {
      // 手跑那一趟喂的是真导出件，而导出得出来的牌谱**常常停在局中**（ADR-0002：
      // 回放是对前缀做 fold），因此不要求它有终局精算；两侧相同那两条照旧。
      const paifu = readFileSync(resolve(paifuPath), "utf8");
      const done = await shareRun(page, { paifu, audit, seed, lengthWire: null });
      runs.push({ label: `真牌谱 ${paifuPath}`, mustEnd: false, ...done });
    }

    console.log("");
    console.log("长度记账（牌谱全量（拌了审计标记）→ 只带棋谱 → deflate → base64url）：");

    for (const { label, mustEnd, full, encoded, opened, checked } of runs) {
      if (encoded.error) {
        problems.push(`${label}：编不出载荷——${encoded.error}`);
        continue;
      }

      const { payload } = encoded;
      const bytes = Math.floor((payload.length * 3) / 4);
      console.log(
        `  ${label}：事件 ${encoded.events} 条　全量 ${full.length} 字符 → 棋谱 ${encoded.kifu_chars} 字符` +
          ` → ${bytes} 字节 → **${payload.length} 字符**（压缩比 ${(encoded.kifu_chars / payload.length).toFixed(1)}:1）`,
      );

      // 2. 放得进 hash：这三个字符出现一个就得再转义一层。
      if (!/^[A-Za-z0-9_-]+$/.test(payload)) {
        const stray = [...payload].find((each) => !/[A-Za-z0-9_-]/.test(each));
        problems.push(
          `${label}：载荷里出现了 base64url 之外的字符「${stray}」，放进 hash 还得再转义一层`,
        );
      }

      if (checked === null || checked.error) {
        problems.push(`${label}：载荷解不回来——${checked?.error ?? "没有报告"}`);
        continue;
      }

      // 1. 真往返：事件流逐条相同、回放逐条相同、终局点数与顺位相同。
      console.log(
        `  　　往返：事件流逐条相同 = ${checked.same_events}　回放逐条相同 = ${checked.same_replay}` +
          `　终局点数 ${JSON.stringify(checked.scores)}　顺位 ${JSON.stringify(checked.juni)}`,
      );
      if (!checked.same_events)
        problems.push(`${label}：解出来的事件流与原牌谱不同：${checked.mismatch}`);
      if (!checked.same_replay) problems.push(`${label}：两侧回放出的事件流不同`);
      if (!checked.same_ruleset) problems.push(`${label}：解出来的规则集与原牌谱不同`);
      if (!checked.same_version) problems.push(`${label}：解出来的格式版本与原牌谱不同`);
      if (mustEnd && checked.scores === null)
        problems.push(`${label}：这一场没打完，终局点数无从对照`);
      if (JSON.stringify(checked.scores) !== JSON.stringify(checked.full_scores)) {
        problems.push(
          `${label}：终局点数不同——原牌谱 ${JSON.stringify(checked.full_scores)}，载荷里 ${JSON.stringify(checked.scores)}`,
        );
      }
      if (JSON.stringify(checked.juni) !== JSON.stringify(checked.full_juni)) {
        problems.push(
          `${label}：顺位不同——原牌谱 ${JSON.stringify(checked.full_juni)}，载荷里 ${JSON.stringify(checked.juni)}`,
        );
      }

      // 4. 审计三样一个都不在。**先验阳性对照**：上路前那份必须真的带着它们。
      if (encoded.decisions < 1 || encoded.thinking < 1 || encoded.preambles < 1) {
        problems.push(
          `${label}：上路前那份牌谱没带审计数据（决策记录 ${encoded.decisions} 条、带 thinking ${encoded.thinking} 条、` +
            `preamble ${encoded.preambles} 份）——「载荷里没有它们」于是什么都没证明`,
        );
      }
      for (const [what, mark] of [
        ["thinking", THINKING],
        ["prompt 尾部", PROMPT_TAIL],
        ["那把假 key", FAKE_KEY],
      ]) {
        if (!full.includes(mark)) {
          problems.push(`${label}：上路前那份牌谱里就找不到${what}——这条断言是空的`);
        }
      }

      if (opened.error) {
        problems.push(`${label}：载荷里那份原文取不出来——${opened.error}`);
        continue;
      }

      // `--poison` 拿**上路前**那份当解出来的那份：等于「变换没抹干净」，下面三条必须当场红。
      const shared = poison ? full : opened.text;
      for (const [what, mark] of [
        ["thinking", THINKING],
        ["prompt 尾部", PROMPT_TAIL],
        ["那把假 key", FAKE_KEY],
      ]) {
        if (shared.includes(mark)) problems.push(`${label}：载荷里出现了${what}`);
      }
      if (checked.decisions > 0)
        problems.push(`${label}：载荷里还剩 ${checked.decisions} 条决策记录`);
      if (checked.preambles > 0)
        problems.push(`${label}：载荷里还剩 ${checked.preambles} 份 preamble`);
      console.log(
        `  　　载荷：决策记录 ${checked.decisions} 条　preamble ${checked.preambles} 份　` +
          `thinking / prompt 尾部 / 假 key 一个都不在 = ${!shared.includes(THINKING) && !shared.includes(PROMPT_TAIL) && !shared.includes(FAKE_KEY)}`,
      );
    }

    // 3. 反向自证。先走整条 F# 那条路（票 78 看见的就是这几句），再逐位置扫一遍。
    const first = runs[0];
    if (!first.encoded.error) {
      console.log("");
      console.log("读不动时那几句话（走的是 `Share.ofPayload` 那条路）：");
      const broken = await brokenPayloads(page, first.full, first.encoded.payload);

      for (const [label, report] of Object.entries(broken)) {
        console.log(`  ${label}：${report.error ?? "（竟然读得动）"}`);
        if (!report.error) {
          problems.push(`载荷${label}之后竟然读得动：反向自证不成立`);
        } else if (!report.error.startsWith("载荷读不动：")) {
          problems.push(`载荷${label}之后红在了别处（该红在「载荷读不动」）：${report.error}`);
        }
      }

      if (withSweep) {
        const report = await sweep(page, first.encoded.payload);
        console.log(
          `  逐位置腐蚀 ${report.positions} 次：读不动 ${report.unreadable}，` +
            `解得开但与原文逐字相同 ${report.identical}，解出另一份 ${report.wrong}`,
        );
        if (report.wrong > 0) {
          problems.push(
            `改坏一个字符竟然解出了另一份牌谱 ${report.wrong} 次（分享链接被抄错一位就会悄悄换一场对局）：\n    ${report.samples.join("\n    ")}`,
          );
        }
        // 断言真的开过口（判据 3）：一条永远执行不到的断言与一条从不失败的断言，危害相同。
        if (report.unreadable < report.positions * 0.9) {
          problems.push(
            `只有 ${report.unreadable}/${report.positions} 个位置读不动：这道闸门几乎没开口`,
          );
        }
      }
    }
  } finally {
    await page.close();
  }

  if (problems.length > 0) return failure("分享载荷验收没过：", problems);

  console.log("");
  console.log("载荷压得动、放得进 hash、解得回同一场对局，审计三样一个都没带上路 ✓");
  return [];
}

if (isEntry(import.meta.url)) {
  const argv = process.argv.slice(2);
  const flag = (name, fallback) => {
    const index = argv.indexOf(name);
    return index < 0 ? fallback : argv[index + 1];
  };

  await runStandalone((lane) =>
    verifyShare(lane, {
      seed: Number.parseInt(flag("--seed", "2088"), 10),
      withSweep: !argv.includes("--no-sweep"),
      poison: argv.includes("--poison"),
      paifuPath: flag("--paifu", null),
    }),
  );
}
