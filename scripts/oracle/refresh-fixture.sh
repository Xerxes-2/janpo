#!/usr/bin/env bash
# 重新生成提交进仓库的 oracle 对拍固件。
#
#   scripts/oracle/refresh-fixture.sh [手数] [随机种子]
#
# 固件让 `dotnet test` 能离线跑对拍（CI 里没有 Python，引擎也不许有 Python 依赖）；
# 大批量的现跑对拍见 scripts/oracle/differential.sh。
# 种子与手数写死在默认值里，改动固件时把这两个参数一起记进报告，才谈得上可复现。
set -euo pipefail

cd "$(dirname "$0")/../.."

count="${1:-4000}"
seed="${2:-20260816}"
target="tests/Janpo.Engine.Tests/fixtures/shanten-oracle.tsv"

mkdir -p "$(dirname "$target")"

{
  echo "# 向听数 oracle 对拍固件 —— 由 scripts/oracle/refresh-fixture.sh 生成，不要手改。"
  echo "# oracle: PyPI 的 mahjong==2.0.0（uv run scripts/oracle/shanten_oracle.py）"
  echo "# 生成: --count $count --seed $seed"
  echo "# 每行两列（tab 分隔）：「<副露数> <mjai 记法...>」与 oracle 算出的向听数。"
  uv run scripts/oracle/shanten_oracle.py generate --count "$count" --seed "$seed"
} >"$target"

echo "已写入 $target（$count 手）"
