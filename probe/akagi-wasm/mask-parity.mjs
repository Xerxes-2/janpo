// 票 92 的地基实验：**遮住他家的手牌，它出的手变不变？**
//
// 强 AI 基线在 janpo 里消费的是 `DecisionPackage.history`——那是**掩蔽事件流**
// （票 29a 的唯一一条掩蔽法则），他家的配牌与他家摸的牌在里面根本没有位置。
// 而票 91 的探路件量的全是**完整**的天凤牌谱（四家配牌都摊着）。
// 两者不是同一份输入，所以票 92 开工前必须先回答两件事：
//
//   ① 掩蔽之后它还跑不跑得动（riichienv 会不会被 13 张 `"?"` 弄崩）；
//   ② 掩蔽之后它出的手变不变——**变了就说明它一直在偷看他家的手牌**，
//      那样它就不配当强度参照系；不变就是「结构性不泄露」在这一席上的实证。
//
// 做法：同一份真实牌谱喂两遍。一遍原样（四家配牌都在），一遍把**除了本席之外**的
// 三家配牌换成 13 个 `"?"`、他家摸的那张也换成 `"?"`（mjai 服务端发给某一家的写法，
// 也正是 `MaskedEvent.encoder` 印出来的写法）。逐个决策点比 `decide()` 的 JSON。
//
// 跑法：
//   node mask-parity.mjs ../../tests/fixtures/paifu/mjai/*.mjson
//   node mask-parity.mjs --seats 0,1,2,3 ../../tests/fixtures/paifu/mjai/2025*.mjson

import { readFileSync } from "node:fs";
import { decide, feedLine, instantiate } from "./probe.js";

const WASM = new URL("./dist/akagi_wasm_probe.wasm", import.meta.url);

/** 他家手上那张牌在 mjai 里的写法（`MaskedEvent.encoder` 用的就是它）。 */
const HIDDEN = "?";

/** 一手牌被遮起来时那 13 个格子。 */
const hiddenHand = (count) => Array.from({ length: count }, () => HIDDEN);

/**
 * 一行 mjai 在 `viewer` 眼里的样子。**只动两条事件**（与 `MaskedEvent.forSeat` 逐条对应）：
 * `start_kyoku` 的他家配牌、`tsumo` 的他家摸牌；其余每一条都是牌桌上公开发生的。
 */
function maskLine(line, viewer) {
  const event = JSON.parse(line);

  if (event.type === "start_kyoku" && Array.isArray(event.tehais)) {
    return JSON.stringify({
      ...event,
      tehais: event.tehais.map((hand, seat) => (seat === viewer ? hand : hiddenHand(hand.length))),
    });
  }
  if (event.type === "tsumo" && event.actor !== viewer) {
    return JSON.stringify({ ...event, pai: HIDDEN });
  }
  return line;
}

/** 逐行喂、逐个决策点收下它出的那一手。 */
function replay(instance, lines, viewer, mask) {
  const decisions = [];
  const rejected = [];

  instance.exports.probe_init(4, viewer);

  for (const raw of lines) {
    const line = mask ? maskLine(raw, viewer) : raw;
    if (feedLine(instance, line) !== 1) {
      rejected.push(line);
      continue;
    }
    const out = decide(instance);
    if (!out.ok) throw new Error(`decide 报错：${out.error}`);
    if (out.decision) decisions.push(JSON.stringify(out.decision.action));
  }

  return { decisions, rejected };
}

function main() {
  const argv = process.argv.slice(2);
  let seats = [0];
  const files = [];

  for (let i = 0; i < argv.length; i += 1) {
    if (argv[i] === "--seats") {
      seats = argv[(i += 1)].split(",").map(Number);
    } else {
      files.push(argv[i]);
    }
  }
  if (files.length === 0) {
    console.error("用法：node mask-parity.mjs [--seats 0,1,2,3] <牌谱.mjson...>");
    process.exit(2);
  }

  const wasmBytes = readFileSync(WASM);

  return instantiate(wasmBytes).then((instance) => {
    let points = 0;
    let diverged = 0;
    let rejects = 0;
    const samples = [];

    for (const file of files) {
      const lines = readFileSync(file, "utf8")
        .split("\n")
        .filter((line) => line.trim().length > 0);

      for (const seat of seats) {
        const plain = replay(instance, lines, seat, false);
        const masked = replay(instance, lines, seat, true);

        rejects += masked.rejected.length;
        if (plain.decisions.length !== masked.decisions.length) {
          console.error(
            `${file} 座位 ${seat}：决策点数就对不上（原样 ${plain.decisions.length} / 掩蔽 ${masked.decisions.length}）`,
          );
          diverged += Math.abs(plain.decisions.length - masked.decisions.length);
        }
        const n = Math.min(plain.decisions.length, masked.decisions.length);
        points += n;
        for (let i = 0; i < n; i += 1) {
          if (plain.decisions[i] !== masked.decisions[i]) {
            diverged += 1;
            if (samples.length < 10) {
              samples.push(`${file} 座位 ${seat} 第 ${i} 个决策点：${plain.decisions[i]} → ${masked.decisions[i]}`);
            }
          }
        }
      }
    }

    console.log(`牌谱 ${files.length} 份、座位 ${seats.join("/")}、决策点 ${points} 个`);
    console.log(`掩蔽之后被引擎拒掉的行：${rejects}`);
    console.log(`两份输入下出的手不同的决策点：${diverged}`);
    for (const sample of samples) console.log(`  ${sample}`);
    process.exit(diverged === 0 && rejects === 0 ? 0 : 1);
  });
}

main();
