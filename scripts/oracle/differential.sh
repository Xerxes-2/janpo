#!/usr/bin/env bash
# 向听数与现成实现（Python 的 mahjong 库）随机对拍。
#
#   scripts/oracle/differential.sh [手数] [随机种子]
#
# 做三件事：产出随机手牌与 oracle 向听数 → 喂给 `janpo shanten --batch` → 比对。
# 差异必须为 0；不为 0 就是引擎错了，改引擎，不许改期望值。
#
# oracle 属于**测试期外部工具**：只在这个脚本里用 uv 拉起，`dotnet test` 与
# `scripts/ci.sh` 都不碰它（引擎的依赖白名单由 ci.sh 把着）。
# 提交进仓库的固定用例集见 tests/Janpo.Engine.Tests/fixtures/shanten-oracle.tsv，
# 由 scripts/oracle/refresh-fixture.sh 重新生成。
set -euo pipefail

cd "$(dirname "$0")/../.."

count="${1:-100000}"
seed="${2:-20260816}"
workdir="$(mktemp -d)"
trap 'rm -rf "$workdir"' EXIT

echo "== 生成 $count 手（seed=$seed）并取 oracle 向听数 =="
uv run scripts/oracle/shanten_oracle.py generate --count "$count" --seed "$seed" >"$workdir/cases.tsv"

echo "== 引擎批量计算 =="
dotnet build src/Janpo.Cli/Janpo.Cli.fsproj --configuration Release >/dev/null
cut -f1 "$workdir/cases.tsv" |
  dotnet run --project src/Janpo.Cli --configuration Release --no-build -- shanten --batch >"$workdir/engine.txt"

cut -f2 "$workdir/cases.tsv" >"$workdir/oracle.txt"

echo "== 比对 =="
if diff -u "$workdir/oracle.txt" "$workdir/engine.txt" >"$workdir/diff.txt"; then
  echo "对拍 $count 手，差异 0 处"
else
  echo "对拍 $count 手，出现差异：" >&2
  paste "$workdir/cases.tsv" "$workdir/engine.txt" |
    awk -F'\t' '$2 != $3 { print "oracle=" $2 " engine=" $3 "  " $1 }' |
    head -20 >&2
  exit 1
fi
