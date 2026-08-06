#!/usr/bin/env bash
set -euo pipefail
# 同步 Shared 源码（不含 bin/obj 等 MSBuild 生成物）到 Unity asmdef 目录。
# 单一编译源：Shared/ 是权威，Unity 侧副本仅为构建产物，由本脚本幂等重建。
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SRC="$ROOT/Shared"
DST="$ROOT/Unity/Assets/Crystal/Shared.Runtime/Shared"

rm -rf "$DST"
mkdir -p "$DST"
find "$SRC" -name "*.cs" -not -path "*/bin/*" -not -path "*/obj/*" -print0 |
  while IFS= read -r -d '' f; do
    rel="${f#"$SRC"/}"
    mkdir -p "$(dirname "$DST/$rel")"
    cp "$f" "$DST/$rel"
  done
N="$(find "$DST" -name '*.cs' | wc -l)"
echo "synced $N .cs files -> $DST"
