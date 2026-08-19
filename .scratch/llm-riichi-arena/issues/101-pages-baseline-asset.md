# 101 — 站点上没有那份产物：强 AI 基线在生产环境永远出不来

**What to build:** 主人在线上点开强 AI 基线，页面说了这么一句：

> 强 AI 基线拉不动：`https://xerxes-2.github.io/janpo/baseline/janpo-baseline.wasm` 回了 HTTP 404
> 座位 0 已退回「有主见」的自带 bot，其余席照常打完这一局。

**降级那一路是对的**（ADR-0006 边界 2 在生产环境里如实生效了，这句提示本身是票 92 的交付物）。
错的是**站点上从来没有那份产物**：`.github/workflows/pages.yml` 只跑 `pnpm run build`
（Fable + Vite），而那 6 MB 的 `web/public/baseline/janpo-baseline.wasm` 是 gitignored 的
（ADR-0006 边界 6：不入版本控制），于是永远不会被发布。
**票 92 与 93 都把这条挂成了待审项，现在它是线上唯一挡着这条线的东西。**

**Blocked by:** 无（92 / 93 已集成）

**Status:** ready-for-agent

## 先量，再选路（这一票的头等交付物是那三个数）

三条路都摆在这儿，**选哪条要用数说话**，别照感觉挑：

1. **在 pages.yml 里现造**：加 Rust 工具链 + `wasm32-unknown-unknown` + 跑 `fetch-upstream.sh`。
   要量：**冷缓存一次部署多花多少分钟**（GH runner 上没有 `/tmp/janpo-probe-target`）、
   工具链要不要进 `flake.nix`（ADR-0006 边界 6 把这件事留给了这一票）。
2. **一次造好挂成 Release 资产**，pages.yml 只下载：
   要量下载耗时与**「上游 pin 变了怎么重造」的手工步骤有多少**。
   注意它仍满足「6 MB 不入版本控制」（Release 资产不在仓库树里）。
3. **Actions 缓存 cargo 产物**：介于两者之间。要量缓存命中时/未命中时的两个数。
   **警告**：票 45 有前例——那次给 nix 加缓存是**净亏**（restore 20 秒 vs 冷拉 18 秒），
   所以「加缓存」这件事在这个仓库里必须先量后加。

- [ ] 三条路各给一组实测数（哪条没量到就写明为什么），**选一条落地**，理由写进报告
- [ ] **部署时长的代价要写在明面上**：今天 pages.yml 跑多久、改完跑多久

## 要什么行为

- [ ] 线上 `https://<owner>.github.io/janpo/baseline/janpo-baseline.wasm` **拿得到 200**
- [ ] **许可随产物上线**（ADR-0006 边界 4，`web/public/third-party/` 里那两份已经在发布了）：
      确认 `LICENSE-akagi.txt` / `NOTICE-akagi` 在 `web/dist` 里、页脚那条声明指得到它们
- [ ] **懒加载不许被破坏**（边界 1）：产物在站点上之后，
      首页与不选那一档的对局**照旧一个字节都不拉**——`verify-baseline.mjs` 那条断言照旧绿
- [ ] **降级那一路不许失效**：产物存在时它测不到，所以那一趟要保留一个「资产地址改坏」的构造
      （今天 CI 里它是主路，改完之后别让它变成一条没人跑的路——判据 3）
- [ ] 部署产物的**体积与缓存头**：6 MB 每次都重下还是 immutable 缓存，写清楚

## 闸门

- [ ] 本地跑通那条构造链（`probe/akagi-wasm/fetch-upstream.sh` + cargo），
      **cargo 缓存在 `/tmp/janpo-probe-target`（约 900 MB，完好），用 `CARGO_TARGET_DIR` 指过去，
      不许 `cargo clean`**
- [ ] `./scripts/ci.sh` 全绿；**`verify-baseline.mjs --asset` 那一档在本机跑过一次**（真推理）
- [ ] workflow 改动要能自证：**不许「推上去看看」**——用 `act` 或把关键步骤在本机复现，
      跑不了就在报告里写清「这一步只能上线验」并给出回滚办法

## 边界

- 不改 `web/src/**` 与 `src/Janpo.Web/**` 的行为（这一票是交付管线，不是功能）
- 不把那 6 MB 提进版本控制（ADR-0006 边界 6；Release 资产/构建期生成都可以）
- 不动 `ci.yml` 的既有六道 + 十八趟结构；要加步骤就加在 `pages.yml`
- 不碰 `tests/Janpo.Engine.Tests/**`（票 100 在里面）

## 顺手记着

**这一票是「东西做完了但用户拿不到」的典型**：92 的懒加载、93 的对照标注、ADR-0006 的六条边界
全部落地并且线上如实降级了——**唯独没有人把那 6 MB 送上去**。
交付管线不是附属品：**在生产环境里，没上线的功能与不存在的功能没有区别。**
