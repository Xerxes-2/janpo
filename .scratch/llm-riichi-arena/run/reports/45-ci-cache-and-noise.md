# 45 — 远端 CI 加缓存，并消掉那条每次都印的告警

**状态**：done　**change**：`mzmpoqtp`（本票一 commit）　**工作区**：`janpo-ws-a`
**fixed point**：`7d3e19c0`（change `ovrvmyrz`）

一句话：远端只改了两条 workflow——给 `/nix` 加一份 **GitHub 自带缓存**（`cache-nix-action`，
无账号、无 secret、无第三方服务），并把 `id-token: write` 从「装 Nix 的那个 job」上收回去，
那条 `FlakeHub Login failure` 告警**从根上没有路径可走**。
`scripts/ci.sh` 与 `scripts/ci-web.sh` **一个字节没动**，十余道闸门一道没跳、一道没加条件。

**先把话说死：本报告里没有一个远端数字。** 我没有推送权限，跑不了 Actions。
下面所有远端数字要么是**本地实测的分解量**（§4 上半），要么是**标着「估算」的算术**（§4 下半）。
真数字由调度器按 §6 的清单读，命中与未命中各一次。

改到的文件：

| 文件 | 改了什么 |
|---|---|
| `.github/workflows/ci.yml` | 加 `permissions: contents: read`、加缓存步与预热步；修掉两处过时注释 |
| `.github/workflows/pages.yml` | 权限从 workflow 级下放到 job 级（`id-token` 只给 deploy）；加缓存步（只读）与预热步 |

**没碰**：`scripts/ci.sh`、`scripts/ci-web.sh`（票面禁改，也是「本地绿=远端绿」的那份硬资产）、
`flake.nix` / `flake.lock`（版本钉死不动）、`web/**`、`src/**`、`CONTEXT.md`、`docs/adr/`、
`.config/dotnet-tools.json`、别人的票。`pages.yml` 里那句 `dotnet tool restore` 原样还在（第 6 步）。

---

## 1. 先量清楚「那五分钟」到底是什么

远端 6m25s、本地 `./scripts/ci.sh` 约 1m。差值的**假设**是「每次重新拉整套工具链」。
这个假设我只能在本地拆到这个粒度（都是 `janpo-ws-a` 实测，宿主机 16 核、同时有别的 agent 在跑）：

| 量 | 数字 | 怎么测的 |
|---|---|---|
| dev shell 闭包 | **135 条路径**、下载 **463 MiB**（压缩后）、解开 **1.5 GiB** | `nix develop --profile` + `nix path-info -S`／向 `cache.nixos.org` 查 narinfo |
| 其中最大的一条 | `dotnet-sdk-10.0.302` 单条 **219.6 MiB** 下载 | 同上 |
| nixpkgs 源码（eval 要用） | **330 MiB / 53,627 个文件** | flake input 的 outPath 上 `du`/`find` |
| `nix develop` 热跑 | **1.0–1.35 s** | `time nix develop --command true` |
| `~/.cache/nix` 冷、store 热 | **1.24 s**，缓存目录只长到 3.7 MB | `XDG_CACHE_HOME=/tmp/... nix develop /tmp/<flake 副本>` |
| `./scripts/ci.sh` 全绿（热） | **1m52s** | `time nix develop --command ./scripts/ci.sh` |

两条推论，都影响了方案选择：

1. **一个 nix 包都不由我们构建。** 整份 dev shell 全是 `cache.nixos.org` 上现成的路径
   （唯一的例外是那个几 KB 的 `janpo-env` 壳）。这一条直接判掉了三家「二进制缓存」（§2）。
2. **`~/.cache/nix` 不在关键路径上**：store 热的时候把它整个清空，`nix develop` 仍是 1.24 s，
   nixpkgs 的 tarball 没有被重新拉。所以缓存 `/nix` 一处就够，不必再加一条 `~/.cache/nix`。

顺带量了两处「不是 nix 的下载」，因为它们本来也在候选缓存名单里：

| 冷跑（空目录）| 耗时 | 落盘 |
|---|---|---|
| `dotnet tool restore` + `dotnet restore janpo.slnx`（`NUGET_PACKAGES=` 指向空目录） | **3.4 s** | 112 MiB |
| `pnpm install --frozen-lockfile`（`--store-dir` 指向空目录） | **2.0 s** | 224 MiB |

**这就是我没给 NuGet 与 pnpm 加缓存的理由**（§3 末）。

---

## 2. 缓存方案比较

判据三条：① 它到底缓存了什么、对**我们这种「零本地构建」**的用法有没有用；
② 没有第三方账号时 CI 还能不能跑；③ 出问题时回退要动几行。

| 方案 | 缓存的是 | 对本仓库的效果 | 账号／secret | 结论 |
|---|---|---|---|---|
| `DeterminateSystems/magic-nix-cache-action` | 本次 workflow **构建出来**的 store path | **一个字节都不会缓存**：它的 `upstream-cache` 默认 `https://cache.nixos.org`，README 原话是「来自上游缓存的路径**不**被缓存，因为它们下次照样取得到」——我们的 135 条路径全部来自那里 | 不需要 | ✗ 无效 |
| `cachix/cachix-action` | 同上（推自己构建的产物） | 同上；Cachix 默认就**不推 NixOS 缓存里已有的路径**（转引自 `cache-nix-action` 对比表里指向 cachix 文档的那条，未去 cachix 官网原文核对）；同时它只给开源项目 5 GB | **要账号 + `CACHIX_AUTH_TOKEN`** | ✗ 无效且引入账号 |
| FlakeHub Cache（`determinate-nixd login` 之后） | 同上 | 同上 | **要付费账号** | ✗ |
| `actions/cache` 直接缓存 `/nix/store` | 目录字节 | 方向对，但**光有 store 目录不算数**：Nix 的有效性记录在 `/nix/var/nix/db/db.sqlite` 里，恢复一堆没登记的路径等于没有；还要处理 WAL 文件、与安装器新建的库合并、root 属主 | 不需要 | ✗ 要自己写这套合并逻辑 |
| **`nix-community/cache-nix-action@v7`** | **整个 `/nix`**（store + db + profiles），用 **GitHub Actions 自带的缓存**后端 | 正好是我们要的：把「下载 463 MiB / 解开 1.5 GiB / 铺 5.3 万个 nixpkgs 源文件」换成一次 tar+zstd 恢复 | **不需要**（用的是仓库自己的 Actions 缓存额度） | ✓ **选它** |

补充两条事实（都取自权威源，不是转述）：

- `cache-nix-action` 的 README 明确列出兼容的安装器包括 `DeterminateSystems/determinate-nix-action`；
  而后者的 `action.yml` 就是一个 composite，内部 `uses: DeterminateSystems/nix-installer-action@<sha>`
  外加一个 `source-tag`。**我们用的 `nix-installer-action` 与它是同一份代码**，兼容性照搬得过来。
- 它 `post-if: success()`：**红的跑批不写缓存**，不会把坏状态存进去。

### 为什么不动安装器本身

`nix-installer-action@v22` 每次装的都是**最新的** Determinate Nix（README 原话）。
想钉住 Nix 版本得换成 `determinate-nix-action@v3.x.y`（现最新 `v3.21.9`，2026-07-30；
恰好等于本机的 Determinate Nix 3.21.9）。**这一票没换**：那会把「CI 跑在哪个 Nix 上」也一起改掉，
而我一次远端都验不了。留成一条建议（§9）。顺带修掉了 `ci.yml` 里那句错的注释——
它写着「用 `with: determinate-nix-version: <版本>` 钉它」，**这个 action 根本没有这个输入**。

---

## 3. 选定的形状与代价

```yaml
- uses: DeterminateSystems/nix-installer-action@v22
- name: 缓存 /nix（整套工具链）
  uses: nix-community/cache-nix-action@v7
  with:
    primary-key: nix-${{ runner.os }}-${{ hashFiles('flake.nix', 'flake.lock') }}
- name: 预热 dev shell（这一步的耗时 = 拉工具链的耗时）
  run: nix develop --command true
- name: fantomas --check、构建五个工程、跑测试、JS 侧关卡
  run: nix develop --command ./scripts/ci.sh
```

四个刻意的选择：

1. **缓存键只跟 `flake.nix` / `flake.lock` 走，且不给 restore 前缀。**
   代价：这两个文件一改就冷跑一次。
   收益：缓存内容**永远严格等于「这份 flake.lock 的工具链」**。给了前缀就会一代驮一代——
   旧 dotnet SDK 与新的一起被存下来，缓存越滚越大，直到 GitHub 按 10 GB 上限把别的挤掉。
   也因此**不需要** `gc-max-store-size`（它会 `nix store gc`，而我们的 dev shell 在跑批机器上没有 gc root，
   GC 反而可能把刚拉下来的工具链删掉）与 `purge`（那还要多要一个 `actions: write` 权限）。
2. **`ci.yml` 写缓存，`pages.yml` 只读（`save: false`）。**
   两条 workflow 在推 main 时同时开跑，同一个键会撞——其中一条必然收到
   「另一个 job 正在创建这份缓存」的告警，而本票的活是**减**噪音。
   代价：`flake.lock` 刚改过的那一次推送，Pages 那条冷跑一次。
3. **单独一步「预热」**，不是为了快，是为了**能读出数字**：这一步的耗时就是「拉工具链」的耗时。
   混在闸门那一步里，命中与未命中就再也分不开了，下一个人也无从判断缓存值不值。
   它不跳过任何东西——下面那步照样 `nix develop --command ./scripts/ci.sh` 跑整份脚本。
4. **`permissions:` 显式写上**（`ci.yml` 只要 `contents: read`）。见 §5。

**没有第三方账号时 CI 照样能跑**，而且是「照样绿、只是慢回今天的样子」：`cache-nix-action`
用的是 GitHub 给每个仓库的 Actions 缓存额度，不登录任何服务、不读任何 secret；
fork 上的 PR 因为缓存作用域拿不到主干的缓存，也只是退回冷跑。**把那两步整个删掉，CI 行为不变。**

**没给 NuGet 与 pnpm 加缓存**：实测冷跑分别是 3.4 s 与 2.0 s（§1 表）。跑批机器上会慢些，
但一份 ~340 MiB 的缓存来回一趟本身就要好几秒，收益很可能被抵掉，而多两条缓存就是多两处会过期、
会撞键、会掩盖「lockfile 与真实依赖对不上」的东西。**先只上一条能算得清账的。**

---

## 4. 数字：本地实测 + 明确标注的远端估算

本地实测见 §1 的两张表。**没有一个是远端数字。**

远端**估算**（算术摆在这里，好让调度器拿真数字来打脸）：

- 6m25s 里，闸门本身（`ci.sh`）在跑批机器上大约 **3–4 分钟**：本机热跑 1m52s，
  而 `ubuntu-latest` 是 4 核、本机 16 核，dotnet 构建/测试与浏览器那六道基本吃满核。
- 剩下的 **2.5–3 分钟**是安装器（下 73 MB 的 installer + 装 Nix，约 20–40 s）
  加 `nix develop` 冷跑（下载 463 MiB、解开 1.5 GiB、铺 5.3 万个 nixpkgs 源文件）。
- 命中缓存后，第二项被换成「恢复一份约 2 GiB（zstd 压缩后估计 0.7–1 GiB）的 `/nix`」，
  **估计 40–70 s**。于是**预期落点 4m30s ± 1 分钟，省下 1.5–2 分钟**。

**这不是承诺，是待证伪的估算。** 三种结果各自的读法写在 §6 末。

---

## 5. 那条 `FlakeHub Login failure` 告警：它想登什么、不登缺什么

### 根因（读的是 action 的源码，不是搜索结果的转述）

`DeterminateSystems/nix-installer-action` 的 `action.yml` 里，输入 `determinate` **默认 `true`**，
描述原文：「Whether to install Determinate Nix **and log in to FlakeHub** for private Flakes and
binary caches」。`src/index.ts` 里安装完必定走这一段：

```ts
if (this.determinate) { await this.flakehubLogin(); }

async flakehubLogin() {
  const canLogin = process.env["ACTIONS_ID_TOKEN_REQUEST_URL"] &&
                   process.env["ACTIONS_ID_TOKEN_REQUEST_TOKEN"];
  if (!canLogin) {
    actionsCore.info("FlakeHub is disabled because the workflow is misconfigured. …id-token: write…");
    return;                                    // ← 只是一行 info，没有注解
  }
  try { await actionsExec.exec(`determinate-nixd`, ["login", "github-action"]); }
  catch (e) { actionsCore.warning(`FlakeHub Login failure: ${stringifyError(e)}`); }  // ← 票面那条
}
```

于是**那条告警只可能出现在拿得到 OIDC 的 job 里**——`ACTIONS_ID_TOKEN_REQUEST_URL`
只有在 job 被显式授予 `id-token: write` 时才存在。本仓库两条 workflow 里，
**唯一给了 `id-token: write` 的是 `pages.yml`**（原来写在 workflow 级，因此 build 与 deploy 两个 job 都拿得到），
而 build 正是装 Nix 的那个 job。`ci.yml` 没有 `permissions:` 块，
`id-token` 又从不随仓库默认权限下发，所以 CI 那条走的是 `!canLogin` 分支、印的是那句 info。

**它想登的是什么**：`determinate-nixd login github-action` 拿 GitHub 的 OIDC 令牌去换一张 FlakeHub 令牌，
用途只有两项——(a) 拉 FlakeHub 上的**私有** flake，(b) 用 **FlakeHub Cache**。

**不登缺什么：什么都不缺。**
- 我们没有私有 flake。
- `flake.nix` 唯一那条 FlakeHub 依赖是**公开**的 `nixpkgs-weekly` tarball，取它不需要登录。
- FlakeHub Cache 只对**付费账号**开放，而且它缓存的是「我们自己构建的产物」，我们一件都没有（§2）。
- 现场证据：本机 `determinate-nixd status` 是 `Authentication: logged-out`，
  而这台机器上整份 1.5 GiB 的工具链闭包一条不缺、`nix develop` 1.35 s 就进得去。

### 处置：不给它 OIDC，那条路径根本不会走到

`pages.yml` 的 `permissions:` 从 workflow 级下放到 job 级：**`id-token: write` 只给 `deploy`**
（`deploy-pages` 换 Pages 部署凭据要用它），`build` 只留 `contents: read` + `pages: write`
（`configure-pages` 要读、必要时建仓库的 Pages 配置）。`ci.yml` 显式写上 `permissions: contents: read`。

这不是「把告警静音」——是**把那次登录尝试本身取消掉**：安装器看不见 OIDC 就不会去登，
也就没有失败可报。附带收益是最小权限：一张能代表本仓库去任何信 GitHub OIDC 的服务换凭据的令牌，
本来就不该发给一个要跑整套工具链与第三方 action 的 job。

### 诚实的残留：那句 info 还在，而我选择留它并写清楚

`!canLogin` 分支那句 `FlakeHub is disabled because the workflow is misconfigured. Please make sure
that id-token: write and contents: read are set…` **每次仍会印**。它不是注解（不上 run summary），
但它按票面的标准仍是噪音，而且更坏——它说「你配错了」，而真相是「我们故意不要」。

**唯一能让它也消失的办法是 `with: determinate: false`**（那样整段 FlakeHub 代码都不走），
但那等于把 CI 换成上游 Nix，而该 action 的 README 写明这个选项 **2026-01-01 之后不再支持**
（今天已经过了那个日期，`--prefer-upstream-nix` 这面旗在本机安装器里还在，但这属于随时会没的东西）。
**为一句 info 换掉跑 CI 的 Nix 实现，代价与收益不成比例。**

所以处置是：**在 `ci.yml` 里把这句话的来龙去脉写成注释**——谁印的、为什么印、我们为什么故意这样、
以及想要它消失该改什么。理由不是「反正无害」，而是「读日志的人一眼知道它是**预期内**的、
不是配置漏了」。这条得由调度器在 §6 的清单里核实一次：**告警注解必须没了，那句 info 必须还在**。
若哪天 Determinate 给出一个「装 Determinate Nix 但别登 FlakeHub」的输入，改一行就能收干净。

---

## 6. 给调度器的远端核对清单

**推送前**：这两条 workflow 我一次都没在远端跑过。第一次推上去请盯着，坏了就按 §8 回退。

### A. 缓存：要两次跑，**未命中一次、命中一次**

1. **第 1 次（必然未命中）**：推上去，看 **CI** 这条 run（workflow 名 `CI`，job `ci`）。记三个数：
   - 步骤 **`缓存 /nix（整套工具链）`** 的日志里应有 `Cache not found for key: nix-Linux-<hash>`
   - 步骤 **`预热 dev shell（这一步的耗时 = 拉工具链的耗时）`** 的**耗时**←**这就是「冷拉工具链」的真值**
   - job 右上角的 **Total duration**（与 6m25s 对照）
   - 另外看末尾 **`Post 缓存 /nix（整套工具链）`** 那一步的耗时（这是本次多花的存盘时间）
2. **第 2 次（应当命中）**：`Actions → CI → Run workflow`（`workflow_dispatch`，本票没动这个触发器）
   或随便再推一个 commit。同样记那三个数：
   - 缓存步应打印 `Cache restored from key: nix-Linux-<hash>`（`hit-primary-key` 为 `true`）
   - **预热步应从「分钟」掉到「几十秒」**
   - `Post` 那一步这次应当什么都不存（命中就不写）
3. **Pages 那条**（workflow 名 `Pages`，job `build`）同样看 `预热 dev shell` 与缓存步。
   注意它 `save: false`，日志里不会有存盘。**第一次推 main 时它多半仍未命中**（CI 那条跑完才存上），
   第二次推 main 才该命中——这是设计里认下的代价，不是 bug。

**三种结果的读法**：
- 预期（预热步冷 2–3 min → 热 40–70 s，总时长 6m25s → 4m30s 上下）：成了，把真数字补进本报告 §4。
- **省得少（预热步冷跑本来就只有 30–60 s）**：那说明「五分钟是拉工具链」这个假设本身不成立，
  剩下的时间在闸门自己身上（4 核跑批机器）。那时该做的不是继续加缓存，而是另立一票谈
  「拆 job 并行 / 换更大的 runner」。**缓存这两步可以留着（它没坏东西），也可以按 §8 撤掉。**
- **更慢了**（`cache-nix-action` 自己的 README 就写着「本 action 可能拖慢你的 workflow，请实测」）：
  按 §8 撤掉，只留权限那一半。

### B. 告警：一次跑批就能核完

1. **CI 与 Pages 两条 run 的 summary 页顶部，都不该再有 ⚠️ `FlakeHub Login failure: …` 注解。**
2. 安装器那一步的日志里**应当**有一句
   `FlakeHub is disabled because the workflow is misconfigured. Please make sure that id-token: write…`
   ——**这句是预期内的**，理由写在 `ci.yml` 的注释与本报告 §5。
3. **证伪点**：若那条 ⚠️ 注解**仍然**出现，尤其是出现在 **CI**（那条 workflow 从来没有 `id-token`）里，
   那我 §5 的根因就是错的。届时请把安装器那一步的完整日志贴进本文件，
   并沿这条线查：是不是仓库/组织层面给 workflow 注入了 OIDC 权限，或是 action 换了行为。
4. Pages 那条要**确认部署仍然成功**（权限被我下放到了 job 级）：`deploy` job 绿、
   `github-pages` environment 的 URL 出得来、站点能打开。这是本票风险最高的一处改动。

---

## 7. 闸门一道没少

- `scripts/ci.sh` 与 `scripts/ci-web.sh` **零改动**（`jj diff --stat` 里没有它们）。
  远端跑的仍是 `nix develop --command ./scripts/ci.sh` 这一条整份脚本。
- **没有新增任何 `if:`**。新加的两步（缓存、预热）都是无条件跑；
  `cache-nix-action` 自带的 `post-if: success()` 只管「要不要存缓存」，与闸门无关。
- 没有引入 `JANPO_NO_BROWSER` 之类的逃生口，没有动 `continue-on-error`，
  没有给任何一步加超时或重试。
- 本地 `./scripts/ci.sh` 在本票改动之后仍然全绿（§9）。

## 8. 风险与回退

| 风险 | 判断 | 回退 |
|---|---|---|
| `cache-nix-action` 与 Determinate Nix 的 daemon 打架（它要合并 `/nix/var/nix/db/db.sqlite`） | 它的 README 把 `determinate-nix-action` 列为兼容，而那个 action 内部就是我们用的 `nix-installer-action`；但**我一次远端都没验过** | 删掉两条 workflow 里的 `缓存 /nix` 那一步（各 5 行）。预热步可留可删 |
| 缓存反而更慢 | 见 §6 A 的读法 | 同上 |
| 收回 `id-token` 之后 Pages 部署挂了 | `deploy` job 仍有 `id-token: write` + `pages: write`，`build` 只是不再拿它 | 把 `permissions:` 三行搬回 workflow 级（那条告警会跟着回来） |
| 缓存里存了坏东西 | `post-if: success()` 决定了红的跑批不写缓存 | Actions → Caches 里删掉 `nix-Linux-*`，或改一下 `flake.nix` 换键 |

## 9. review 与留给人的

**本地全量**：`nix develop --command ./scripts/ci.sh` **全绿**——改动前 1m52s、改动后 1m38s
（差别是宿主机上别的 agent 的负载，不是本票的效果；本票不碰任何被 `ci.sh` 检查的东西，
这两趟是回归性质的）。`dotnet fantomas --check .` 在 `ci.sh` 内跑过。
两条 workflow 都用 `yaml.safe_load` 解析过，job 与 permissions 结构逐项核对。

**code-review（Standards 轴，fixed point `7d3e19c0`）**：本票零 F# / TS 改动，
`docs/agents/fsharp-style.md` 与 `scripts/check-style.sh` 都不适用；对照的是 `AGENTS.md`、
`docs/agents/issue-tracker.md`、`docs/agents/triage-labels.md`、RUNBOOK，加 Fowler 味道基线。

- **硬违规：0 条**。jj 全程（无 git 命令）；未动 `CONTEXT.md` / `docs/adr/` / 别人的票；
  `Status:` 用的是 `triage-labels.md` 里的原字串；决定进了 `DECISIONS.md` 末尾的「## 45」段。
- **仓库惯例（从既有 workflow 提取）：合**。action 一律钉在**发布 tag**上
  （`cache-nix-action@v7` 是它当前的最新 release，2026-01-08 发布 / 01-30 更新，
  用 GitHub Releases API 核的，**不是搜索结果的转述**——M1 第 4 条规矩）；
  每一步都留了「为什么这么写」的中文注释；预热那一步的存在理由（可观测）写在注释里，
  不是暗桩。另修掉一处**既有的错注释**（它叫人用 `determinate-nix-version` 钉 Nix 版本，
  而那个输入在 action 的 `action.yml` 里根本不存在）与一处**已不成立的断言**（「仓库目前没有远端」）。
- **味道基线：1 条判断题（记录，未改）——Duplicated Code**。
  缓存键 `nix-${{ runner.os }}-${{ hashFiles('flake.nix', 'flake.lock') }}` 在两条 workflow 里各写一遍，
  **两处必须逐字一致才共用得上同一份缓存**（改一边忘一边→ Pages 那条静静地永不命中，
  没有任何东西会变红）。治法是抽一个 `.github/actions/nix-toolchain/action.yml` 复合 action，
  把「装器 + 缓存 + 预热」三步收成一处（`save` 做成输入）。**本票没做**：那是另一层
  我一次远端都验不了的间接，而现在这两段 YAML 是一眼看得出对错的。暂时的兵器是两边注释互相点名。
  等缓存在远端真跑绿了，这一步值得单独收一次。
- 其余味道（Speculative Generality 尤其）：没有多写一个用不上的输入——
  `gc-max-store-size` / `purge*` / `restore-prefixes-*` / `paths` **一个都没写**，理由在 §3。

**留给人的三条**：

1. **要不要把 Nix 版本也钉住**：换成 `DeterminateSystems/determinate-nix-action@v3.21.9`
   （= 本机版本），CI 跑的 Nix 就不再是移动靶，缓存键也可以把它算进去。本票没做，理由见 §2 末。
2. **那句 `FlakeHub is disabled…` 的 info 仍在**（§5 末）。若主人认为「印着『你配错了』的 info
   本身就不可接受」，那唯一的路是 `determinate: false`（换回上游 Nix，且该选项官方已宣布停止支持），
   我判断代价不划算——请裁决。
3. **`ci.yml` 开头那句「仓库目前没有远端」已删**（本票在改这个文件，而它已经不成立：
   调度器正在远端跑 CI）。改成了它本来想说的那句不变量：CI 与本地跑的是**逐字同一份** `ci.sh`。
