# Agent 层的用例固件（票 23，票 24 加了 Assisted 档，票 25 加了危险度）

**CI 里绝不调真实 API**（M1 增量约束 6）。这里的文件让「兜底闭环」在 CI 里是确定性的。

两类文件：

| 文件 | 是什么 | 怎么来的 |
|---|---|---|
| `decision-*.json` | 决策包（`DecisionPackage.encoder` 的产物，**含 `scaffold`**） | `janpo decide 2088 --steps 6`（打牌那一手）、`--steps 5`（响应那一手）与 `janpo decide 99 --steps 6 --seat 3`（**带危险度**那一手） |
| `ask-*.json` | 模型的一次回答（`AskResult` 的形状） | `pnpm run record:agent`，**真的问过 DeepSeek** |

每个 `ask-*.json` 的 `_note` 写着它是怎么录出来的。四条失败路径各一份，加上两档各一份合法输出：

- `ask-legal` —— 合法输出（**Bare 档**）：调了 `choose_action`，给的 id 在包里（`action_id: "2"`）
- `ask-assisted` —— 合法输出（**Assisted 档**）：同一份决策包、同一个模型，只换了 prompt 档位。
  它的理由里引了脚手架的数（「66枚19种」「会退向」），输入 token 从 814 涨到 2183
- `ask-danger` —— 合法输出（**Assisted 档、带危险度那一手**，票 25）：`decision-danger.json` 的 prompt。
  它选了排第一位的现物（id=0），理由里原话引了「对家现物」与「宝牌周边」
- `ask-text-only` —— 格式跑偏：不注册工具时模型只回文字
- `ask-aborted` —— 超时：`stopReason: "aborted"`，**不抛异常**（票 18 的实测结论）
- `ask-error-bad-key` —— provider 报错：401 的原文，同样是值不是异常

**「越界 id」那条路径没有单独的录制**：`ask-legal`（id=2）放到 `decision-response.json`
（只有 id 0 / 1 的那一手）上就是一个越界 id。同一条真实响应，两种判读。

## 重录

```sh
cd web
JANPO_KEY_FILE=/tmp/deepseek_key pnpm run record:agent               # 全部重录
JANPO_KEY_FILE=/tmp/deepseek_key pnpm run record:agent ask-assisted   # 只重录点名的那份
```

**决策包重生成**（例：`scaffold` 加了字段）：

```sh
dotnet run --project src/Janpo.Cli -- decide 2088 --steps 6 > web/tests/fixtures/agent/decision-dahai.json
dotnet run --project src/Janpo.Cli -- decide 2088 --steps 5 > web/tests/fixtures/agent/decision-response.json
dotnet run --project src/Janpo.Cli -- decide 99 --steps 6 --seat 3 > web/tests/fixtures/agent/decision-danger.json
cd web && pnpm run format
```

**key 只从文件读**，不进代码、不进固件、不进提交（录完 grep 一遍确认）。
重录之后逐个看 diff：这些文件是「模型真的这么答过」的证据，改动要看得懂。
