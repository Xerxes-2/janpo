# 45 — 远端 CI 加缓存，并消掉那条每次都印的告警

**What to build:** 远端 CI **6m25s**，本地 `./scripts/ci.sh` 约 **1m**，差的五分钟基本是每次重新拉整套
工具链（`flake.nix` 钉住的 dotnet SDK / node / pnpm / uv）——目前除了安装器本身**没有任何 nix 缓存**。
另外每次跑都印一条 `FlakeHub Login failure: determinate-nixd failed with exit code 1`
（无 token 导致，无害，但每次都在），**升级到 `nix-installer-action@v22` 之后仍在**，
所以它来自安装器自身而不是版本落后。

**Blocked by:** None — can start immediately.

**Status:** ready-for-agent

- [ ] 给远端加 nix 存储缓存（`magic-nix-cache`、`cachix`、或 `actions/cache` 缓存 `/nix/store`——
      自己比较并说清选择理由与代价）
- [ ] 给出**实测对照**：加之前 6m25s，加之后多少；缓存命中与未命中两种情况各测一次
      （首跑必然不命中，别把首跑当成结论）
- [ ] 消掉或如实静音那条 FlakeHub 告警：**先查清它到底想登录什么、不登录会缺什么**，
      再决定是配置它还是关掉它。**不许用「反正无害」当理由留着**——每次都印的噪音会训练人忽略日志
- [ ] `ci.yml` 与 `pages.yml` 两条 workflow 都要照顾到（它们都跑 nix）
- [ ] 不许为了变快牺牲闸门：**十余道闸门一道都不能跳过或有条件跳过**

## 边界

- [ ] 不碰 `scripts/ci.sh` 的内容（它是本地与远端共用的那一份，**「本地绿=远端绿」靠的就是它逐字相同**）
- [ ] 不碰 `flake.nix` 钉住的版本
- [ ] 不引入第三方缓存服务的账号依赖，除非在报告里说清「没有它 CI 是否照样能跑」

## 为什么现在做

M1 的票数与测试量还小，6 分钟无所谓；M2 的票更多、测试更重、还要加 UI 闸门。
**慢 CI 会改变行为**——它让人少推、攒着推、或在本地跳着测。
