/**
 * prompt 里那几个中文词的**唯一一份**（票 29b 抽出来，票 31 降成数据）。
 *
 * 存在的理由是「同一套措辞」：前缀（【到目前为止你看到的】，`history.ts`）与尾部
 * （【现在】，`prompt.ts`）渲的是同一件事的两种形态，两处的风向、座位与副露必须写成一样的词，
 * 否则同一份 prompt 里会出现两套说法——这正是「历史交给 TS 渲染」那条裁决要避免的
 * （DECISIONS 第六次裁决）。
 *
 * 术语照 `CONTEXT.md`（Shimocha / Toimen / Kamicha、Pon / Chi / Ankan / Minkan / Kakan、
 * Riichi），牌一律 mjai 记法（ADR-0001：中文牌名不进 prompt）。
 *
 * **票 31 把它降成了数据**：`DEFAULT_WORDING` 只是**默认的那一份**，模板可以整表替换
 * （`template.ts`）。渲染器不再直接读常量，一律经 `Words`——它由「哪一份措辞表」加
 * 「谁在看」现造，**一局之内恒定**，因此拿它渲历史不破坏前缀的字节稳定性。
 */

/**
 * 一份措辞表。五张表各自是「wire 取值 → 中文」的字典，**认不出来的键原样写出去**
 * （读不懂的东西不许悄悄消失）。
 *
 * 换表就是换 prompt 的措辞，**不必改代码重编**：整表从座位配置里的模板 JSON 注入。
 */
export interface Wording {
  /** 风：mjai 的字牌记法 → 中文单字。场风与自风共用。 */
  kaze: Record<string, string>;
  /** 立直状态（`Observation` 的三值字段）。 */
  riichi: Record<string, string>;
  /**
   * 相对座位（CONTEXT.md 的 Shimocha / Toimen / Kamicha），键是引擎算好的 `relative`。
   * **这一层一次取模都不做**——三麻没有对家，那是引擎的事。
   */
  relative: Record<string, string>;
  /** 副露的种类（wire 名照 mjai，`daiminkan` 就是术语表的 Minkan）。 */
  naki: Record<string, string>;
  /** 流局的形态（`RyuukyokuReason` 的 wire 取值）。 */
  ryuukyoku: Record<string, string>;
}

/** 默认的那一份措辞表。**模板不指定时用它**（票 31：默认模板仍在代码里）。 */
export const DEFAULT_WORDING: Wording = {
  kaze: { "1z": "东", "2z": "南", "3z": "西", "4z": "北" },
  riichi: { none: "无", declared: "已宣言", accepted: "已成立" },
  relative: { "1": "下家", "2": "对家", "3": "上家" },
  naki: {
    pon: "碰",
    chi: "吃",
    ankan: "暗杠",
    daiminkan: "大明杠",
    kakan: "加杠",
  },
  ryuukyoku: {
    fanpai: "荒牌流局",
    nagashimangan: "流し満贯",
    kyushukyuhai: "九种九牌",
    sufonrenta: "四风连打",
    sukaikan: "四杠散了",
    suchareach: "四家立直",
    sanchaho: "三家和了",
  },
};

/** 认不出来就把 wire 值原样写出去：**读不懂的东西不许悄悄消失**。 */
function lookup(table: Record<string, string>, key: string): string {
  return table[key] ?? key;
}

/**
 * 这一份 prompt 里的说法：五张措辞表加「谁」的称呼。
 *
 * **它一局之内恒定**（措辞表来自模板，称呼来自座位，两者都不随手数变），
 * 因此拿它渲染历史**不破坏前缀的字节稳定性**（`prefix.test.ts` 守着这条）。
 */
export interface Words {
  kaze: (notation: string) => string;
  riichiState: (wire: string) => string;
  nakiKind: (wire: string) => string;
  ryuukyokuReason: (wire: string) => string;
  /** 那个座位在这份 prompt 里的称呼：`你` / `下家` / `对家` / `上家`。**参照系是观测者**。 */
  who: (seat: number) => string;
  /**
   * `seat` 相对 `origin` 是第几家：`下家` / `对家` / `上家`（数不出来就写座位号）。
   *
   * **参照系是 `origin` 而不是观测者**，副露的「来自谁」要的就是它：那句话说的是
   * **副露方**的第几家（`Naki.Target` 的语义，与牌桌那边一致）。换成观测者的参照系
   * 会写出「吃 来自对家」——日麻里不成立的句子（票 40）。
   */
  whoFrom: (origin: number, seat: number) => string;
}

/**
 * 现造这一份 prompt 的说法。
 *
 * **相对位置由决策包里的 `relative` 给**（引擎算的），因此前缀与尾部叫的是同一个名字，
 * 而这一层不必知道座位数。
 *
 * `whoFrom` 要的那个参照系（副露方）**包里没有**——包给的每一项 `relative` 都是相对观测者的。
 * 它因此**沿着座位环走**：包给的那几个 `relative` 排出来的就是「自己、下家、对家、上家」
 * 那个圈（引擎的 `Seat.orderFrom`），从副露方那一格起往下家方向数到目标那一格是第几步。
 * **这不是第二处座位取模**（CONTEXT.md：全仓库唯一的座位取模在 `Seat` 的私有 `shift` 里）：
 * 数的是包自己给的那个圈，圈多长、有没有对家都由引擎说了算。
 */
export function wordsFor(
  wording: Wording,
  viewer: number,
  others: { seat: number; relative: number }[],
): Words {
  const names = new Map<number, string>([[viewer, "你"]]);
  for (const other of others)
    names.set(other.seat, lookup(wording.relative, String(other.relative)));

  // 座位环：观测者排头，其余按引擎给的 `relative` 升序——那就是下家方向。
  const ring = [{ seat: viewer, relative: 0 }, ...others]
    .sort((left, right) => left.relative - right.relative)
    .map((each) => each.seat);

  return {
    kaze: (notation) => lookup(wording.kaze, notation),
    riichiState: (wire) => lookup(wording.riichi, wire),
    nakiKind: (wire) => lookup(wording.naki, wire),
    ryuukyokuReason: (wire) => lookup(wording.ryuukyoku, wire),
    who: (seat) => names.get(seat) ?? `座位 ${seat}`,
    whoFrom: (origin, seat) => {
      const start = ring.indexOf(origin);
      // 把圈从 `origin` 那一格转到头，往后找一次就是第几家（找不到、或者数出 0 家
      // ——那是它自己——都退回座位号：**读不懂的东西不许悄悄消失**）。
      const rotated = start < 0 ? [] : [...ring.slice(start), ...ring.slice(0, start)];
      const steps = rotated.indexOf(seat);
      return steps > 0 ? lookup(wording.relative, String(steps)) : `座位 ${seat}`;
    },
  };
}
