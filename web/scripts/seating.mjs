// 把一份**坐法**灌进 localStorage（票 73）：模型档案库 + 每席一条绑定。
//
// 页面的 `init` 读的就是它（F# 侧 `Store.readSeating`），因此各道闸门与手验脚本
// 用这一个入口摆桌子，**键名只有这一份**——写的与读的拼不到一块去，是票 23 那次
// 「键名手写了两遍」的教训（报告 23 §7）。
//
// 键的形状（与 `src/Janpo.Web/Store.fs` 逐字对应）：
//
//   janpo.profiles.count                = "2"
//   janpo.profiles.<i>.name|provider|model|base_url|api_key|timeout_ms|thinking
//   janpo.seats.<座位>.choice|tier|persona|template|clock
//
// `choice` 的三种写法：`random` / `opinionated` / `profile:<档案名>`。
//
// **老格式（`janpo.llm.*`）不在这里**：那一份只出现在迁移那道闸门里
// （`verify-seats.mjs` 的第二程），别处灌它等于顺手依赖迁移路径。

/** 一份档案的默认值（与 F# 侧 `ModelProfile.initial` 同一套，超时取票 72 定的 4 分钟）。 */
const PROFILE = {
  name: "档案 1",
  provider: "deepseek",
  model: "deepseek-v4-flash",
  base_url: "",
  api_key: "",
  timeout_ms: "240000",
  thinking: "off",
};

/** 一席绑定的默认值（与 F# 侧 `SeatBinding.initial` 同一套：均匀随机、裸奔档、不限时）。 */
const BINDING = { choice: "random", tier: "bare", persona: "", template: "", clock: "0" };

/** 引用一份档案的那种 choice。 */
export const profileChoice = (name) => `profile:${name}`;

/**
 * 一份坐法 → 一张 `键 → 值` 的表。**纯的**，因此闸门可以拿它去核
 * 「页面重新打开之后 localStorage 里还是不是这一份」。
 */
export function seatingEntries({ profiles = [], seats = [] }) {
  const entries = [["janpo.profiles.count", String(profiles.length)]];

  profiles.forEach((profile, index) => {
    for (const [key, fallback] of Object.entries(PROFILE)) {
      entries.push([`janpo.profiles.${index}.${key}`, String(profile[key] ?? fallback)]);
    }
  });

  seats.forEach((seat, index) => {
    for (const [key, fallback] of Object.entries(BINDING)) {
      entries.push([`janpo.seats.${index}.${key}`, String(seat[key] ?? fallback)]);
    }
  });

  return entries;
}

/**
 * 开页面之前把这份坐法摆好。**走 `addInitScript`**：页面的 `init` 一次就读到它，
 * 不必开完再点一遍面板。
 */
export function plantSeating(page, seating) {
  return page.addInitScript((entries) => {
    for (const [key, value] of entries) localStorage.setItem(key, value);
  }, seatingEntries(seating));
}

/** 手验与闸门里那份档案在库里的叫法（**它绝不该出现在牌谱里**：那是本机的私人叫法）。 */
export const PROFILE_NAME = "同一份档案";

/** 第 N 席的人格。**逐席不同**：同一份档案坐两席时，两条 preamble 的正文必须因此分岔。 */
export const personaFor = (index) => `你是座位 ${index} 的雀士，第 ${index} 号打法。`;

/**
 * 渲染版本号拆成三截：`模板 id@模板哈希.渲染器摘要`（`web/src/agent/render-version.ts`）。
 */
const versionParts = (version) => {
  const [id, rest = ""] = version.split("@");
  const dot = rest.lastIndexOf(".");
  return { id, hash: rest.slice(0, dot), digest: rest.slice(dot + 1) };
};

/**
 * 牌谱里那几条 preamble 对不对（票 73 的对照实验形态）。返回失败清单（空 = 绿）。
 * **调用方要给各席不同的人格**（`personaFor`），下面第 2 与第 4 条按这个前提写。
 *
 * 四条，缺一不可：
 *   1. 点名的每一席都留下了自己那一条（按「座位 + 渲染版本」存，见 `Paifu.Prompting`）；
 *   2. 各席的正文**两两不同**——人格不同却渲出同一段，说明人格根本没跟着座位走；
 *   3. **模板 id 与渲染器摘要那两截相同**——模板没换、渲染器也没换，换的只有人格。
 *      这一条是 M2 对照实验的判据：自变量只许有一个；
 *   4. **模板哈希那一截不同**——人格排在 system 消息里，也就在**可缓存前缀**里
 *      （`templateDigest` 把 `template.system` 算了进去）。它是第 2 条的阳性对照：
 *      哪天人格被挪出前缀，这一条会当场红，而那正是「缓存命中率崩了」要先知道的事。
 */
export function preambleProblems(paifu, seats) {
  const preambles = paifu.prompting?.preambles ?? [];
  const problems = [];
  const mine = new Map();

  for (const seat of seats) {
    const found = preambles.filter((each) => each.seat === seat);
    if (found.length === 0) {
      problems.push(`座位 ${seat} 一条 preamble 都没进牌谱：那一席根本没被问过话`);
      continue;
    }
    mine.set(seat, found[0]);
  }

  const named = [...mine.entries()];

  for (const [seat, preamble] of named) {
    for (const [other, another] of named) {
      if (other <= seat) continue;
      if (preamble.text === another.text) {
        problems.push(
          `座位 ${seat} 与座位 ${other} 的 preamble 正文逐字相同：两席的人格没跟着座位走`,
        );
      }
      const mine = versionParts(preamble.render_version);
      const theirs = versionParts(another.render_version);

      if (mine.id !== theirs.id || mine.digest !== theirs.digest) {
        problems.push(
          `座位 ${seat} 与座位 ${other} 的模板 id / 渲染器摘要不同（${preamble.render_version} / ${another.render_version}）：` +
            "对照实验的自变量不止一个了",
        );
      }
      if (mine.hash === theirs.hash) {
        problems.push(
          `座位 ${seat} 与座位 ${other} 的模板哈希相同（${mine.hash}）：` +
            "两席的人格不同，它却没进可缓存前缀——那条阳性对照塌了",
        );
      }
    }
  }

  return problems;
}
