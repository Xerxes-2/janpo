// 副露上那三个记号（票 38）：**被鸣的是哪一张**（横放）、**来自谁**（一枚标签）、
// **加杠加上去的那张**（一枚「＋」）。这个模块只做「给一组读回来的副露、核它对不对」，
// 页面怎么走到那一局是调用方的事。
//
// **两道闸门共用它，各跑各的语料**（票 44 抽出来的）：
//   - `verify-tracer.mjs`：种子 1223、均匀随机、90 手 —— 五种副露形态齐全（含加杠与暗杠）；
//   - `verify-board.mjs`：种子 9、有主见、走到有立直为止 —— 立直局里的副露。
// 同一条不变量、两份真语料。抽成一处是因为它们核的是同一件事：改了记号的含义，两道一起红。

/** 相对位置的中文（CONTEXT.md 的 Shimocha / Toimen / Kamicha），参照系是**副露方**。 */
export const WHO = { 1: "下家", 2: "对家", 3: "上家" };

/**
 * 从页面上把四家的副露逐组读回来。读的是 `data-`（同一件事给机器看的那一半），
 * 人看见的那一半是横放、「来自上家」与「＋」——两者对不上时下面那道断言会说出来。
 */
export function readNakiGroups(page) {
  return page.evaluate(() =>
    [0, 1, 2, 3].flatMap((seat) =>
      [...document.querySelectorAll(`[data-testid="seat-${seat}-naki"] [data-naki]`)].map(
        (node) => ({
          seat,
          kind: node.getAttribute("data-naki"),
          from: node.getAttribute("data-naki-from"),
          fromSeat: node.getAttribute("data-naki-from-seat"),
          taken: node.querySelectorAll("[data-naki-taken]").length,
          added: node.querySelectorAll("[data-naki-added]").length,
          // 「来自上家」那枚标签是**人看见的那一半**：`data-` 还在而标签没了，
          // 牌桌上就又看不出来源了（票 38 立票的起因）。
          label: node.querySelector(".naki-from")?.textContent ?? null,
        }),
      ),
    ),
  );
}

/**
 * 逐组核四件事：
 *
 * 1. 非暗杠的每一组**恰好一张**带 `data-naki-taken`（横放的那张），且写了来源；
 * 2. 暗杠两样都没有——它不是鸣来的；
 * 3. 来源的**中文说法与绝对座位对得上**：参照系是副露方，不是观测者（漂了这条当场就红），
 *    且**吃恒来自上家**；
 * 4. 只有加杠有那一张 `data-naki-added`（后加上去的，出自自家手里）。
 *
 * 防空转由调用方负责（一组鸣来的副露都没有时，下面全部断言都会空着全绿）。
 */
export function checkNakiGroups(groups) {
  const missing = [];

  for (const group of groups) {
    const where = `座位 ${group.seat} 的「${group.kind}」`;

    if (group.kind === "暗杠") {
      if (group.taken !== 0) missing.push(`${where}画了横放的那张，可暗杠没有被鸣的牌`);
      if (group.from !== null) missing.push(`${where}标了来源「${group.from}」，可暗杠不是鸣来的`);
    } else {
      // 两件事分开报（而不是 else if 一路串下去）：「哪一张」与「来自谁」是两条信息，
      // 单单丢掉一条时得看得出丢的是哪一条。
      if (group.taken !== 1)
        missing.push(`${where}里被鸣的那张有 ${group.taken} 处记号（该有且只有一处）`);

      if (group.from === null || group.fromSeat === null) {
        missing.push(`${where}没写来源（data-naki-from / data-naki-from-seat）`);
      } else {
        const relative = (Number(group.fromSeat) - group.seat + 4) % 4;
        if (WHO[relative] !== group.from)
          missing.push(
            `${where}写着「来自${group.from}」，可座位 ${group.fromSeat} 相对副露方是「${WHO[relative] ?? relative}」`,
          );
        if (group.kind === "吃" && group.from !== "上家")
          missing.push(`${where}写着「来自${group.from}」：吃只吃得了上家`);
        if (group.label !== `来自${group.from}`)
          missing.push(
            `${where}牌桌上没写出来源那枚标签（看得见的是「${group.label}」，data- 上是「来自${group.from}」）`,
          );
      }
    }

    const expected = group.kind === "加杠" ? 1 : 0;
    if (group.added !== expected)
      missing.push(`${where}里「加上去的那张」有 ${group.added} 处记号（该有 ${expected} 处）`);
  }

  const seen = groups
    .map((group) => `${group.kind}${group.from === null ? "" : `←${group.from}`}`)
    .join("、");

  return { missing, seen: seen || "无" };
}
