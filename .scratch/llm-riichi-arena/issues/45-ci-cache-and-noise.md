# 45 — 远端 CI 加缓存，并消掉那条每次都印的告警

**What to build:** 远端 CI **6m25s**，本地 `./scripts/ci.sh` 约 **1m**，差的五分钟基本是每次重新拉整套
工具链（`flake.nix` 钉住的 dotnet SDK / node / pnpm / uv）——目前除了安装器本身**没有任何 nix 缓存**。
另外每次跑都印一条 `FlakeHub Login failure: determinate-nixd failed with exit code 1`
（无 token 导致，无害，但每次都在），**升级到 `nix-installer-action@v22` 之后仍在**，
所以它来自安装器自身而不是版本落后。

**Blocked by:** None — can start immediately.

**Status:** ready-for-human

- [x] 给远端加 nix 存储缓存（`magic-nix-cache`、`cachix`、或 `actions/cache` 缓存 `/nix/store`——
      自己比较并说清选择理由与代价）
      → 选了 `nix-community/cache-nix-action@v7`（缓存整个 `/nix`，后端是 GitHub 自带的缓存，
      无账号无 secret）；三家**二进制缓存**全被判掉，因为本仓库一个 nix 包都不构建。
      比较表与理由见报告 §2、§3
- [ ] 给出**实测对照**：加之前 6m25s，加之后多少；缓存命中与未命中两种情况各测一次
      （首跑必然不命中，别把首跑当成结论）
      → **agent 无推送权限，这一条只能由调度器完成**。本地已把可拆的量拆完
      （135 条路径 / 下载 463 MiB / 解开 1.5 GiB；nixpkgs 源码 330 MiB、5.3 万个文件），
      远端该读哪几个数、两次怎么跑、三种结果各怎么读：报告 §6
- [x] 消掉或如实静音那条 FlakeHub 告警：**先查清它到底想登录什么、不登录会缺什么**，
      再决定是配置它还是关掉它。**不许用「反正无害」当理由留着**——每次都印的噪音会训练人忽略日志
      → 根因：`determinate` 输入默认 true → 装完必定跑一次 `determinate-nixd login github-action`；
      拿得到 OIDC 时失败就 `core.warning` 一条注解。它想登的是私有 flake 与 FlakeHub Cache
      （**仅付费账号**），两样我们都不用，不登什么都不缺。处置：把 `id-token: write`
      从装 Nix 的那个 job 上收回（只给 `deploy`），**那条代码路径根本走不到**。
      残留的那句 info 与它的唯一清除办法（代价不划算）写在报告 §5 末，已请主人裁
- [x] `ci.yml` 与 `pages.yml` 两条 workflow 都要照顾到（它们都跑 nix）
      → 同一个缓存键、同一份 paths；`ci.yml` 写、`pages.yml` 只读（`save: false`）
- [x] 不许为了变快牺牲闸门：**十余道闸门一道都不能跳过或有条件跳过**
      → 零新增 `if:`，闸门那一步仍是整份 `nix develop --command ./scripts/ci.sh`（报告 §7）

## 边界

- [x] 不碰 `scripts/ci.sh` 的内容（它是本地与远端共用的那一份，**「本地绿=远端绿」靠的就是它逐字相同**）
      → `ci.sh` 与 `ci-web.sh` 都零改动（票 49 正在改后者）
- [x] 不碰 `flake.nix` 钉住的版本 → `flake.nix` / `flake.lock` 零改动
- [x] 不引入第三方缓存服务的账号依赖，除非在报告里说清「没有它 CI 是否照样能跑」
      → 零账号、零 secret；用的是仓库自己的 Actions 缓存额度。把那两步删掉 CI 行为不变（只是慢回今天）

## 为什么现在做

M1 的票数与测试量还小，6 分钟无所谓；M2 的票更多、测试更重、还要加 UI 闸门。
**慢 CI 会改变行为**——它让人少推、攒着推、或在本地跳着测。
