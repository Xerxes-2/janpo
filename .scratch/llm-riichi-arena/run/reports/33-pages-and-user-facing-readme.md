# 33 — GitHub Pages 部署与面向用户的 README

**状态**：done　**工作区**：`janpo-ws-c`　**fixed point**：`0aee6982`（change `pplkusut`）

产出：`.github/workflows/pages.yml`（新）、`web/vite.config.ts` 的可配 `base`、
重写的 `README.md`（用户向）、新的 `docs/development.md`（原 README 的开发手册搬家 + 部署一节），
外加三处小改（两个无头脚本里页面内的动态 `import` 改成相对页面地址、`ci-web.sh` 的一句提示改指向、
`web/index.html` 的标签页标题与 `meta description`）。

**没碰**：`web/src/styles.css`、`docs/images/table.png`（README 引用它的路径一个字没变）、
`web/src/agent/`、`CONTEXT.md`、`docs/adr/`、引擎与它的测试、`scripts/ci.sh`。

---

## 1. Pages 部署

### 1.1 workflow

`.github/workflows/pages.yml`：`on: push: branches: [main]` + `workflow_dispatch`，
权限 `contents:read / pages:write / id-token:write`，`concurrency: pages`（**不**取消进行中的部署）。
两个 job：`build`（checkout → nix installer → `pnpm install --frozen-lockfile` →
`pnpm run build` → `configure-pages` → `upload-pages-artifact path: web/dist`）与
`deploy`（`actions/deploy-pages@v4`，`environment: github-pages`）。

工具链**照 `ci.yml` 的写法**：`DeterminateSystems/nix-installer-action` + `nix develop --command`，
dotnet（跑 Fable）与 node/pnpm（打包）都由 `flake.lock` 钉住版本 ——
所以 Pages 上产出的那份 `web/dist` 与本地 `pnpm run build` 是同一条链路。

### 1.2 base 怎么配

`web/vite.config.ts`：

```ts
const base = process.env.JANPO_BASE?.trim() || "./";
```

- **默认 `"./"`（相对）** —— 与改动前一致，本地 `pnpm run dev` / `preview` 与全部
  `web/scripts/verify-*.mjs`、`shoot-table.mjs` 都按这个默认跑，**一个字都不用改**。
- **Pages 构建注入 `JANPO_BASE=/janpo/`** ——「站点挂在哪个子路径」这件事**只写在
  `pages.yml` 的 job `env` 那一行**（有 ★★ 注释标着）。仓库改名或换自定义域名（那时填 `/`）
  只改那一行。另有一处写着站点地址的是 **README 末尾的 `[play]:` 引用式链接**（给人点的，
  不参与构建，README 里所有「在线试玩」都指它）。

> **偏离票面一处**：票与裁决写的是「本地默认 `/`」，实现里默认保留仓库既有的 `"./"`。
> 理由与被否决的选项见 `DECISIONS.md`「## 33」第 1 条：`"./"` 让产物在任意子路径下都能托管，
> 改成 `/` 是净损失，而票要防的事（无头闸门别断）两种默认都满足。

### 1.3 base 可配的验证记录（两种都实跑过）

| 验的什么 | 命令 | 结果 |
|---|---|---|
| 默认 base 的产物 | `cd web && pnpm run build` | `index.html` 里 `src="./assets/index-*.js"` |
| `/janpo/` 的产物 | `JANPO_BASE=/janpo/ vite build` | `src="/janpo/assets/index-*.js"`、`href="/janpo/assets/*.css"` |
| **默认 base 全套闸门** | `./scripts/ci.sh` | **全绿**（EXIT=0；引擎 695 + 浏览器宿主 73 条测试，浏览器内三道 ✓） |
| **`/janpo/` 下全套闸门** | `JANPO_BASE=/janpo/ ./scripts/ci.sh` | **全绿**（EXIT=0，浏览器内三道同样 ✓） |
| preview 真的挂在子路径 | 起 `startPreview` 打印 `resolvedUrls` | `http://localhost:4173/janpo`；`GET /janpo/` → 200、`GET /janpo/assets/index-*.js` → 200、`GET /` → **404**（确实只在 base 下） |
| **像 Pages 那样托管**（不用 vite） | 自写的 `node:http` 静态服务器挂在 `/janpo/` 前缀 + 无头 Chrome 打开 | 标题读到、点「单步」牌桌真的走了一手（`上一手：座位 0 手切9筒`）、请求过的路径只有 `/janpo/…` 三条、**console 无错、无 4xx**、`问题：无 ✓` |

最后一行就是票里「部署完自己开一次线上地址」那条：**无头打开 Pages 构建产物**，
用的是一个普通静态服务器（GitHub Pages 也就是这个），不是 vite preview。真部署要主人推了才有。

**顺手修的一个真隐患**：`verify-golden.mjs` / `verify-export.mjs` 在页面里
`await import("/src/generated/*.js")` —— vite dev 在 base 非 `/` 时对 base 外的路径直接 404，
写死绝对路径等于「base 只在 preview 那道能配」。改成 `"./src/generated/*.js"`（相对页面地址），
两种 base 下都过。

---

## 2. README：砍了什么、留了什么

### 2.1 新骨架

| 节 | 内容 |
|---|---|
| 首屏 | 一句话是什么 → **▶ 在线试玩链接** → 截图 + 图注 → 6 行英文简介 → WIP 告示（不提票号） |
| 怎么玩 | 六步：开链接 → 填 provider/模型/key → 选座位 → 拨脚手架 → 播放/单步/切视角 → 导出牌谱；末尾一段「模型不听话会怎样」 |
| 你的 key 去了哪 | 纯静态无后端 / localStorage / 浏览器直发 provider / **建议用有额度上限的 key** / OAuth 用不了只能填 key / 牌谱里不含 key |
| 想接本地模型 | 指向 `docs/host/custom-endpoint.md`，加一句 https 页面会问「本地网络访问」权限 |
| 不是什么 | 六条（保留原节，去掉 ADR 链接与「Mortal 是 M3 的可选依赖」这类开发向表述） |
| 现在能玩到什么，还差什么 | 两段，用户视角，**无票号、无 M0/M1/M2/M3 代号** |
| 许可证 | MIT |
| 末尾 | 一行 `docs/development.md` 链接 + 那个 `[play]:` 引用式链接 |

「怎么玩」里 `nix` / `dotnet` / `pnpm` / `Fable` **一个都没有**（`grep -n "nix\|dotnet\|pnpm\|Fable\|fable" README.md` 无输出——整份 README 都没有）。

### 2.2 整节删掉的（**没有改弱，是删掉**）

1. **「这个仓库是怎么造出来的」整节** —— 跑批流程、`.scratch/` 目录导览表、三个真事、
   RUNBOOK 链接、票数/报告数/commit 数。README 现在一个字都不提（日志文件仍然公开）。
2. **「凭什么有意思」整节** —— 双目标逐字段钉（10,983 行、黄金用例 40 条/1947 字段/3210 行）、
   结构性信息隐藏的源码索引、脚手架判据的 `CONTEXT.md` 引文、兜底闭环的实现位置、
   soak 200 场/1038 局/86,324 手、真牌谱对拍 18 局/200 局与抓到的两个 bug、规则集是一等输入。
   **它是工程叙述，不是用户叙述**；没有搬进 `docs/development.md`，理由见 DECISIONS「## 33」第 5 条
   （搬就要重新核实那些数字，而主人要的是裁剪读者面不是重新调研）。整节在 `0aee6982` 的历史里。
   —— 顺带一个发现：那节的黄金用例数字**已经过时**了，README 写「1947 个字段、3210 行」，
   本工作区实测现在是 **2069 个字段、3378 行**（票 28/29b 之后变的）。
3. **「开发」整节** —— 原样搬进 `docs/development.md`（环境、常用命令、真 key 的手动验收、
   自定义端点手验、截图脚本、黄金用例命令、CLI、仓库结构、加新模块的约定），另加一节
   **「部署（GitHub Pages）」**讲 `JANPO_BASE`。README 末尾只留一行链接。
4. 搬家时删掉两个会漂的数字：`dotnet test` 那行的「当前 763 条」（现在实测 768）、
   截图脚本那行的「端口另占 4190」（29b 之后端口由 vite 自己挑）。**删数字不是把声称写弱**。

### 2.3 留下来的已核实声称（**数字一个没动**）

- 兜底闭环：四类失败、**每手最多问 3 次**（本次复核 `Agent.fs` 仍是 `retryLimit = 2`）、
  两档代打策略、代打不静默。
- 断电演习：**60 次请求全部 4xx/5xx、20 手由引擎代打、一局照样打完**（原文数字照抄）。
  只删掉了「用时 18.4 s」——对用户没有意义（坏 key 的请求是秒失败）。
- provider 八家 + 自定义端点、模型是自由文本框、**M1 只支持一席**、工具搜索档灰着。
- key 只落 localStorage / 浏览器直发 / OAuth 用不了 / 牌谱里不含 key（`verify-export.mjs:164` 那道）。
- 危险度：现物 / 筋 / 壁 / 宝牌周边**四条规则**、威胁只认立直或副露的家、是启发式不是概率模型。
- 默认规则对齐**天凤鳳凰卓**。
- 本地端点那段的结论**原样指向** `docs/host/custom-endpoint.md`，https 页面那句照票 30 的实测写：
  「页面不在本地地址空间里 → Chrome 按『本地网络访问』规则拦一道 → 弹授权框，点允许就通；
  页面开在 localhost 则什么都不用管」。

---

## 3. 主人推之前要自己做的清单

1. **确认默认分支叫 `main`**。不是的话改 `pages.yml` 的 `on: push: branches:` 那一行
   （只有这一处认分支名）。
2. **确认仓库名是 `janpo`、owner 是 `Xerxes-2`**。不是的话改**两处**：
   `pages.yml` 里 `JANPO_BASE: /janpo/`（构建用，带 ★★ 注释），
   与 README 末尾 `[play]: https://xerxes-2.github.io/janpo/`（给人点的）。
   换成自定义域名时 `JANPO_BASE` 填 `/`，另外记得给 Pages 配 CNAME。
3. **仓库 Settings → Pages → Source 选「GitHub Actions」**（不是 "Deploy from a branch"）。
   不点这一下 `deploy-pages` 会失败。
4. 第一次跑完确认 **Actions → Pages → deploy 那步给出的 `page_url`** 与 README 里写的地址一致，
   然后**真开一次那个地址**，点「单步」看牌桌走不走得动（本地已用同构方式验过，见 §1.3）。
5. **CI 徽章我没做**（远端地址未定）。要加就在 README 首屏那句话下面贴：
   `[![CI](https://github.com/Xerxes-2/janpo/actions/workflows/ci.yml/badge.svg)](…/actions/workflows/ci.yml)`
   ——注意徽章是开发向的东西，主人自己权衡要不要放进纯用户向的 README（我倾向不放，或只放在
   `docs/development.md` 顶上）。
6. **仓库 topics / About**：建议 topics `mahjong` `riichi` `llm` `fsharp` `fable` `browser`，
   About 的一句话直接抄 README 首屏那句，Website 填 Pages 地址（勾上 "Use your GitHub Pages website"）。
7. 顺带：`.github/workflows/ci.yml` 现在 `on: push` 无分支过滤，推上去后每个分支都会跑一遍全量 CI
   （约十几分钟含 nix）。要省额度可以给它也加分支过滤——**我没动它**，那不属于这票。

## 4. 留给人的待审项 / 已知不完美

1. **`base` 默认值与票面文字的偏离**（`"./"` 而非 `/`），见 §1.2 与 DECISIONS「## 33」第 1 条。
2. **`web/scripts/shoot-table.mjs` 没做 base 适配**：它自己拼 `http://localhost:${port}/`。
   它不进 CI、也只在默认 base 下用，且票 32 正在改截图，所以**我没碰它**。
   将来若要在子路径下重出截图，那一行要改。
3. **`src/Janpo.Web/Agent.fs:182` 的注释写着「README 的『渲染层出口』约定」** ——
   那条约定现在在 `docs/development.md` 里。改它要动 F# 源码（这票的禁改边界），只报告不改。
4. **`DeterminateSystems/nix-installer-action@main` 没钉 tag**：照 `ci.yml` 的现状抄的，
   两份 workflow 同一个问题，要钉一起钉。
5. **Pages 构建每次都从零装 nix + 下 dotnet SDK**，没加任何 cache。第一次先看看耗时，
   嫌慢再上 `cachix/install-nix-action` + `DeterminateSystems/magic-nix-cache-action` 之类。
6. 我**没有**真部署过（无远端、且跑批禁止任何远端操作）；线上那一跑要主人自己点。

## 5. code-review（两轴，自己顺序跑的，无法派生 sub-agent）

### Standards

标准来源：`AGENTS.md`、`docs/agents/fsharp-style.md`（本次**没有 F# 改动**）、
`docs/agents/issue-tracker.md`、`docs/agents/triage-labels.md`、RUNBOOK；
工具强制的部分（Biome / tsc / Fantomas）跳过不提——它们本次都绿。

- **无 hard violation。** jj-only ✓（全程 `jj status` / `jj diff`，没碰 git）、
  没改 `CONTEXT.md` / `docs/adr/` / 别人的票 ✓、票文件的 `Status:` 行按 triage-labels 写成
  `ready-for-human` ✓、决策追加到 `DECISIONS.md` 文件末尾 ✓。
- **判断题（已修）：Duplicated Code / 两处真源。** 站点地址一开始在 README 里出现两次（标题 + 步骤 1）
  加 workflow 一次 = 三处。已把 README 的两处收成一个 `[play]:` 引用式链接，
  现在改名只需动两行（构建一行 + 给人看的一行），且两处互相指名。
- **判断题（未修）：workflow 里 `nix develop --command bash -c "cd web && …"` 出现两次。**
  合成一步能去掉重复，但会让「装依赖失败」与「构建失败」在 Actions UI 上分不开。
  保留两步，理由写在这里。
- **判断题（未修）：`docs/development.md` 是一份长文档**（环境 / 命令 / 部署 / 结构 / 约定）。
  它是原 README「开发」节的整体搬家，票就是这么要求的；真要拆等它继续长。
- 文档风格与仓库一致：中文优先、注释解释**为什么**、`web/vite.config.ts` 与 `pages.yml`
  的注释都写清了「改这一行会怎样」。

### Spec

票：`.scratch/llm-riichi-arena/issues/33-pages-and-user-facing-readme.md`。

- **一、Pages 部署**：五条验收全部落地（见 §1）。**一处偏离**：base 默认 `"./"` 而非票面写的 `/`
  （§1.2，已在票文件的那一条下面注明，并进了 DECISIONS）。
  「部署完自己开一次线上地址」按票里给的替代方式（无头打开 Pages 构建产物）验的。
- **二、README 重写**：八条验收全部落地（见 §2）。
- **三、边界**：禁改清单全部遵守。
- **scope creep（自查，三处，都记在 DECISIONS）**：
  1. 两个 `verify-*.mjs` 的页面内动态 `import` 改成相对地址 —— 直接服务于「base 必须可配」，
     不改的话 base 只有 preview 那道能配；
  2. `scripts/ci-web.sh` 的一句提示从「见 README」改成「见 docs/development.md」 ——
     手册搬家后原指向失效；
  3. `web/index.html` 的标签页标题（「浏览器里的第一颗曳光弹」→「浏览器里的 LLM 日麻竞技场」）
     与新增的 `meta description` —— 陌生人点进来第一眼看到的字，属于这票的读者面。
     **这三处都不在票的禁改清单里，且都跑过全套 CI。**

**结论**：Standards 0 个 hard violation（3 个判断题，1 个已修）；Spec 0 个缺失，
1 处有记录的偏离（base 默认值）+ 3 处自查的小 scope creep。没有 blocking 项要修。

---

## 6. 新 README 全文

````markdown
# janpo

**在浏览器里跑的 LLM 日本麻将（立直麻将）竞技场。** 你自带 API key，把牌桌上的一个座位交给模型，
看它一手一手打，随时把牌谱导出来。**没有后端**——整个平台就是一个网页。

### ▶ [在线试玩：xerxes-2.github.io/janpo][play]

打开就能玩，不用注册、不用装任何东西。

![牌桌截图：围观视角坐在座位 0，种子 1177 走了 52 手](docs/images/table.png)

围观视角（座位 0）下的牌桌：场况、四家的河（虚线是摸切）与副露、自己的手牌。
他家那几行看不到牌面——**模型看到的和你一样多**，别人的暗牌在页面拿到的数据里根本不存在。
想复盘就按一下切到上帝视角。

**In English.** janpo is a browser-only arena where an LLM plays Japanese mahjong (riichi).
Bring your own API key, seat a model, choose how much information it gets (what a player at the
table sees for free, or with shanten / ukeire / danger computed for it), watch it play hand by
hand, and export the game log. There is no server: the site is a static page, your key stays in
your browser's `localStorage`, and requests go straight from your browser to the provider.
UI and docs are Chinese-first. Work in progress — the interface and the export format will change.

> **还在做（WIP）。** 现在能玩的是「一个座位交给模型 + 三家随机选手」；
> 「模型坐一席打完**一整场**」还没验收过，界面、prompt 与牌谱格式都还会变。

---

## 怎么玩

1. 打开[在线试玩链接][play]。
2. 在配置面板里填模型：**provider** 选一家（`deepseek` / `anthropic` / `openai` / `google` /
   `openrouter` / `xai` / `groq` / `mistral`，或**自定义 OpenAI 兼容端点**）→ **模型**填名字
   （自由文本框，填端点认的那个 id）→ 填 **API key**；思考预算与超时按需要拨。
3. **模型坐席**：挑一个座位交给模型（现在只支持一席，其余三家是随机选手）。
4. **脚手架**：拨一档，决定告诉模型多少东西——
   - **裸奔**：只给一个坐在牌桌前的人**免费得到**的一切（他亲眼见过的事件、一眼看得见的场况）；
   - **信息辅助**：额外把**要算才有**的量算给它——向听数、有效牌（进张）、每张打牌的进退向，
     以及危险度排名。

   （还有「工具搜索」一档，灰着，还没做。）
5. 按 **播放** 让它自己打下去，或 **单步** 一手一手看；**视角**按钮在围观视角与上帝视角之间切。
6. 随时按 **导出牌谱** 下一个 JSON：mjai 风格的事件流，外加每一手的决策记录——当时给模型的 prompt、
   它的原始输出与 thinking、延迟、重试了几次。不必等终局。

**模型不听话会怎样。** 四种毛病会被当场接住：超时、provider 报错、输出格式跑偏、
以及给出一个这一手根本不能做的动作。接住之后带着原因重问，**每手最多问 3 次**；
仍然交不出来，就由规则引擎替它打一手（裸奔档摸切，信息辅助档在不退向听的打法里挑最安全的那张）。
代打**不静默**：那一手在牌桌上写着兜底的原因，状态行数着这一桌兜底了几手。
所以模型再怎么坏，对局都打得完——拿一把作废的 key 实测过一局：
**60 次请求全部 4xx/5xx，20 手由引擎代打，一局照样打到终局**。

## 你的 key 去了哪

- 页面**纯静态、没有后端**：没有任何服务器接得到你的 key，也没有地方存你的对局。
- key 只写进**你这台浏览器的 localStorage**，请求由**你的浏览器直接发给 provider**。
- 因此账单是你自己的：**建议用一把有额度上限的 key**（多数 provider 都能设消费上限或另开子 key），
  玩完把那一栏清掉也行。
- **订阅制的 OAuth 登录在浏览器里用不了**（Claude Pro / ChatGPT Plus 那种），只能填 API key。
- 导出的牌谱里不含 key——有一道自动检查专门守着这件事。

### 想接本地模型（Ollama / LM Studio / llama.cpp / vLLM / 自建网关）

可以，而且通常连 key 都不用填：provider 选「自定义端点（OpenAI 兼容）」，填一个 baseUrl。
baseUrl 怎么填、端点那侧的 **CORS 怎么放行**、接不上时页面会说什么，全在
[`docs/host/custom-endpoint.md`](docs/host/custom-endpoint.md)，结论都是实测的。

一句提醒：**在线试玩是 https 页面**，页面不在本地地址空间里，
所以从它连你本机的模型时 Chrome 会按「本地网络访问」规则拦一道，
弹一个授权框（允许本站访问你本地网络里的设备），点允许就通；
页面开在本机（localhost）时则什么都不用管。

## 不是什么

- **不是天凤 / 雀魂的替代品**：没有账号、没有匹配、没有天梯，也不打联机。
- **没有实时观战**：key 在你本地、请求由你的浏览器发，所以**你的浏览器就是唯一能让对局前进的地方**。
  别人打开链接看不到你正在进行的对局；想分享就把牌谱导出来发给他
  （用链接分享、把牌谱导回来看，都还没做）。
- **没有服务端**，因此不存牌谱、不存 key、也没有排行榜。导出的 JSON 归你自己。
- **危险度不是概率模型**：它按现物 / 筋 / 壁 / 宝牌周边四条规则给出安全度排名，
  威胁只认已经立直或有副露的家（一家都没有时整节不出现）。它是启发式，别当成雀力评价。
- **只打四麻**，本期不做三麻、古役与地方规则。
- **没有演出动画**，也没有为手机屏幕适配。

## 现在能玩到什么，还差什么

**现在**：一个座位交给模型、三家随机选手；两档脚手架；播放与单步；围观 / 上帝两种视角；
牌谱随时导出，而导出的那份字节能原样回放出同一局。规则跑的是完整的东风战——役与符、点数、
立直棒与本场、终局精算都在，默认规则对齐天凤鳳凰卓。

**还差**：模型坐一席打完一整场的验收；四个模型同桌互打与思考气泡；把牌谱导回来看、
用链接分享一局；首页放一局 demo 自动播；本地真人也坐一席；接一个成熟的麻将 AI 做对照基线。

## 许可证

[MIT](LICENSE) © 2026 Xerxes-2

---

想自己跑一份、改点什么、或者读代码：[`docs/development.md`](docs/development.md)。

<!-- 站点地址只写在这一处；仓库改名时这一行与 .github/workflows/pages.yml 的 JANPO_BASE 一起改。 -->

[play]: https://xerxes-2.github.io/janpo/
````
