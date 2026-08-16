#!/usr/bin/env bash
# CI 关卡。本地与 CI 跑的是同一个脚本，CI 只负责把它放进 nix dev shell 里跑：
#   nix develop --command ./scripts/ci.sh
set -euo pipefail

cd "$(dirname "$0")/.."

# 会被 Fable 编的工程必须留在 Fable 兼容的子集内：只允许下面这些 NuGet 包。
# 要加新包，先确认它能被 Fable 编译，再改这份名单（连同一条 ADR 或 DECISIONS 记录）。
FABLE_ALLOWED_PACKAGES=("FSharp.Core" "Fable.Core" "Thoth.Json.Core")

# 用法：check_fable_dependencies <fsproj> [允许引用的工程文件名...]
# 工程引用也得白名单：一个 Fable 子集的工程引了个 dotnet-only 的工程，同样破功。
check_fable_dependencies() {
  local project="$1"
  shift
  local allowed_projects=("$@")
  local failed=0

  while read -r package; do
    [[ -z "$package" ]] && continue
    local allowed=0
    for candidate in "${FABLE_ALLOWED_PACKAGES[@]}"; do
      [[ "$package" == "$candidate" ]] && allowed=1
    done
    if [[ "$allowed" -eq 0 ]]; then
      echo "$project 引入了不在 Fable 允许名单里的包：$package" >&2
      failed=1
    fi
  done < <(sed -n 's/.*PackageReference Include="\([^"]*\)".*/\1/p' "$project")

  while read -r reference; do
    [[ -z "$reference" ]] && continue
    local allowed=0
    for candidate in ${allowed_projects[@]+"${allowed_projects[@]}"}; do
      [[ "$(basename "$reference")" == "$candidate" ]] && allowed=1
    done
    if [[ "$allowed" -eq 0 ]]; then
      echo "$project 引用了不该引用的工程：$reference" >&2
      failed=1
    fi
  done < <(sed -n 's/.*ProjectReference Include="\([^"]*\)".*/\1/p' "$project")

  [[ "$failed" -eq 0 ]] || exit 1
  echo "$project 依赖检查通过（Fable 允许名单）"
}

echo "== dotnet =="
dotnet --version

echo "== Fable 侧工程的依赖名单 =="
check_fable_dependencies "src/Janpo.Engine/Janpo.Engine.fsproj"
# 黄金用例（票 21）也要过 Fable，只准引引擎。
check_fable_dependencies "src/Janpo.Golden/Janpo.Golden.fsproj" "Janpo.Engine.fsproj"

echo "== dotnet tool restore =="
dotnet tool restore

echo "== fantomas --check =="
dotnet fantomas --check .

echo "== 风格闸门 =="
bash "$(dirname "$0")/check-style.sh"

echo "== build =="
dotnet build janpo.slnx --configuration Release

echo "== test =="
dotnet test janpo.slnx --configuration Release --no-build

# JS 侧（M1 起）：Biome + Fable + Vite + 浏览器内与 dotnet 侧的曳光弹对拍 + 浏览器内的黄金用例。
# 黄金用例（票 21）两侧读同一份 `tests/fixtures/golden/`：dotnet 侧在上面的 `dotnet test` 里，
# JS 侧在下面的 ci-web.sh 里，两道同一个关卡。
# 拆成单独一个脚本是为了能单跑（`./scripts/ci-web.sh`），不用每次重跑整套 dotnet 关卡。
echo "== web =="
bash "$(dirname "$0")/ci-web.sh"

echo "== CI 全绿 =="
