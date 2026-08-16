/**
 * prompt 里那几个中文词的**唯一一份**（票 29b）。
 *
 * 存在的理由是「同一套措辞」：前缀（【到目前为止你看到的】，`history.ts`）与尾部
 * （【现在】，`prompt.ts`）渲的是同一件事的两种形态，两处的风向、座位与副露必须写成一样的词，
 * 否则同一份 prompt 里会出现两套说法——这正是「历史交给 TS 渲染」那条裁决要避免的
 * （DECISIONS 第六次裁决）。
 *
 * 术语照 `CONTEXT.md`（Shimocha / Toimen / Kamicha、Pon / Chi / Ankan / Minkan / Kakan、
 * Riichi），牌一律 mjai 记法（ADR-0001：中文牌名不进 prompt）。
 *
 * **票 31 要把措辞降成数据**，这份文件就是它的落脚点：现在是常量，那时是模板槽位。
 */

/** 风：mjai 的字牌记法 → 中文单字。场风与自风共用。 */
const KAZE: Record<string, string> = { "1z": "东", "2z": "南", "3z": "西", "4z": "北" };

/** 立直状态（`Observation` 的三值字段）。 */
const RIICHI: Record<string, string> = { none: "无", declared: "已宣言", accepted: "已成立" };

/**
 * 相对座位（CONTEXT.md 的 Shimocha / Toimen / Kamicha）。
 * **数字是引擎算好的 `relative`**，这一层一次取模都不做——三麻没有对家，那是引擎的事。
 */
const RELATIVE: Record<number, string> = { 1: "下家", 2: "对家", 3: "上家" };

/** 副露的种类（wire 名照 mjai，`daiminkan` 就是术语表的 Minkan）。 */
const NAKI: Record<string, string> = {
  pon: "碰",
  chi: "吃",
  ankan: "暗杠",
  daiminkan: "大明杠",
  kakan: "加杠",
};

/** 流局的形态（`RyuukyokuReason` 的 wire 取值）。 */
const RYUUKYOKU: Record<string, string> = {
  fanpai: "荒牌流局",
  nagashimangan: "流し満贯",
  kyushukyuhai: "九种九牌",
  sufonrenta: "四风连打",
  sukaikan: "四杠散了",
  suchareach: "四家立直",
  sanchaho: "三家和了",
};

/** 认不出来就把 wire 值原样写出去：**读不懂的东西不许悄悄消失**。 */
function lookup<T extends string | number>(table: Record<T, string>, key: T): string {
  return table[key] ?? String(key);
}

export function kaze(notation: string): string {
  return lookup(KAZE, notation);
}

export function riichiState(wire: string): string {
  return lookup(RIICHI, wire);
}

export function nakiKind(wire: string): string {
  return lookup(NAKI, wire);
}

export function ryuukyokuReason(wire: string): string {
  return lookup(RYUUKYOKU, wire);
}

/**
 * 「谁」的称呼表：座位号 → `你` / `下家` / `对家` / `上家`。
 *
 * **相对位置由决策包里的 `relative` 给**（引擎算的），因此前缀与尾部叫的是同一个名字，
 * 而这一层不必知道座位数，也不必做取模。这张表在一局之内恒定（座位不动），
 * 因此拿它渲染历史**不破坏前缀的字节稳定性**。
 */
export interface Naming {
  /** 那个座位在这份 prompt 里的称呼。 */
  who: (seat: number) => string;
}

export function namesFor(viewer: number, others: { seat: number; relative: number }[]): Naming {
  const table = new Map<number, string>([[viewer, "你"]]);
  for (const other of others) table.set(other.seat, lookup(RELATIVE, other.relative));

  return { who: (seat: number) => table.get(seat) ?? `座位 ${seat}` };
}
