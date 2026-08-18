// 浏览器形态（V8）下**建决策包**这条路的量具（票 84）。
//
// **量的必须是这条路**：`DecisionPackage.forSeat` → `Observation.ofState` + `Scaffold.calculate`
// （后者内部是 `Shanten` / `Ukeire` / `Danger.rank` 的一批形态判定）。
// 四家 bot 的对局**不走这条路**：bot 只走 `GameState.step`，一次决策包都不建，
// 拿 `verify-export --to-end` 或 `soak` 当基准会量出一个漂亮但与本票无关的数。
// 因此它也被量了一份（`game` 口径，同一个分母），**作为反面对照摆在旁边**。
//
// **不进 CI 关卡**：它要跑几万次形态判定，每次提交都等它不划算。手跑：
//
//   cd web && node scripts/bench-decision.mjs                       # 用 src/generated 里的引擎
//   cd web && node scripts/bench-decision.mjs --engine /tmp/x       # 用另一份 Fable 产物
//   cd web && node scripts/bench-decision.mjs --engine /tmp/a,/tmp/b --seeds 1-12 --rounds 5
//   cd web && node scripts/bench-decision.mjs --prims                # 34 长数组那几个原语的单价
//
// `--engine` 收逗号分隔的**多份产物**：它们各自建自己的语料（记录类型不能跨模块图混用），
// 然后**逐轮交错**跑（A B A B …），这样别的 agent 在同一台机器上跑构建时噪声落在两边而不是一边。
//
// 可复现的三件事（判据 14 的量法，与 `run/reports/55-*.md` §2.3 同一套）：
//   1. **固定种子**：语料是种子 1-12 各打一场东风战里「有人被问」的那些局面，逐点存下来；
//   2. **先整跑一遍预热**再计时，让 V8 升到优化层；多轮取中位并报区间，不报单值；
//   3. **digest**：每次跑都把全部脚手架的数值折成一个字符串印出来。两份产物（例如
//      `--typedArrays` 开与关）digest 必须逐字相同——这只是本脚本自带的对照。
//      **真正的语义闸门在 CI 里**：形态判定的逐字段数值由 `pnpm run verify:golden` 背书，
//      **牌山与事件流的可复现性**由 `verify:tracer` / `verify:export --to-end` / `verify:share` 背书。

import { existsSync, readdirSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const webRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");

/** `--foo bar` / `--foo=bar` / `--flag`。 */
function parseArgs(argv) {
  const options = {};
  for (let index = 0; index < argv.length; index += 1) {
    const token = argv[index];
    if (!token.startsWith("--")) continue;
    const [name, inline] = token.slice(2).split("=");
    if (inline !== undefined) {
      options[name] = inline;
    } else if (argv[index + 1] !== undefined && !argv[index + 1].startsWith("--")) {
      options[name] = argv[index + 1];
      index += 1;
    } else {
      options[name] = true;
    }
  }
  return options;
}

/** `1-12` 或 `3,5,8` 都收。 */
function parseSeeds(text) {
  if (text === undefined) return [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12];
  const seeds = [];
  for (const part of String(text).split(",")) {
    const range = part.match(/^(\d+)-(\d+)$/);
    if (range) {
      for (let seed = Number(range[1]); seed <= Number(range[2]); seed += 1) seeds.push(seed);
    } else {
      seeds.push(Number(part));
    }
  }
  return seeds;
}

/**
 * Fable 产物的目录（里面直接躺着 `Scaffold.js`）。默认是 `pnpm run fable` 的输出。
 * 另一份产物这样出：`dotnet fable src/Janpo.Engine -o /tmp/x [--typedArrays false]`。
 */
export function engineDir(option) {
  const dir =
    option === undefined ? resolve(webRoot, "src/generated/Janpo.Engine") : resolve(option);
  if (!existsSync(resolve(dir, "Scaffold.js"))) {
    throw new Error(
      `${dir} 里没有 Scaffold.js；先 \`cd web && pnpm run fable\`，或用 --engine 指过去`,
    );
  }
  return dir;
}

/**
 * Fable 的运行时库跟着产物走，版本号在目录名里，因此得找而不是写死。
 * 库放在哪一层看编的是哪个工程：单编引擎时就在产物旁边，
 * 编整个 `Janpo.Web` 时引擎落在 `Janpo.Engine/` 子目录而库在上一层。
 */
function fableLibraryDir(dir) {
  for (let here = dir; ; here = dirname(here)) {
    const modules = resolve(here, "fable_modules");
    const found = existsSync(modules)
      ? readdirSync(modules).find((name) => name.startsWith("fable-library-js"))
      : undefined;
    if (found !== undefined) return resolve(modules, found);
    if (dirname(here) === here)
      throw new Error(`${dir} 往上都找不到 fable_modules/fable-library-js.*`);
  }
}

export async function loadEngine(dir) {
  const library = fableLibraryDir(dir);
  const [
    game,
    gameState,
    kyoku,
    rng,
    ruleset,
    decision,
    observation,
    scaffold,
    danger,
    shanten,
    ukeire,
    tile,
    list,
  ] = await Promise.all([
    import(resolve(dir, "Game.js")),
    import(resolve(dir, "GameState.js")),
    import(resolve(dir, "Kyoku.js")),
    import(resolve(dir, "Rng.js")),
    import(resolve(dir, "Ruleset.js")),
    import(resolve(dir, "DecisionPackage.js")),
    import(resolve(dir, "Observation.js")),
    import(resolve(dir, "Scaffold.js")),
    import(resolve(dir, "Danger.js")),
    import(resolve(dir, "Shanten.js")),
    import(resolve(dir, "Ukeire.js")),
    import(resolve(dir, "Tile.js")),
    import(resolve(library, "List.js")),
  ]);
  return {
    game,
    gameState,
    kyoku,
    rng,
    ruleset,
    decision,
    observation,
    scaffold,
    danger,
    shanten,
    ukeire,
    tile,
    list,
  };
}

/**
 * 语料：种子各打一场东风战，把每一个「有人被问」的局面存下来。
 * 这与 `Kyoku.run` 的驱动逐条相同（每步只问合法动作集里的第一个座位），
 * 只是多了一句「把这个局面记下来」——决策包正是在这些点上建的。
 */
export function collectDecisionPoints(engine, seeds, limit) {
  const { game, gameState, kyoku, rng, ruleset, list } = engine;
  const rules = ruleset.RulesetModule_yonma;
  const points = [];

  for (const seed of seeds) {
    let generator = rng.RngModule_ofSeed(seed);
    let current = game.GameModule_start(rules);

    for (;;) {
      const context = game.GameModule_nextKyoku(current);
      if (context == null) break;

      const started = gameState.GameStateModule_start(rules, context, generator);
      if (started.tag !== 0) throw new Error(`种子 ${seed} 开不了局`);
      let state = started.fields[0][0];
      generator = started.fields[0][1];

      for (;;) {
        const legal = gameState.GameStateModule_legalActions(state);
        if (list.isEmpty(legal)) break;
        const choice = list.head(legal);
        points.push({ seat: choice.Seat, state });

        const picked = kyoku.Kyoku_randomPlayer(generator, state, choice);
        generator = picked[1];
        const stepped = gameState.GameStateModule_step(state, picked[0]);
        if (stepped.tag !== 0) throw new Error(`种子 ${seed} 被引擎拒了一个合法动作`);
        state = stepped.fields[0][0];
      }

      current = game.GameModule_advance(state, current);
      if (limit > 0 && points.length >= limit) return points.slice(0, limit);
    }
  }

  return points;
}

/**
 * 每个决策点预先备好 `Scaffold.calculate` 的三个入参。
 * 这一步不计时——它是 `DecisionPackage.forSeat` 里脚手架之外的那一半（掩蔽事件流的 fold）。
 */
export function prepare(engine, points) {
  const { gameState, observation, tile, list } = engine;
  const prepared = [];

  for (const point of points) {
    const view = observation.ObservationModule_ofState(point.seat, point.state);
    if (view == null) continue;
    const legal = gameState.GameStateModule_legalActions(point.state);
    const asked = list.tryFind((choice) => choice.Seat === point.seat, legal);
    if (asked == null) continue;
    prepared.push({
      seat: point.seat,
      state: point.state,
      view,
      kindSet: gameState.GameStateModule_ruleset(point.state).TileKinds,
      numbered: list.mapIndexed((index, action) => [index, action], asked.Actions),
      // `Danger.rank` 收的正是这一份（`Scaffold.calculate` 里的 `List.map snd dahai`）：
      // 打得出的每一条动作各一张，去红。
      dahai: list.choose(
        (action) => (action.tag === 0 ? tile.TileModule_deaka(action.fields[1]) : undefined),
        asked.Actions,
      ),
    });
  }

  return prepared;
}

/**
 * 数值指纹：把每一份脚手架的每一个数折进一个字符串。
 * 两份 Fable 产物跑同一份语料，这个字符串必须逐字相同。
 */
export function digest(engine, prepared) {
  const { scaffold, shanten, ukeire, list } = engine;
  const parts = [];

  for (const item of prepared) {
    const computed = scaffold.ScaffoldModule_calculate(item.kindSet, item.numbered, item.view);
    if (computed == null) {
      parts.push("-");
      continue;
    }
    const trials = list.toArray(computed.Dahai).map((trial) => {
      const acceptance =
        trial.Ukeire == null
          ? "x"
          : `${ukeire.UkeireModule_total(trial.Ukeire)}/${ukeire.UkeireModule_kindCount(trial.Ukeire)}`;
      const risk = trial.Danger == null ? "x" : `${trial.Danger.Tier.tag}:${trial.Danger.Rank}`;
      return `${shanten.ShantenModule_value(trial.Shanten)},${trial.ShantenDelta},${acceptance},${risk}`;
    });
    const acceptance =
      computed.Ukeire == null ? "x" : `${ukeire.UkeireModule_total(computed.Ukeire)}`;
    parts.push(
      `${shanten.ShantenModule_value(computed.Shanten)}|${acceptance}|${trials.join(";")}`,
    );
  }

  // FNV-1a：只是要个短指纹，不是密码学用途。
  let hash = 0x811c9dc5;
  const text = parts.join("\n");
  for (let index = 0; index < text.length; index += 1) {
    hash ^= text.charCodeAt(index);
    hash = Math.imul(hash, 0x01000193) >>> 0;
  }
  return { hash: hash.toString(16).padStart(8, "0"), lines: parts.length, chars: text.length };
}

/** 一趟：整份语料各跑一次。返回一个吸收值，免得 V8 把整个循环优化掉。 */
function makeWorkloads(engine, prepared, seeds, limit) {
  const { decision, observation, scaffold, danger, shanten } = engine;

  return {
    // **反面对照**（全部数字都按同一个分母归一，因此直接可比）：
    // 四家 bot 把这些局从头打到尾（`GameState.step`），**一个决策包都不建**。
    // 它就是 `verify-export --to-end` 量到的那条路：它比上面几条便宜两个量级，
    // 拿它当这张票的基准会量出一个漂亮但无关的数。
    game: () => collectDecisionPoints(engine, seeds, limit).length,
    // 这一票的主口径：一次决策的脚手架（Shanten × ~400 + Ukeire + Danger）。
    scaffold: () => {
      let sink = 0;
      for (const item of prepared) {
        const computed = scaffold.ScaffoldModule_calculate(item.kindSet, item.numbered, item.view);
        if (computed != null) sink += shanten.ShantenModule_value(computed.Shanten);
      }
      return sink;
    },
    // 整包：脚手架 + 掩蔽事件流的 fold（`Observation.ofState`）。
    package: () => {
      let sink = 0;
      for (const item of prepared) {
        const built = decision.DecisionPackageModule_forSeat(item.seat, item.state);
        if (built != null && built.Seat === item.seat) sink += 1;
      }
      return sink;
    },
    // 观测：整包减脚手架的那一半，单独量一次好归因（判据 13）。
    observation: () => {
      let sink = 0;
      for (const item of prepared) {
        const view = observation.ObservationModule_ofState(item.seat, item.state);
        if (view != null) sink += 1;
      }
      return sink;
    },
    // 危险度：`Danger.visibleCounts` 那一处 34 长数组的主。
    danger: () => {
      let sink = 0;
      for (const item of prepared) {
        sink += danger.DangerModule_rank(item.view, item.dahai) == null ? 0 : 1;
      }
      return sink;
    },
  };
}

function median(values) {
  const sorted = [...values].sort((left, right) => left - right);
  const middle = Math.floor(sorted.length / 2);
  return sorted.length % 2 === 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
}

/**
 * 34 长数组的几个**原语**单价。量的不是引擎，是 Fable 真发出来的那几句
 * （`Array.zeroCreate` / `Array.copy` / `Array.blit` 各自的两个版本）。
 * 它给的是「把剩下的分配全部变成复用缓冲」能省多少的**上界**：单价 × 每决策的个数。
 */
async function measurePrims(dir, rounds) {
  const library = fableLibraryDir(dir);
  const array = await import(resolve(library, "Array.js"));
  const reps = 200000;
  const typedSource = new Int32Array(34);
  const plainSource = new Array(34).fill(0);
  const typedTarget = new Int32Array(34);
  const plainTarget = new Array(34).fill(0);

  const shapes = {
    "new Int32Array(34)": () => new Int32Array(34),
    "new Array(34).fill(0)": () => array.fill(new Array(34), 0, 34, 0),
    "copy(Int32Array)": () => array.copy(typedSource),
    "copy(Array)": () => array.copy(plainSource),
    "copyTo(Int32Array)": () => array.copyTo(typedSource, 0, typedTarget, 0, 34),
    "copyTo(Array)": () => array.copyTo(plainSource, 0, plainTarget, 0, 34),
  };

  console.log(`node ${process.version}，原语单价（${reps} 次 × ${rounds} 轮，交错）`);
  const samples = Object.fromEntries(Object.keys(shapes).map((name) => [name, []]));
  for (const shape of Object.values(shapes)) {
    let sink = 0;
    for (let index = 0; index < reps; index += 1) sink += shape() == null ? 0 : 1;
    if (sink < 0) throw new Error("unreachable"); // 只为把 sink 用掉：否则 V8 能把整个循环删掉
  }
  for (let round = 0; round < rounds; round += 1) {
    for (const [name, shape] of Object.entries(shapes)) {
      const start = process.hrtime.bigint();
      let sink = 0;
      for (let index = 0; index < reps; index += 1) sink += shape() == null ? 0 : 1;
      const elapsed = Number(process.hrtime.bigint() - start);
      if (sink < 0) throw new Error("unreachable");
      samples[name].push(elapsed / reps);
    }
  }
  for (const [name, values] of Object.entries(samples)) {
    console.log(
      `  ${name.padEnd(24)} 中位 ${median(values).toFixed(1)} ns（区间 ${Math.min(...values).toFixed(1)}–${Math.max(...values).toFixed(1)}）`,
    );
  }
}

function summarise(samples) {
  return {
    median: median(samples),
    min: Math.min(...samples),
    max: Math.max(...samples),
    rounds: samples,
  };
}

async function main() {
  const options = parseArgs(process.argv.slice(2));
  const dirs = String(options.engine ?? "")
    .split(",")
    .filter((part) => part !== "")
    .map((part) => engineDir(part));
  const targets = dirs.length === 0 ? [engineDir(undefined)] : dirs;
  const seeds = parseSeeds(options.seeds);
  const rounds = Number(options.rounds ?? 5);
  const limit = Number(options.limit ?? 0);
  const only = options.only === undefined ? null : String(options.only).split(",");

  if (options.prims) {
    await measurePrims(targets[0], rounds);
    return;
  }

  const lanes = [];
  for (const dir of targets) {
    const engine = await loadEngine(dir);
    const prepared = prepare(engine, collectDecisionPoints(engine, seeds, limit));
    const workloads = makeWorkloads(engine, prepared, seeds, limit);
    const names = Object.keys(workloads).filter((name) => only === null || only.includes(name));
    for (const name of names) workloads[name](); // 预热：整跑一遍再计时。
    const samples = Object.fromEntries(names.map((name) => [name, []]));
    lanes.push({ dir, prepared, digest: digest(engine, prepared), workloads, names, samples });
  }

  // **逐轮交错**：同一轮里每份产物各跑一遍，机器上别人的负载因此落在所有产物上。
  for (let round = 0; round < rounds; round += 1) {
    for (const lane of lanes) {
      for (const name of lane.names) {
        const start = process.hrtime.bigint();
        lane.workloads[name]();
        const elapsed = Number(process.hrtime.bigint() - start) / 1000; // µs
        lane.samples[name].push(elapsed / lane.prepared.length);
      }
    }
  }

  const report = {
    node: process.version,
    seeds,
    rounds,
    lanes: lanes.map((lane) => ({
      engine: lane.dir,
      decisions: lane.prepared.length,
      digest: lane.digest,
      results: Object.fromEntries(lane.names.map((name) => [name, summarise(lane.samples[name])])),
    })),
  };

  if (options.json) {
    console.log(JSON.stringify(report));
    return;
  }

  console.log(`node ${process.version}，种子 ${seeds.join(",")}，${rounds} 轮（交错）`);
  const baseline = report.lanes[0];
  for (const lane of report.lanes) {
    console.log(`\n引擎 ${lane.engine}`);
    console.log(
      `  决策点 ${lane.decisions} 个，digest ${lane.digest.hash}（${lane.digest.lines} 行 / ${lane.digest.chars} 字）` +
        (lane === baseline
          ? ""
          : lane.digest.hash === baseline.digest.hash
            ? " —— 与第一份逐字相同"
            : " —— **与第一份不同**"),
    );
    for (const [name, result] of Object.entries(lane.results)) {
      const ratio =
        lane === baseline
          ? ""
          : `，对第一份 ${(baseline.results[name].median / result.median).toFixed(3)}×`;
      console.log(
        `  ${name.padEnd(12)} 中位 ${result.median.toFixed(1)} µs/决策（区间 ${result.min.toFixed(1)}–${result.max.toFixed(1)}）${ratio}`,
      );
    }
  }
}

// 被 `import` 时不自己跑（掉分配的探针就是拿上面那几个导出拼出来的）。
// 不用 `browser-lane.mjs` 的 `isEntry`：那份要拉 playwright，而这个量具不需要浏览器。
if (process.argv[1] !== undefined && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  await main();
}
