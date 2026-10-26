#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")/.."
runtime="${1:-osx-arm64}"
case "$runtime" in
    osx-arm64|osx-x64) ;;
    *) echo "Expected osx-arm64 or osx-x64." >&2; exit 1 ;;
esac
destination_bundle="artifacts/$runtime/MacMon.app"
mkdir -p "artifacts/$runtime"
stage_root="$(mktemp -d "artifacts/$runtime/.macmon-package.XXXXXX")"
trap 'rm -rf "$stage_root"' EXIT
# Publish into a fresh directory: reused output can retain a newer-dated DLL from an older local package.
bundle="$stage_root/MacMon.app"
mkdir -p "$bundle/Contents/MacOS"
dotnet publish src/MacMon/MacMon.csproj -c Release -r "$runtime" --self-contained true \
    -m:1 -p:UseSharedCompilation=false -o "$bundle/Contents/MacOS"
cat > "$bundle/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>CFBundleName</key><string>MacMon</string>
  <key>CFBundleDisplayName</key><string>MacMon</string>
  <key>CFBundleIdentifier</key><string>com.galenblakeman.macmon</string>
  <key>CFBundleExecutable</key><string>MacMon</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>0.1.0</string>
  <key>CFBundleVersion</key><string>1</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>LSMinimumSystemVersion</key><string>13.0</string>
</dict></plist>
PLIST
codesign --force --deep --sign - "$bundle"
host_arch="$(uname -m)"
if [[ "$runtime" == "osx-arm64" && "$host_arch" == "arm64" ]] || \
   [[ "$runtime" == "osx-x64" && "$host_arch" == "x86_64" ]]; then
    "$bundle/Contents/MacOS/MacMon" --smoke
else
    echo "Native bundle smoke skipped: $runtime differs from host $host_arch."
fi
rm -rf "$destination_bundle"
mv "$bundle" "$destination_bundle"
echo "Local development app: $PWD/$destination_bundle"
