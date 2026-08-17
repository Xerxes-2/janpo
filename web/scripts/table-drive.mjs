// 牌桌的**页面内**驱动（票 56）。
//
// 从前「走一手」是四次 playwright 往返：问一次「单步」灰没灰、读一次「上一手」、点一次、
// 再等一次 DOM 变。一整场 785 手就是三千多次跨进程往返——实测（同一台机器、同一颗种子 447）：
//
//   每手一次往返（原写法）        200 手 6680 ms
//   页面内一次走完 + rAF 轮询      200 手 3329 ms
//   页面内一次走完 + 微任务轮询    200 手  279 ms  ← 现在用的
//
// 也就是说那 16 秒里绝大部分既不是引擎在算、也不是浏览器在画，而是**往返本身**。
// 这一段把「走一手」整段搬进页面：一次 `evaluate` 走到底。
// **断言一条没动**——闸门读的还是同样的 DOM，只是不再每手回一趟 node。
//
// 轮询策略是「先微任务、再宏任务」：Elmish 的 setState 落在微任务里（四家 bot 的那几道闸门
// 因此一手一微任务就够），但等模型回话的那一档（`verify-export --llm`）非得让宏任务跑起来
// 不可——一路 `Promise.resolve()` 会把 setTimeout 与网络回调整个饿死。

/**
 * 页面内走一手 / 走一段的那段代码。**它被 playwright 序列化后在页面里跑**，
 * 因此不许闭包引用模块作用域里的任何东西。
 *
 * @param options.limit 最多点几次「单步」
 * @param options.nextKyoku 这一局打完之后要不要点「下一局」接着打
 * @param options.budgetMs 单独一手最多等多久（等模型那一档要它）
 * @returns walked 点了几次单步、kyokus 打了几局、stuckAt 第几手没走动（null = 都走动了）、
 *          closed 收手时「单步」是灰的（这一局打完了 / 终局了）
 *
 * **只报调用方读得到的那几件事**：「终局了没有」与「引擎拒了没有」各闸门自己另查
 * （`table-result` / `table-fault`），这里不重复一份说法。
 */
async function driveTable({ limit, nextKyoku, budgetMs }) {
  const at = (testId) => document.querySelector(`[data-testid="${testId}"]`);
  const latest = () => at("table-latest").textContent.trim();

  // 先在微任务里抢答，拖久了退到宏任务（别把事件循环饿死）。
  const settle = (attempt) => {
    if (attempt < 8) return Promise.resolve();
    if (attempt < 64) return new Promise((done) => setTimeout(done, 0));
    return new Promise((done) => setTimeout(done, 8));
  };

  // 等到 `done()` 成立为止，超出预算就返回 false。
  const until = async (done) => {
    const deadline = performance.now() + budgetMs;
    let attempt = 0;
    while (!done()) {
      if (performance.now() > deadline) return false;
      await settle(attempt);
      attempt += 1;
    }
    return true;
  };

  let walked = 0;
  let kyokus = 1;
  let stuckAt = null;

  for (let turn = 0; turn < limit; turn += 1) {
    const step = at("table-step");

    if (step.disabled) {
      // 这一局打完了。不接着打的那几道闸门（走到某一手取快照）就停在这里。
      if (!nextKyoku) break;
      const next = at("table-next");
      if (next === null || next.disabled) break; // 终局：「下一局」也灰了
      next.click();
      kyokus += 1;
      if (!(await until(() => !at("table-step").disabled))) {
        stuckAt = turn;
        break;
      }
      continue;
    }

    const before = latest();
    step.click();
    walked += 1;

    // 三种落地方式：上一手变了、引擎拒了这一手（`table-fault`）、这一局正好打完（单步灰了）。
    const landed = await until(
      () => latest() !== before || at("table-fault") !== null || at("table-step").disabled,
    );
    if (!landed) {
      stuckAt = turn;
      break;
    }
    if (at("table-fault") !== null) break;
  }

  return { walked, kyokus, stuckAt, closed: at("table-step").disabled };
}

/**
 * 走一段：点「单步」最多 `limit` 次。`nextKyoku` 打开时一局打完就点「下一局」接着打。
 *
 * `walked` 数的是**点出去的单步次数**（与从前那几个循环同一口径）。
 */
export function stepTurns(page, { limit, nextKyoku = false, budgetMs = 30000 }) {
  return page.evaluate(driveTable, { limit, nextKyoku, budgetMs });
}

/**
 * 走一手，走动了返回 true。**这一局打完了（点之前或点之后「单步」灰了）返回 false**
 * ——牌桌那道闸门按这条口径数手数，因此单独留一个函数，别拿 `stepTurns` 凑。
 */
export async function stepOnce(page, { budgetMs = 30000 } = {}) {
  const { walked, stuckAt, closed } = await stepTurns(page, { limit: 1, budgetMs });
  if (stuckAt !== null) throw new Error(`点了「单步」之后 ${budgetMs}ms 里牌桌没走动`);
  return walked === 1 && !closed;
}
