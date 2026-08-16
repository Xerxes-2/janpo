#!/usr/bin/env bash
# CI 关卡。本地与 CI 跑的是同一个脚本，CI 只负责把它放进 nix dev shell 里跑：
#   nix develop --command ./scripts/ci.sh
set -euo pipefail

cd "$(dirname "$0")/.."

# 引擎工程必须留在 Fable 兼容的子集内：只允许下面这些 NuGet 包。
# 要加新包，先确认它能被 Fable 编译，再改这份名单（连同一条 ADR 或 DECISIONS 记录）。
ENGINE_ALLOWED_PACKAGES=("FSharp.Core" "Fable.Core" "Thoth.Json.Core")

check_engine_dependencies() {
  local project="src/Janpo.Engine/Janpo.Engine.fsproj"
  local failed=0

  while read -r package; do
    [[ -z "$package" ]] && continue
    local allowed=0
    for candidate in "${ENGINE_ALLOWED_PACKAGES[@]}"; do
      [[ "$package" == "$candidate" ]] && allowed=1
    done
    if [[ "$allowed" -eq 0 ]]; then
      echo "引擎工程引入了不在 Fable 允许名单里的包：$package" >&2
      failed=1
    fi
  done < <(sed -n 's/.*PackageReference Include="\([^"]*\)".*/\1/p' "$project")

  if grep -q 'ProjectReference' "$project"; then
    echo "引擎工程不应引用其它工程：$project" >&2
    failed=1
  fi

  [[ "$failed" -eq 0 ]] || exit 1
  echo "引擎依赖检查通过（Fable 允许名单）"
}

echo "== dotnet =="
dotnet --version

echo "== 引擎依赖名单 =="
check_engine_dependencies

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

echo "== CI 全绿 =="
