#!/usr/bin/env bash
# macOS 上验证跨平台核心：编译整个解决方案并运行测试套件。
# Windows 壳会以交叉编译方式一起构建，用于确认核心改动没有破坏 Windows 侧。
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONFIGURATION="${1:-Debug}"
cd "$ROOT"

echo "== build solution (${CONFIGURATION}) =="
dotnet build OpenClawDebugger.sln -c "${CONFIGURATION}"

echo
echo "== run tests =="
dotnet run --project tests/OpenClawDebugger.Tests/OpenClawDebugger.Tests.csproj -c "${CONFIGURATION}" --no-build
