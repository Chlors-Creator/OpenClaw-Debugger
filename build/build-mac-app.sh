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
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONFIG="${CONFIG:-Release}"
RID="${RID:-osx-arm64}"
OUT="${OUT:-$ROOT/artifacts}"
APP="$OUT/OpenClawDebugger.app"
PROJECT="$ROOT/src/OpenClawDebugger.Shell.Mac/OpenClawDebugger.Shell.Mac.csproj"
ICON_SRC="$ROOT/src/OpenClawDebugger.Shell.Windows/Assets/OpenClawDebugger.ico"
BUNDLE_ID="com.openclaw.debugger"
VERSION="1.0.0"

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

echo "==> 发布 $RID ($CONFIG)"
PUBLISH_TMP="$(mktemp -d)"
trap 'rm -rf "$PUBLISH_TMP"' EXIT
dotnet publish "$PROJECT" -c "$CONFIG" -r "$RID" --self-contained true -o "$PUBLISH_TMP" --nologo

echo "==> 生成图标"
ICON_TMP="$(mktemp -d)"
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

echo "==> 组装 bundle"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp -R "$PUBLISH_TMP"/. "$APP/Contents/MacOS/"
if [ -f "$ICON_TMP/AppIcon.icns" ]; then
  cp "$ICON_TMP/AppIcon.icns" "$APP/Contents/Resources/AppIcon.icns"
fi
rm -rf "$ICON_TMP"

cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
	<key>CFBundleName</key><string>OpenClaw Debugger</string>
	<key>CFBundleDisplayName</key><string>OpenClaw Debugger</string>
	<key>CFBundleIdentifier</key><string>$BUNDLE_ID</string>
	<key>CFBundleVersion</key><string>$VERSION</string>
	<key>CFBundleShortVersionString</key><string>$VERSION</string>
	<key>CFBundlePackageType</key><string>APPL</string>
	<key>CFBundleExecutable</key><string>OpenClawDebugger</string>
	<key>CFBundleIconFile</key><string>AppIcon</string>
	<key>CFBundleInfoDictionaryVersion</key><string>6.0</string>
	<key>LSMinimumSystemVersion</key><string>11.0</string>
	<key>NSHighResolutionCapable</key><true/>
	<key>LSApplicationCategoryType</key><string>public.app-category.developer-tools</string>
	<key>NSPrincipalClass</key><string>NSApplication</string>
</dict>
</plist>
PLIST

# 本机运行用 ad-hoc 签名即可；对外分发需要 Developer ID + notarytool 公证。
echo "==> ad-hoc 签名"
codesign --force --deep --sign - "$APP"
codesign --verify --verbose=1 "$APP" 2>&1 | tail -1

if [ "$INSTALL" = "1" ]; then
  echo "==> 安装到 /Applications"
  rm -rf "/Applications/OpenClawDebugger.app"
  cp -R "$APP" "/Applications/"
  echo "完成：/Applications/OpenClawDebugger.app"
else
  echo "完成：$APP"
fi
