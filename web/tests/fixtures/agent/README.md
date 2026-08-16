# Agent 层的用例固件（票 23）

**CI 里绝不调真实 API**（M1 增量约束 6）。这里的文件让「兜底闭环」在 CI 里是确定性的。

两类文件：

| 文件 | 是什么 | 怎么来的 |
|---|---|---|
| `decision-*.json` | 决策包（`DecisionPackage.encoder` 的产物） | `janpo decide 2088 --steps 6`（打牌那一手）与 `--steps 5`（响应那一手） |
| `ask-*.json` | 模型的一次回答（`AskResult` 的形状） | `pnpm run record:agent`，**真的问过 DeepSeek** |

每个 `ask-*.json` 的 `_note` 写着它是怎么录出来的。四条路径各一份：

- `ask-legal` —— 合法输出：调了 `choose_action`，给的 id 在包里（`action_id: "2"`）
- `ask-text-only` —— 格式跑偏：不注册工具时模型只回文字
- `ask-aborted` —— 超时：`stopReason: "aborted"`，**不抛异常**（票 18 的实测结论）
- `ask-error-bad-key` —— provider 报错：401 的原文，同样是值不是异常

**「越界 id」那条路径没有单独的录制**：`ask-legal`（id=2）放到 `decision-response.json`
（只有 id 0 / 1 的那一手）上就是一个越界 id。同一条真实响应，两种判读。

## 重录

```sh
cd web
JANPO_KEY_FILE=/tmp/deepseek_key pnpm run record:agent
```

**key 只从文件读**，不进代码、不进固件、不进提交（录完 grep 一遍确认）。
重录之后逐个看 diff：这些文件是「模型真的这么答过」的证据，改动要看得懂。
