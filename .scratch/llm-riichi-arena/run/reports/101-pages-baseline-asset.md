# 票 101：站点上那份产物 —— 实现报告

**结论先说四句。**

1. **选了第 3 条路的一个变体：Actions 缓存那 6 MB **产物本身**（不是 cargo 的 target 目录），
   没命中就现造，造不出来**不阻断部署**。** 稳态下部署多花约 **10 s**，
   重造那一次多花估 **1.5–2.5 分钟**，**人手步骤 0 步**。
2. **三条路的数逐条量在第 ① 节**，没量到的那几项写明了为什么（判据 4）。
   决定这件事的两个数是：cargo 冷编 **43 s 且加核心没用**（串行尾巴），
   而那份缓存只有 **4.75 MB**——与票 45 那次「restore 20 s vs 冷拉 18 s」的净亏差两个数量级。
3. **workflow 改动在本机用 `act` 跑过四趟**（冷造 / 命中 / 造不出来 / 许可缺一份），
   其中后两趟是破坏实验。`act` 顺手抓出**两条真 bug**：`hashFiles` 匹配不到文件时
   静静返回空串（缓存键退化成常量），以及默认 shell 没有 `pipefail`（`脚本 | tee` 那一步永远绿）。
4. **降级那一路没被这一票破坏**：CI 里那 6 MB 仍旧不在场，因此它照旧是 CI 的主路；
   而「把资产地址改坏」那个构造走的是 `route(...).abort`，**在产物在场时同样红得起来**
   ——`--asset` 那一档（产物在场）第 ② 趟原样绿。

改到的文件：

| 文件 | 改了什么 |
| --- | --- |
| `.github/workflows/pages.yml` | 加 5 步：算缓存键 → 取缓存 → 造（没命中才跑、失败不阻断）→ 存 → 核发布件 |
| `scripts/build-baseline-wasm.sh` | **新增**。造那 6 MB 并放进 `web/public/baseline/`。本机与 Pages 跑同一份 |
| `scripts/check-pages-dist.sh` | **新增**。发出去之前核 `web/dist`：三份许可、产物是不是一份像样的 wasm |
| `web/public/baseline/README.md` | 「怎么造」改成一条命令；「上线」那节写清今天真实的流水线形态 |
| `probe/akagi-wasm/README.md` | 那句「`flake.nix` 里现在什么都不用加」补上这一票量出来的理由 |

**没碰**：`flake.nix`（理由是量出来的，见 ①-4）、`ci.yml`、`scripts/ci.sh` / `ci-web.sh`、
`web/src/**`、`src/Janpo.Web/**`、`tests/**`、`CONTEXT.md`、`docs/adr/*`、别人的票。
**那 6 MB 一个字节都没进版本控制**（`web/public/baseline/.gitignore` 排掉 `*.wasm`；
`jj st` 里只有上面五份文本）。

---

## ① 三条路，逐条的数

**测量条件**：本机 AMD 9950X3D（16 核 32 线程）、`nice -n 19`、`/tmp` 是 tmpfs（内存盘），
机器上另有一个 agent 在跑票 100。**本机的数一律偏乐观**（单核比 runner 快、盘比 runner 快），
凡是要外推到 runner 的地方都标着「估」。

### 路 1：pages.yml 里现造

| 量 | 数 | 怎么测的 |
| --- | ---: | --- |
| cargo 冷编（空 target 目录，**全机 32 线程**） | **43.11 s** | `/usr/bin/time -v`，`--offline`，user 81.0 s / 198% CPU |
| cargo 冷编（`taskset -c 0-3` + `-j 4`，**贴近 4 vCPU 的 runner**） | **43.81 s** | 同上，user 72.7 s / **170% CPU** |
| 拉上游（`fetch-upstream.sh`，sparse + blobless） | **1.57 s** / 4.9 MB | 空目录里真拉了一次 |
| `rustup target add wasm32-unknown-unknown`（已装） | **0.022 s** | 本机 |
| 同上（**没装时要下的那一份**） | **21,949,220 B** | `curl -I static.rust-lang.org/dist/rust-std-1.97.1-wasm32-unknown-unknown.tar.xz`（权威源） |
| 产物 | **6,039,832 B**；gzip -9 **4,794,245**；brotli -q11 **4,600,271** | 本机 |
| target 目录 | **325 MB** | `du -sh`（票 91 报告里那个「863 MB」含原生 `--bin parity` 与 debug，wasm 这一条只要 325 MB） |

**第一个要害：加核心没用。** 32 线程与 4 核跑出来**同一个 43 s**，CPU 占用只有 170–198%
——这条编译链有一条很长的**串行尾巴**（`lto = true` + `codegen-units = 1`，加上 candle/gemm
那几个大 crate 本身就是单条依赖链）。**于是 runner 的核数不重要，单核速度才重要**：
ubuntu-latest 是 4 vCPU 的云机，单核大约比本机慢 1.5–2.5 倍 ⇒ **估 65–110 s**（判据 14 补强二：
这是标着假设的外推，不是除法）。

**第二个要害：工具链不必装，更不必进 `flake.nix`。**
**runner 镜像自带 Rust**——`actions/runner-images` 的 `Ubuntu2404-Readme.md`（权威源，不是搜索转述）
写着 `Cargo 1.97.1 / Rust 1.97.1 / Rustup 1.29.0`，**与本机同一版**（本机 `cargo --version` 也是 1.97.1）。
只缺 `wasm32-unknown-unknown` 那块 std（22 MB，一行 `rustup target add`）。

**把 Rust 塞进 `flake.nix` 的代价（ADR-0006 边界 6 把这件事留给了这一票）**：

```
$ nix build --no-link --dry-run nixpkgs#rustc
these 5 paths will be fetched (448.2 MiB download, 1.5 GiB unpacked)
$ nix build --no-link --dry-run nixpkgs#cargo
these 4 paths will be fetched (461.0 MiB download, 1.6 GiB unpacked)
```

dev shell 今天是 **463 MiB 下载 / 1.5 GiB 解开**（票 45 §1 实测）——加上 rustc + cargo 就是**翻倍**，
而**那份 shell 每一趟 CI 都要拉**（今天冷拉 18–21 s，见 ② 的分步表）。
**为一条只有 Pages 用得上的工具链，把每一趟 CI 都拖慢，不划算。结论：不进 flake.nix。**
（次要理由：nixpkgs 的 `rustc` 不保证带 wasm32 那块 std，真要用还得再引 fenix/oxalica 一层。）

### 路 2：一次造好挂成 Release 资产

| 量 | 数 | 怎么测的 |
| --- | ---: | --- |
| 下载一份 GitHub Release 资产 | 13,069,957 B / **2.39 s**（5.5 MB/s） | `curl -L` 拉 `cli/cli` 的一份资产；**本机家宽，runner 在 Azure 上更快** ⇒ 6 MB **估 ≤ 1.2 s** |
| pages.yml 那一步要写的 | 一句 `gh release download` + `contents: read` | 不必新加权限 |
| **人手步骤（每次上游 pin 或 crate 变了）** | **4 步** | ①本机造 ②`gh release create/upload` ③改 workflow 里的 tag ④重跑部署 |
| 没量到 | `gh release upload` 的真实耗时、第一份资产上传 | **本票没有远端权限，造不出 release** |

**判掉它的不是那 1.2 s，是那 4 步人手**。这一票的题目就是「东西做完了但没人把它送上去」——
再选一条**依赖人记得手动重造**的路，等于把同一个坑挪后一格：
上游 pin 一改，线上那份就悄悄地是旧的，而**没有任何东西会变红**。
（它另有一条好处：产物与源码彻底解耦，重造不占部署时间。若哪天编译时间涨到分钟级以上，
它值得回头再议——那时该做的是「CI 自动 `gh release upload`」，即路 2 的自动化版。）

### 路 3：Actions 缓存

**两个变体，数不一样，别混着说。**

**3a 缓存 cargo 的 target 目录**（字面意义上的「缓存 cargo 产物」）：

| 量 | 数 |
| --- | ---: |
| target 目录 | 325 MB |
| tar + zstd -3 -T4 之后 | **111,722,879 B（107 MiB）** |
| **恢复之后仍要编的那一段** | **7.06 s** |

最后一行是关键：`.upstream/native_bot` 是**路径依赖**，而它每趟都是新 clone 下来的
（mtime 全新）⇒ cargo 必定重编 `native_bot` + 叶子 crate（含 LTO）。实测把上游源码 `touch`
一遍再编就是这 7 s。**也就是说 107 MiB 的缓存换回 36 s**——能省，但要背一份 107 MiB
的传输与仓库缓存额度。

**3b 缓存那 6 MB 产物本身**（选的这条）：

| 量 | 数 | 怎么测的 |
| --- | ---: | --- |
| 缓存条目大小 | **4,750,987 B（~4.75 MB）** | `act` 真跑出来的 `Cache Size`（内部是 tar + zstd） |
| 命中时还要编的 | **0** | 造那一步整步跳过 |
| 命中那一趟本机全程 | **1.56 s** | `act` 墙钟（含取缓存、核发布件） |
| 未命中那一趟本机全程 | **1 m 43 s**（造那一步 46.8 s） | 同上，`.upstream/`、target 目录、缓存全清掉 |
| GH 上真实的 restore/save 耗时 | **没量到** | `act` 用的是本机缓存服务器。锚点：票 45 实测 GH 上恢复约 1 GiB 的 `/nix` 花 20 s ⇒ 4.75 MB **估 1–3 s** |

**为什么这次「加缓存」过得了票 45 那一关**：那次是 restore 20 s vs 冷拉 18 s（净亏 2 s，拆掉）；
**这次是 restore 估 1–3 s vs 冷造估 65–110 s——差两个数量级**。判据的要求是「先量后加」，
量完了，加。

**缓存的失效面**（写在明面上，它们都只让部署变慢，不会让站点变坏）：
① 那几份输入变了 → 重造一次（这正是我们要的）；② GitHub 对 **7 天没被读过**的缓存条目做逐出、
仓库总额度 10 GB（`ci.yml` 那份 `/nix` 缓存也在同一个池子里）→ 极端情况下每次部署都重造，
等于退回路 1；③ 缓存服务本身抽风 → `actions/cache` 自己降级成 miss。

---

## ② 部署时长：改前 / 改后

**改前是真数，不是估**——用 GitHub 的公开 API 读的最后一次 Pages 跑批
（run #127，head `fe7856ea` = 本票的 fixed point）：

| 步 | 耗时 |
| --- | ---: |
| `actions/checkout@v7` | 2 s |
| `nix-installer-action@v22` | 11 s |
| 预热 dev shell（= 拉工具链） | 21 s |
| 装依赖（pnpm） | 4 s |
| 恢复 dotnet 本地工具 | 5 s |
| 构建静态站（Fable + Vite） | 25 s |
| `upload-pages-artifact@v5` | 1 s |
| **build job 合计** | **75 s** |
| deploy job（`deploy-pages@v5`） | 9 s |
| **一次部署（created → updated）** | **92 s** |

**改后（缓存命中，也就是绝大多数次）**：

| 新增/变化 | 估 | 依据 |
| --- | ---: | --- |
| 算缓存键 | ~1 s | 一句 bash |
| 取缓存（4.75 MB） | 1–3 s | 票 45 的 `/nix` 恢复锚点外推 |
| 造产物 | **0**（整步跳过） | `if: cache-hit != 'true'` |
| 存缓存 | **0**（整步跳过） | `if: outcome == 'success'`，命中时没跑造那步 |
| Vite 多拷 6 MB | +0.1 s | 本机 `vite build` 1.58 s（含那 6 MB） |
| 上传 Pages 产物（3.9 MB → 9.7 MB） | +1–2 s | 改前那一步 1 s |
| 核发布件 | <1 s | 本机 11 ms |
| **合计** | **约 +5～10 s ⇒ 一次部署约 100 s** | |

**改后（未命中，即输入变了或缓存被逐出）**：再加 **拉上游 1.6 s + `rustup target add` ≤5 s +
cargo 65–110 s + 存缓存 1–3 s ≈ +1.5–2.5 分钟 ⇒ 一次部署约 3–4 分钟**。
**这一次只发生在该发生的时候**（造它的输入真的变了），其余每次都是 100 s 那一档。

---

## ③ 许可随产物上线（ADR-0006 边界 4）—— 证据

`web/public/` 里那三份文本由 Vite 原样拷进 `dist/`，**本机真构建了一遍**（`JANPO_BASE=/janpo/`）：

```
web/dist/third-party/:
-rw-r--r-- 10752 LICENSE-akagi.txt
-rw-r--r--  5414 NOTICE-akagi
-rw-r--r--  2437 README.md
web/dist/baseline/:
-rw-r--r-- 6039832 janpo-baseline.wasm
```

页脚那条链接指的是 `third-party/README.md`（`src/Janpo.Web/Footer.fs` 里唯一那一处，
按 `document.baseURI` 解析，因此 `/janpo/` 子路径下也不会 404）——**三份缺一份，那条链接就烂一条**。
于是这件事现在有执行体了（判据 2）：`scripts/check-pages-dist.sh` 逐份核，缺一份就红：

```
$ mv web/dist/third-party/NOTICE-akagi /tmp && ./scripts/check-pages-dist.sh
发布件闸门没过：许可件 web/dist/third-party/NOTICE-akagi 不在或是空的（ADR-0006 边界 4）
（退出码 1）
```

同一道闸门在 `act` 上也真的把整个 job 按红过（见 ⑤ 的第四趟）。

---

## ④ `--asset` 那一档：本机真推理跑过

```
$ ./scripts/build-baseline-wasm.sh
强 AI 基线产物：web/public/baseline/janpo-baseline.wasm
  字节：6039832
  sha256：bc139d6ccbbdd1ea889e2e6ec1024b15cbcfb47c1aabee74c640c23c51973c96

$ cd web && pnpm run fable && node scripts/verify-baseline.mjs --asset
那份产物 6039832 字节；页面上写的是：强 AI 基线已就位（座位 0，5.8 MiB）。它不会说话：没有思考气泡，也没有 token 账单。

首页与不选它的对局：那份资产的网络请求计数为 0（阳性对照：拨上它就恰好 1 次）✓
资产拉不动：页面明说原因、那一席退回自带 bot，其余席照常打完一局 ✓
它真出手了一整段，那一席仍旧没有气泡、没有账单行（阳性对照：同桌的模型席两样都有）✓
与真人同桌：他家三席的手牌行里一个 data-pai 都没有，那一席也不长气泡 ✓
本机演习：它真坐一席打完一局，牌谱里认得出它，一手都没兜底 ✓
```

**`./scripts/ci.sh` 全绿跑了两遍，一遍产物在场、一遍不在场**——两种世界都绿：

- 产物在场（full `ci.sh`）：`== CI 全绿 ==`，强 AI 基线那一道印的是「它真出手了一整段」。
- 产物不在场（`ci-web.sh`，**CI 上就是这个形态**）：那一道印的是
  「这一趟站点上没有那份产物，因此第 ②③④ 趟走的都是降级那一路」。

---

## ⑤ workflow 怎么自证的：`act` 五趟（判据 1）

**没有「推上去看看」。** 用 `act 0.2.89` + `-P ubuntu-latest=-self-hosted`（不拉容器镜像，
省掉 1.5 GB 的下载），把 `pages.yml` 里**新加的那几步用脚本抽出来**（逐字同一份）在本机跑。
两处 act 专用的替身写在那份 workflow 里：替 `actions/checkout` 把仓库拷进工作区、
替 Vite 把 `public/` 拷进 `dist/`。

| 趟 | 造的局面 | 结果 |
| --- | --- | --- |
| ① 冷 | 无缓存、无 `.upstream/`、无 target 目录 | 拉上游 → cargo 45.25 s → 产物 6,039,960 B → **`Cache saved`（4,750,987 B）** → 发布件闸门绿。全程 1 m 43 s |
| ② 命中 | 把产物、`.upstream/`、target 全删掉再跑 | `cache-hit=true`，**造与存两步整步没跑**，闸门绿。**全程 1.56 s** |
| ③ **破坏**：造不出来 | 往 crate 里塞一句 `compile_error!` | 那一步 ❌「Failed but continue next step」，**存缓存那步没跑**（坏产物进不了缓存），闸门印出「本次发布的站点上没有强 AI 基线那份产物」，**job 仍旧成功**（退出码 0）⇒ 部署不被阻断 |
| ④ **破坏**：许可缺一份 | 删掉 `dist/third-party/NOTICE-akagi` | 闸门 ❌，**job 失败**（退出码 1）⇒ 不许发出去 |
| ⑤ review 改完重跑 | 产物路径收成 job 级 `env: BASELINE_WASM` 之后 | 键跟着脚本变了 ⇒ 重造 → 存 → 闸门绿：`path:` 从 env 拿得到，取/存两步都好用 |

**act 顺手抓出的两条真 bug（都不是理论上的）**：

1. **`hashFiles` 一个文件都匹配不上时不报错，它返回空字符串** ——第一版跑出来的键是
   `baseline-wasm-Linux-`（后面什么都没有）。这在真 GH 上意味着：谁把 `probe/akagi-wasm/crate/`
   挪了个地方，缓存键就退化成**常量**，「上游 pin 改了要重造」这条不变量**静静地失效**，
   站点从此永远发一份照旧源码造的产物，而**没有任何东西会变红**。
   处置：加一步「算缓存键」，`digest` 为空就当场红，同时那一长串 `hashFiles` 只写一处
   （restore 与 save 不必各抄一遍——票 45 报告里点名过的那种「两处必须逐字一致」的坑）。
2. **默认 shell 没有 `pipefail`。** `./scripts/check-pages-dist.sh | tee -a "$GITHUB_STEP_SUMMARY"`
   在默认 shell（`bash -e {0}`）下，退出码是 `tee` 的 0 ——**那一步永远绿**，
   即判据 1/3 那种「一道从不失败的闸门」。act 第一版跑出来的正是这一幕：脚本压根没找到，
   那一步照样 ✅。处置：那两步写 `shell: bash`（GitHub 文档的原话：显式写 bash 时命令是
   `bash --noprofile --norc -eo pipefail {0}`，不写时是 `bash -e {0}`——权威源，不是搜索转述）。
   ④ 那一趟就是它的反向自证。

---

## ⑥ 降级那一路没变成没人跑的路（判据 3）

- **CI 里那 6 MB 仍旧不在场**：它不入版本控制，而 `ci.yml` 不造它（这一票一个字没动 `ci.yml`）。
  因此 `verify-baseline.mjs` 在 CI 上跑到的**照旧是降级那一路**——本机跑了一趟没有产物的
  `ci-web.sh` 复现了这个形态，原样绿（见 ④）。
- **「把资产地址改坏」的构造不靠运气**：那一趟（`degrades`）用的是
  `context.route(**/baseline/janpo-baseline.wasm).abort("failed")`，
  **与产物在不在场无关**。证据：`--asset` 那一档（产物真在场）第 ② 趟照旧绿。
  也就是说产物上线之后，这道断言仍然每趟都在执行，而不是「因为文件不存在所以顺便绿了」。
- **懒加载照旧**：`--asset` 那一档第 ① 趟仍是「首页与不选它的对局请求计数为 0，
  拨上它恰好 1 次」——产物在场时那一条阳性对照才是真的 200（CI 上它是 404 但请求真发出去了）。

---

## ⑦ 缓存头与体积：6 MB 到底重下几次

**Pages 的响应头我们几乎改不了**（没有 `_headers`、没有 CDN 配置面），实测的就是它给什么：

| 量 | 实测 |
| --- | --- |
| 本站任一资产（含内容哈希的 JS） | `cache-control: max-age=600`、`etag: W/"…"`、`content-encoding: gzip` |
| 别的 Pages 站上的 `.wasm`（`sql.js.org/dist/sql-wasm.wasm`，658,410 B） | `content-type: application/wasm`、**`content-encoding: gzip`（325,266 B）**、同样 `max-age=600` |

**结论逐条**：

1. **拿不到 `immutable`，也拿不到长 TTL**：Pages 一律 `max-age=600`。**改不了**——
   这不是我们的疏忽，是 Pages 不给这个口子（想要就得换托管，与 ADR-0003「静态站、不运维」相悖）。
2. **但「每次进站重下 6 MB」不成立**，两重原因：
   ① **懒加载**（ADR-0006 边界 1）——不选那一席就一个字节都不拉，首页永远不拉；
   ② TTL 过期之后浏览器带 `If-None-Match` 来一趟，产物没变就是 **304，正文 0 字节**。
   真正付那 6 MB 的只有「第一次选那一席」与「产物真的换了之后的第一次」。
3. **传输量**：gzip 后 **4.79 MB**（brotli 4.60 MB）。Pages 对 `application/wasm` 确实开 gzip
   ——**但我量到的最大一份只有 658 KB**，6 MB 的响应体会不会照样在边缘压，**只能上线验**（见 ⑧）。
   若它不压，第一次下载就是 6.04 MB 而不是 4.79 MB（差 26%）。
4. 票 91 那条「摘掉三麻权重省 42%」仍然挂着（票 92 §⑦ 说明了为什么不顺手做：它会触发
   Apache-2.0 §4(b)）。**这一票也没做**。

---

## ⑧ 只能上线验的几件事，以及回滚办法

| 只能上线验 | 为什么 | 上线后怎么读 |
| --- | --- | --- |
| Actions 缓存真实的 restore / save 耗时 | `act` 用的是本机缓存服务器，不过网 | 看那一步的耗时（预计 1–3 s） |
| runner 上 cargo 的真实耗时 | 本机比 runner 快，且 `/tmp` 是内存盘 | 第一次部署时「造产物」那一步的耗时（估 65–110 s） |
| Pages 会不会对 6 MB 的 `application/wasm` 开 gzip | 手上没有那么大的 wasm 在 Pages 上 | `curl -sI -H 'Accept-Encoding: gzip' …/baseline/janpo-baseline.wasm` 看有没有 `content-encoding` |
| 缓存逐出后的命中率 | 7 天窗口 + 10 GB 额度是长期行为 | 若哪天部署突然变成 3–4 分钟，就是重造那一档 |

**回滚（三级，越往下越彻底）**：

1. **产物出问题（造坏了 / 上游变了）**：去 Actions → Caches 删掉 `baseline-wasm-Linux-*` 那条，
   下次部署会重造。真要让站点先回到没有它的样子，把 `pages.yml` 里「造…」那一步删掉即可
   ——**站点会当场回到今天的形态**（那一席如实降级，页面自己说得出原因）。
2. **流水线出问题**：把新加的那 5 步整体删掉（它们连成一段、注释里自带边界），
   `pages.yml` 就逐字回到 `fe7856ea` 那一版。**它们没有改动任何既有步骤。**
3. **构建期的 Rust 完全不想要**：同 2。这一票**没有动 `flake.nix`**，
   所以回滚不牵扯工具链，`ci.yml` 一个字都没碰。

**「产物那一步失败不阻断部署」是对的吗——想清楚了，是对的。**
站点上没有那份产物时页面**如实降级**（ADR-0006 边界 2，而且它是线上验证过的：票 101 的由来
就是主人在线上看到了那句话）。为了一个**可选依赖**把整站的发布扣下来，
会连带拦住与它无关的十几样东西——那正是 ADR-0006 说的「它不是单点」。
代价是「没上线」变得不那么刺眼，所以补了两处**大声说话**的地方：
那一步在 Actions 里是红的，且跑批总结里有一句「本次发布的站点上没有强 AI 基线那份产物」。

---

## ⑨ 顺手撞出来的一件事：产物跟着**源码路径**变，换个目录就不是同一份字节

**两边都量了（判据 14：别把差额直接归因给最可疑的那一项）**：

- **同一源码目录、三个不同的 target 目录**（全机冷跑 / 4 核冷跑 / 脚本跑）：
  三份产物 **sha256 逐字相同**（`bc139d6c…`，6,039,832 B）——**编译本身是确定性的**。
- **换一个源码目录**（`act` 把仓库放在 `~/.cache/act/<每趟不同的哈希>/hostexecutor/` 下）：
  体积变成 **6,039,960 B**，且两趟 act 的哈希不同时 sha256 也不同。

原因是源码路径进了二进制（panic 位置那些字符串），而 `~/.cargo` 的路径也随用户不同。
**后果只有一个**：核对「线上那份是不是这一次发的」
要拿**那一次跑批总结里印的 sha256** 比，**不能**拿本机造的那份比。
要做到逐字可重现得给 rustc 加 `--remap-path-prefix`（两处：工作区与 cargo registry），
**本票没做**——它属于「供应链可核」那条线，值得单独一票，且不影响本票的任何判据。

---

## ⑩ review 结论（两轴，fixed point `uqlmotul` / `fe7856ea`）

**Standards 轴**（`AGENTS.md`、`docs/agents/*`、既有 workflow 与 `scripts/*.sh` 的惯例；
本票零 F#/TS 改动，`fsharp-style.md` 与 `check-style.sh` 不适用）：

- **硬违规 0 条**：全程 jj，无远端操作；没动 `CONTEXT.md` / `docs/adr/*` / 别人的票 / 别人的工作区；
  那 6 MB 没进版本控制；没有任何 key 进仓库。
- **仓库惯例：合**。action 一律钉在**发布 tag** 上——`actions/cache` 用 `@v6`
  （当前最新 release 是 **v6.1.0**，2026-06-26，用 Releases API 核的，不是搜索转述；
  `restore/` 与 `save/` 两个子 action 在 v6.1.0 里都在，查的是仓库内容 API）。
  两个新脚本沿用 `scripts/ci.sh` 的形状（`set -euo pipefail` + `cd "$(dirname "$0")/.."`
  + 中文注释写「为什么这么写」）。缓存键**不给 restore 前缀**，与 `ci.yml` 那条 `/nix` 缓存
  同一个理由（票 45：缓存内容永远严格等于这份输入，不一代代驮着旧东西走）。
- **Speculative Generality（改了）**：`check-pages-dist.sh` 第一版收一个可选参数（核哪个目录），
  **而一个调用方都没用过它**（流水线与本机都跑默认的 `web/dist`）——删了。
- **一处 Duplicated Code（改了一半）**：产物路径 `web/public/baseline/janpo-baseline.wasm`
  原本在 `pages.yml` 里写了两遍（restore 与 save 各一份 `path:`）。
  **按这份文件自己的惯例收成了一处**：job 级 `env: BASELINE_WASM`，
  与旁边那个 `JANPO_BASE`（注释原话：「部署地址的**唯一**来源…别处没有第二处写着路径」）同一个形状。
  改完用 `act` 重跑一趟，取/存两步依旧绿。
  **剩下的三处记录不改**：`build-baseline-wasm.sh`（它要能单独跑，拿不到 workflow 的 env）、
  `web/src/baseline/wasm.ts` 的 `ASSET_FILE`、`verify-baseline.mjs` 的 `ASSET`。
  **改一处忘一处的下场是「静静地不上线」**，今天靠 `check-pages-dist.sh` 兜底
  （dist 里没有它就大声说）——这条兜底本身就是本票加的。真要全收，收法是让 TS 侧那个常量
  与 workflow 读同一份 JSON——**不在这一票的边界里**。

**Spec 轴**（票 101 票面 + ADR-0006 六条边界）：

| 票面要的 | 落在哪 |
| --- | --- |
| 三条路各一组实测数 + 选一条 + 理由 | ① |
| 部署时长改前改后 | ② |
| 线上那条地址拿得到 200 | **只能上线验**（⑧ 给了判据与回滚） |
| 许可随产物上线 | ③（并做成了一道红得起来的闸门） |
| 懒加载不许被破坏 | ④⑥：`--asset` 那一档第 ① 趟原样绿 |
| 降级那一路不许失效 | ⑥ |
| 体积与缓存头 | ⑦ |
| 本地跑通构造链、不许 `cargo clean` | ①：全程 `CARGO_TARGET_DIR` 指到 `/tmp`，`/tmp/janpo-probe-target` 那 916 MB **一次都没动过**，冷跑用的是新目录 |
| `ci.sh` 全绿 + `--asset` 跑过 | ④ |
| workflow 能自证 | ⑤（四趟 `act`，两趟是破坏实验） |

边界逐条对过：ADR-0006 的 1（懒加载）2（降级）4（许可）6（不入版本控制 + 工具链去留）
都在上面有位置；3（预算按 0.7 ms）与 5（术语用通名）本票不涉及。

**票面没要、但做了的两样（各有理由）**：

1. **`scripts/check-pages-dist.sh`**——票面只说「确认一遍许可在 `web/dist` 里」。
   把它做成一道**每次发布都跑的闸门**而不是我看一眼，是判据 2（写下一条不变量，
   先问谁来执行它）：「许可随产物上线」要是只写在报告里，下一个改 `web/public/` 的人就能
   静静地把它弄掉。它同时是「产物真的上去了吗」的唤醒器（总结里那句话）。
2. **`probe/akagi-wasm/README.md` 里那句已过期的话**（「票 92 真要上线时才谈工具链」）
   改成了本票量出来的结论——那份文件在票面列的地盘里，而判据 15 说的正是「前提变了就回头重问那条决策」。

**未做的有一样**：票面那条「线上拿得到 200」在本票无法合上（没有远端权限，也不该有）——
票文件里那一格因此**故意留着不勾**，并写明推 main 之后怎么核。

---

## 留给人的待审项

1. **`ci.sh` 里没有许可闸门**：`check-pages-dist.sh` 只在 Pages 那条路上跑。
   要让「谁删了 `web/public/third-party/` 会当场红」，得往 `ci-web.sh` 加一行
   ——票面明令**不动 `ci.yml` 的六道 + 十八趟结构**，因此没做。
2. **产物路径写在五处**（见 ⑩ 的 Duplicated Code 一条），收法要跨 TS/YAML/bash 三种文件。
3. **Pages 对 6 MB 的 wasm 压不压**（⑦-3）：第一次部署之后一条 `curl` 就有答案，
   若不压，票 91 报告里那句「传输量 4.8 MB」在生产上要改成 6.04 MB。
4. **逐字可重现**（⑨）没做。
5. 票 92 挂着的那两条（摘三麻权重 −42%、模型席同族的过期问话）仍然挂着，本票没碰。
