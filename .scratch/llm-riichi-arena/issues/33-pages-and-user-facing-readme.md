# 33 — GitHub Pages 部署与面向用户的 README

**What to build:** 让陌生人**打开一个链接就能玩**：把静态站部署到 GitHub Pages，
并把 README 重写成**纯面向用户**的样子 —— 读者是想玩的人，不是想读代码的人，
**也不关心这个项目是怎么造出来的**。

主人的原话：「README 我是想单纯面向用户的，不需要别人了解怎么开发以及我是怎么开发的。」

**Blocked by:** None — can start immediately.

**Status:** ready-for-agent

## 一、Pages 部署

- [ ] 一个 pages workflow：只在默认分支推送时跑，构建产物是 `web/dist`
- [ ] **`base` 路径必须可配**：本地与既有无头脚本默认 `/`，只在 Pages 构建时设成 `/janpo/`
      （环境变量注入）。**理由**：`web/scripts/verify-*.mjs` 靠 vite preview 打开页面，
      base 一写死那些闸门全断。改完要真跑一遍 `./scripts/ci.sh` 确认没断
- [ ] 仓库地址按 `https://xerxes-2.github.io/janpo/` 假定（owner `Xerxes-2`、repo `janpo`）。
      仓库名若不同只该改**一处**，把那一处标清楚
- [ ] 构建要能在 CI 里装 dotnet + node 并跑 Fable（照 `.github/workflows/ci.yml` 的现成写法）
- [ ] 部署完自己开一次线上地址，确认**真的能跑起来**（无头打开 Pages 构建产物即可，
      不必等真部署——但要验 base 路径下资源都加载得到）

## 二、README 重写（纯用户向）

- [ ] **砍掉**「这个仓库是怎么造出来的」整节（跑批流程、`.scratch/` 导览、三个真事）。
      日志文件仍然公开，但 README **一个字都不提**
- [ ] **砍掉**开发命令手册，整节搬到 `docs/development.md`，README **末尾留一行链接**
- [ ] 首屏：一句话说清是什么 → **在线试玩链接** → 截图 → 三五行英文简介 → WIP 告示
- [ ] 「怎么玩」写给用户：开链接 → 填 provider/模型/API key → 选座位与脚手架档位 → 开局 →
      播放/单步/看视角 → 导出牌谱。**不出现 nix、dotnet、pnpm、Fable 这些词**
- [ ] **安全说明**（诚实、简短）：页面纯静态无后端，key 只存在你自己浏览器的 localStorage，
      请求由你的浏览器直发 provider；建议用有额度上限的 key。订阅制 OAuth 在浏览器里用不了，只能填 API key
- [ ] 想接本地模型（Ollama / LM Studio）的用户指向 `docs/host/custom-endpoint.md`，
      并提一句 **Pages 是 https 页面**，浏览器会问「本地网络访问」权限（票 30 实测过，别自己改写结论）
- [ ] 「不是什么」那节**保留**（无实时观战、无服务端、危险度是启发式而非概率模型）——
      它挡掉的是错期待，属于用户向
- [ ] 路线那节保留但**写给用户看**（能玩到什么、还差什么），不要列票号

## 三、边界

- [ ] **不碰** `web/src/styles.css` 与 `docs/images/table.png`（票 32 正在改它们）；
      README 引用图的路径不要变
- [ ] 不碰引擎、不碰 `web/src/agent/`（票 31 正在改）、不碰 `CONTEXT.md`
- [ ] 现有 README 里那些**已核实过的数字与声称**（黄金用例条数、回放规模、兜底行为）
      若还留在用户向 README 里，**不许改动数字**；写不进用户向叙述的就整条删掉，不许改弱成含糊的话
- [ ] 不做 CI 徽章（远端地址要主人推完才定），在报告里提醒他自己加
