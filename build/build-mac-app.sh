#!/usr/bin/env bash
# 把 macOS 壳打包成可双击运行的 .app bundle。
#
# 用法：
#   ./build/build-mac-app.sh            生成到 artifacts/OpenClawDebugger.app
#   ./build/build-mac-app.sh --install  生成后安装到 /Applications
#
# 环境变量：
#   CONFIG=Release     构建配置
#   RID=osx-arm64      目标架构（Intel 机器用 osx-x64）
#   OUT=<dir>          产物目录，默认 <repo>/artifacts
#
# 说明：壳现在是纯 AppKit（net10.0-macos），dotnet build 直接产出 .app，
# 不再手工组装 bundle 或生成 Info.plist。图标仍从 Windows 壳的 .ico 转出注入。
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONFIG="${CONFIG:-Release}"
RID="${RID:-osx-arm64}"
OUT="${OUT:-$ROOT/artifacts}"
APP="$OUT/OpenClawDebugger.app"
PROJECT="$ROOT/src/OpenClawDebugger.Shell.Mac/OpenClawDebugger.Shell.Mac.csproj"
ICON_SRC="$ROOT/src/OpenClawDebugger.Shell.Windows/Assets/OpenClawDebugger.ico"

INSTALL=0
for arg in "$@"; do
  case "$arg" in
    --install) INSTALL=1 ;;
    *) echo "未知参数：$arg" >&2; exit 2 ;;
  esac
done

# 允许未把 dotnet 写进 PATH 的机器直接用用户级安装。
if ! command -v dotnet >/dev/null 2>&1; then
  if [ -x "$HOME/.dotnet/dotnet" ]; then
    export PATH="$HOME/.dotnet:$PATH"
  else
    echo "找不到 dotnet，请先安装 .NET 10 SDK。" >&2
    exit 1
  fi
fi
export DOTNET_CLI_TELEMETRY_OPTOUT=1

# net10.0-macos 需要完整 Xcode（不只是 CommandLineTools）。
# 若系统默认指向 CommandLineTools，则自动切换到 Xcode.app。
if ! xcode-select -p >/dev/null 2>&1 || xcode-select -p 2>/dev/null | grep -q CommandLineTools; then
  if [ -d /Applications/Xcode.app/Contents/Developer ]; then
    export DEVELOPER_DIR="/Applications/Xcode.app/Contents/Developer"
  fi
fi

echo "==> 构建 $RID ($CONFIG)"
dotnet build "$PROJECT" -c "$CONFIG" -r "$RID" --nologo

# net10.0-macos 的 .app 直接产在 bin 下。
BIN_APP="$ROOT/src/OpenClawDebugger.Shell.Mac/bin/$CONFIG/net10.0-macos/$RID/OpenClawDebugger.app"
if [ ! -d "$BIN_APP" ]; then
  echo "找不到构建产物：$BIN_APP" >&2
  exit 1
fi

echo "==> 生成图标"
ICON_TMP="$(mktemp -d)"
trap 'rm -rf "$ICON_TMP"' EXIT
if [ -f "$ICON_SRC" ]; then
  python3 - "$ICON_SRC" "$ICON_TMP/base.png" <<'PY'
import struct, sys
src, dst = sys.argv[1], sys.argv[2]
data = open(src, 'rb').read()
_, _, count = struct.unpack('<HHH', data[:6])
best = None
for i in range(count):
    off = 6 + i * 16
    w, _, _, _, _, _, size, offset = struct.unpack('<BBBBHHII', data[off:off + 16])
    w = w or 256
    if best is None or w > best[0]:
        best = (w, offset, size)
open(dst, 'wb').write(data[best[1]:best[1] + best[2]])
PY
  mkdir -p "$ICON_TMP/AppIcon.iconset"
  for size in 16 32 128 256 512; do
    sips -z "$size" "$size" -s format png "$ICON_TMP/base.png" \
      --out "$ICON_TMP/AppIcon.iconset/icon_${size}x${size}.png" >/dev/null
    sips -z "$((size * 2))" "$((size * 2))" -s format png "$ICON_TMP/base.png" \
      --out "$ICON_TMP/AppIcon.iconset/icon_${size}x${size}@2x.png" >/dev/null
  done
  iconutil -c icns "$ICON_TMP/AppIcon.iconset" -o "$ICON_TMP/AppIcon.icns"
fi

echo "==> 组装产物"
rm -rf "$APP"
# 用 ditto 保留 SDK 生成的签名与元数据。
ditto "$BIN_APP" "$APP"
if [ -f "$ICON_TMP/AppIcon.icns" ]; then
  cp "$ICON_TMP/AppIcon.icns" "$APP/Contents/Resources/AppIcon.icns"
  # The SDK does not know about this post-build icon; declare it in the final
  # bundle metadata so Finder and Dock can resolve the resource.
  /usr/libexec/PlistBuddy -c "Delete :CFBundleIconFile" "$APP/Contents/Info.plist" 2>/dev/null || true
  /usr/libexec/PlistBuddy -c "Add :CFBundleIconFile string AppIcon.icns" "$APP/Contents/Info.plist"
  /usr/libexec/PlistBuddy -c "Delete :CFBundleIconName" "$APP/Contents/Info.plist" 2>/dev/null || true
  /usr/libexec/PlistBuddy -c "Delete :CFBundleIconFiles" "$APP/Contents/Info.plist" 2>/dev/null || true
  /usr/libexec/PlistBuddy -c "Add :CFBundleIconFiles array" "$APP/Contents/Info.plist"
  /usr/libexec/PlistBuddy -c "Add :CFBundleIconFiles:0 string AppIcon.icns" "$APP/Contents/Info.plist"
fi
rm -rf "$ICON_TMP"

# 本机运行用 ad-hoc 签名即可；对外分发需要 Developer ID + notarytool 公证。
# 注意顺序：必须先逐个签 native dylib，再签主可执行，最后签整个 app。
# 直接用 codesign --deep 会破坏 dylib 与 bundle 的签名一致性，导致 dyld 报 Code Signature Invalid。
echo "==> ad-hoc 签名"
for dylib in "$APP/Contents/MonoBundle/"*.dylib; do
  codesign --force --sign - "$dylib"
done
codesign --force --sign - "$APP/Contents/MacOS/OpenClawDebugger"
codesign --force --sign - "$APP"
codesign --verify --verbose=1 "$APP" 2>&1 | tail -1

if [ "$INSTALL" = "1" ]; then
  echo "==> 安装到 /Applications"
  rm -rf "/Applications/OpenClawDebugger.app"
  # 必须用 ditto：cp -R 会丢掉 _CodeSignature 相关元数据，导致 ad-hoc 签名失效。
  ditto "$APP" "/Applications/OpenClawDebugger.app"
  codesign --verify "/Applications/OpenClawDebugger.app" || {
    echo "安装后签名校验失败" >&2
    exit 1
  }
  echo "完成：/Applications/OpenClawDebugger.app"
else
  echo "完成：$APP"
fi
