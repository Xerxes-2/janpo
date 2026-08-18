// 副露上那几个记号：**被鸣的是哪一张**（横放，票 38）、**来自谁**（票 51 起是**横放那张
// 落在第几格**，文字只留给读屏）、**加杠加上去的那张**（一枚「＋」，且叠在横放那张上）。
// 这个模块只做「给一组读回来的副露、核它对不对」，页面怎么走到那一局是调用方的事。
//
// **两道闸门共用它，各跑各的语料**（票 44 抽出来的）：
//   - `verify-tracer.mjs`：种子 1223、均匀随机、90 手 —— 五种副露形态齐全（含加杠与暗杠）；
//   - `verify-board.mjs`：种子 9 走到有立直那一手（立直局里的副露），另加两颗种子
//     专门把三种来源位置与三种杠摆齐（票 51 的位置断言）。
// 同一条不变量、几份真语料。抽成一处是因为它们核的是同一件事：改了记号的含义，两道一起红。

/** 相对位置的中文（CONTEXT.md 的 Shimocha / Toimen / Kamicha），参照系是**副露方**。 */
export const WHO = { 1: "下家", 2: "对家", 3: "上家" };

/**
 * **副露方自己的「左→右」在屏幕上是哪个方向**（票 82）。
 *
 * 位置编码本身一字未改（票 51：横放那张落在第几格就是来源，参照系是副露方），
 * 变的只是那一排画在屏幕的哪条轴上：上下两家仍是横着一排（x 递增），
 * **左右两家竖着一列（y 递增）**——牌转了 90°，“左右”跟着转成了“上下”。
 * **两侧一律从上往下读**（不镜像）：屏幕前只有一个读者，四家都按同一个方向读才不会把一家读反。
 */
const RUN = {
  self: { key: "x", said: "左→右", next: "右边" },
  toimen: { key: "x", said: "左→右", next: "右边" },
  kamicha: { key: "y", said: "上→下", next: "下边" },
  shimocha: { key: "y", said: "上→下", next: "下边" },
};

/**
 * 加杠加上去那张该叠在当初碰来那张的哪一侧：**副露方自己的「上面」**
 * （= 离自己更远那一侧）。上下两家那一侧在屏幕上方，
 * 上家（坐左、脸朝右）在右侧，下家（坐右、脸朝左）在左侧。
 */
const STACK = {
  self: { key: "y", sign: -1, said: "上面" },
  toimen: { key: "y", sign: -1, said: "上面" },
  kamicha: { key: "x", sign: 1, said: "右边（上家那一侧的外侧）" },
  shimocha: { key: "x", sign: -1, said: "左边（下家那一侧的外侧）" },
};

/**
 * 横放那张该落在第几格——**位置本身就是来源**（票 51）。
 *
 * 参照系是**副露方自己的左中右**：上家最左、对家中间、下家最右；
 * 四张（大明杠）时对家那一档取**左起第二格**（M 联盟公式规则第 6 条第 3 款：
 * 「明槓子(大明槓によるもの) … 上家からは左、対面から左2番目、下家からは右に並べる」）。
 *
 * `slots` 是这一组有几格（加杠是三格：加上去那张叠在自己那一格里，不另占一格）。
 */
export function expectedSlot(relative, slots) {
  if (relative === 3) return 0;
  if (relative === 2) return 1;
  if (relative === 1) return slots - 1;
  return null;
}

/** 每种副露该有几张牌、该占几格。加杠四张三格——第三格摞着两张。 */
const SHAPE = {
  吃: { tiles: 3, slots: 3 },
  碰: { tiles: 3, slots: 3 },
  大明杠: { tiles: 4, slots: 4 },
  暗杠: { tiles: 4, slots: 4 },
  加杠: { tiles: 4, slots: 3 },
};

/**
 * 从页面上把四家的副露逐组读回来。读的是 `data-`（同一件事给机器看的那一半），
 * 人看见的那一半是**横放那张在第几格**与那枚「＋」——两者对不上时下面那道断言会说出来。
 *
 * 逐格连**屏幕坐标**一起读（两个轴都读）：DOM 顺序对、画出来反了的话，
 * 「最左＝上家」这句话就假了（票 44 §3.3 同一条教训：属性与坐标两头都要核）。
 * **方位也读回来**（票 82）：那一排画在哪条轴上跟着方位走（`RUN`）。
 */
export function readNakiGroups(page) {
  return page.evaluate(() =>
    [0, 1, 2, 3].flatMap((seat) =>
      [...document.querySelectorAll(`[data-testid="seat-${seat}-naki"] [data-naki]`)].map(
        (node) => {
          const center = (element) => {
            const rect = element.getBoundingClientRect();
            return { x: rect.x + rect.width / 2, y: rect.y + rect.height / 2 };
          };
          const label = node.querySelector(".naki-from");
          const labelBox = label === null ? null : label.getBoundingClientRect();

          return {
            seat,
            // 这一家画在哪个方位（`Board.position` 的输出）：副露那一排的轴跟着它走。
            position: node.closest("[data-seat-position]")?.getAttribute("data-seat-position"),
            kind: node.getAttribute("data-naki"),
            from: node.getAttribute("data-naki-from"),
            fromSeat: node.getAttribute("data-naki-from-seat"),
            taken: node.querySelectorAll("[data-naki-taken]").length,
            added: node.querySelectorAll("[data-naki-added]").length,
            tiles: node.querySelectorAll(".tile").length,
            // 逐格：这一格里有没有横放那张 / 加上去那张，以及它画在哪儿。
            slots: [...node.querySelectorAll(".naki-slot")].map((slot) => {
              const takenTile = slot.querySelector("[data-naki-taken]");
              const addedTile = slot.querySelector("[data-naki-added]");
              return {
                taken: takenTile !== null,
                added: addedTile !== null,
                tiles: slot.querySelectorAll(".tile").length,
                x: center(slot).x,
                y: center(slot).y,
                takenAt: takenTile === null ? null : center(takenTile),
                addedAt: addedTile === null ? null : center(addedTile),
              };
            }),
            // 「来自上家」那句话**牌桌上看不见了**（票 51 主人裁定：位置为主、文字不留），
            // 但它仍写给读屏用户。两件事都核：文字在不在、以及它是不是真的看不见。
            label: label?.textContent ?? null,
            labelVisible: labelBox === null ? false : labelBox.width > 1 && labelBox.height > 1,
          };
        },
      ),
    ),
  );
}

/**
 * 逐组核这几件事：
 *
 * 1. 非暗杠的每一组**恰好一张**带 `data-naki-taken`（横放的那张），且写了来源；
 * 2. 暗杠两样都没有——它不是鸣来的，因此也没有位置编码；
 * 3. 来源的**中文说法与绝对座位对得上**：参照系是副露方，不是观测者（漂了这条当场就红），
 *    且**吃恒来自上家**；
 * 4. 只有加杠有那一张 `data-naki-added`（后加上去的，出自自家手里），且它**叠在**
 *    横放那张的外侧（同一格、按副露方自己的上下算，见 `STACK`）；
 * 5. **横放那张所在的槽位 ↔ 绝对座位**（票 51 的位置编码），吃恒在最左；
 * 6. 那几格在屏幕上**按副露方自己的左→右**画着（见 `RUN`：上下两家是屏幕的左→右，
 *    左右两家竖着摆，因此是屏幕的上→下），DOM 顺序与坐标同向；
 * 7. 来源那句中文仍写给读屏用户（`.naki-from`），但**牌桌上看不见**。
 *
 * 防空转由调用方负责（一组鸣来的副露都没有时，下面全部断言都会空着全绿）。
 */
export function checkNakiGroups(groups) {
  const missing = [];

  for (const group of groups) {
    const where = `座位 ${group.seat} 的「${group.kind}」`;
    const shape = SHAPE[group.kind];
    const takenSlot = group.slots.findIndex((slot) => slot.taken);

    if (shape === undefined) missing.push(`${where}不是五种副露之一`);
    else {
      if (group.tiles !== shape.tiles)
        missing.push(`${where}画出 ${group.tiles} 张牌，该有 ${shape.tiles} 张`);
      if (group.slots.length !== shape.slots)
        missing.push(`${where}摆成 ${group.slots.length} 格，该有 ${shape.slots} 格`);
    }

    // 那几格必须**按副露方自己的左→右**画出来（票 82 起分方位取轴：上下两家是屏幕的左→右，
    // 左右两家的牌转了 90°，那一排跟着变成屏幕的上→下）。DOM 顺序与坐标一致才谈得上
    // 「最左＝上家」——样式里一句 `row-reverse` / `column-reverse` 就能让属性全对、画出来全反。
    const run = RUN[group.position];
    if (run === undefined) {
      missing.push(`${where}所在的那一家没写方位（data-seat-position=「${group.position}」）`);
    } else {
      for (let index = 1; index < group.slots.length; index += 1) {
        const here = group.slots[index][run.key];
        const before = group.slots[index - 1][run.key];
        if (!(here > before))
          missing.push(
            `${where}第 ${index} 格没画在第 ${index - 1} 格${run.next}` +
              `（${run.key}=${Math.round(here)} vs ${Math.round(before)}）：` +
              `这一家的副露该按 ${run.said} 排，反了位置就读不出来源`,
          );
      }
    }

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

        // 票 51 的要害：**横放那张的位置就是那句「来自X」**，参照系是副露方自己的左中右。
        // 拿绝对座位算出该落哪一格，与画出来的对拍——参照系漂到观测者（或屏幕左右）就红。
        const wanted = expectedSlot(relative, group.slots.length);
        if (wanted !== null && takenSlot !== wanted)
          missing.push(
            `${where}横放那张摆在第 ${takenSlot} 格，可来源是座位 ${group.fromSeat}（相对副露方是${WHO[relative]}）该摆第 ${wanted} 格`,
          );
        if (group.kind === "吃" && takenSlot !== 0)
          missing.push(`${where}横放那张摆在第 ${takenSlot} 格：吃只吃得了上家，恒在最左`);

        if (group.label !== `来自${group.from}`)
          missing.push(
            `${where}没给读屏用户写出来源（读得到的是「${group.label}」，data- 上是「来自${group.from}」）`,
          );
        // 主人裁定：位置为主、文字不留。这一条防的是它又被写回牌桌上。
        if (group.labelVisible)
          missing.push(`${where}把「来自${group.from}」又写到牌桌上了：位置已经说了这件事`);
      }
    }

    const expected = group.kind === "加杠" ? 1 : 0;
    if (group.added !== expected)
      missing.push(`${where}里「加上去的那张」有 ${group.added} 处记号（该有 ${expected} 处）`);

    // 加杠：加上去那张与当初碰来的那张**同一格**，且画在它上面。
    // 摆到末尾去（另占一格）的话，四格里横放那张的位置读出来会是另一个来源——
    // 维基「槓」点名了这个坏处：上家からのポンが対面からの大明槓に化ける。
    if (group.kind === "加杠") {
      const stacked = group.slots.find((slot) => slot.added);
      const stack = STACK[group.position];
      if (stacked === undefined) missing.push(`${where}没画出加上去的那张`);
      else if (!stacked.taken)
        missing.push(`${where}加上去的那张没跟当初碰来的那张摞在一格：位置读出来会变成另一个来源`);
      else if (stack === undefined)
        missing.push(`${where}所在的那一家没写方位（data-seat-position）`);
      else {
        // 「叠在上面」按**副露方自己**的上下算（票 82）：左右两家的牌转了 90°，
        // 那一侧在屏幕上是左右方向。差值要过半张牌（> 4px）才算真叠开了。
        const stackGap = (stacked.addedAt[stack.key] - stacked.takenAt[stack.key]) * stack.sign;
        if (!(stackGap > 4))
          missing.push(
            `${where}加上去的那张没叠在${stack.said}` +
              `（${stack.key}=${Math.round(stacked.addedAt[stack.key])} vs ${Math.round(stacked.takenAt[stack.key])}）：看不出它是后加的`,
          );
      }
    }
  }

  const seen = groups
    .map(
      (group) =>
        `${group.kind}${group.from === null ? "" : `←${group.from}`}${
          group.kind === "暗杠" ? "" : `第${group.slots.findIndex((slot) => slot.taken)}格`
        }`,
    )
    .join("、");

  return { missing, seen: seen || "无" };
}
