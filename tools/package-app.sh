#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")/.."
runtime="${1:-osx-arm64}"
if [[ $# -gt 0 ]]; then shift; fi
identity="-"
if [[ $# -gt 0 ]]; then
    if [[ $# -ne 2 || "$1" != "--identity" ]]; then echo "Expected optional --identity CERTIFICATE." >&2; exit 1; fi
    identity="$2"
fi
python3 tools/sign-app.py --check-identity "$identity"
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
    -m:1 -p:UseSharedCompilation=false -p:DebugType=None -p:DebugSymbols=false -o "$bundle/Contents/MacOS"
app_version="$(dotnet msbuild src/MacMon/MacMon.csproj -getProperty:Version -nologo)"
bundle_build="$(dotnet msbuild src/MacMon/MacMon.csproj -getProperty:MacMonBundleBuild -nologo)"
python3 - "$bundle/Contents/Info.plist" "$app_version" "$bundle_build" <<'PY'
import plistlib, sys
with open(sys.argv[1], "wb") as stream:
    plistlib.dump(dict(CFBundleName="MacMon", CFBundleDisplayName="MacMon",
        CFBundleIdentifier="com.galenblakeman.macmon", CFBundleExecutable="MacMon",
        CFBundlePackageType="APPL", CFBundleShortVersionString=sys.argv[2],
        CFBundleVersion=sys.argv[3], NSHighResolutionCapable=True, LSMinimumSystemVersion="13.0"), stream)
PY
python3 tools/sign-app.py "$bundle" "$identity" packaging/MacMon.entitlements
host_arch="$(uname -m)"
if [[ "$runtime" == "osx-arm64" && "$host_arch" == "arm64" ]] || \
   [[ "$runtime" == "osx-x64" && "$host_arch" == "x86_64" ]]; then
    "$bundle/Contents/MacOS/MacMon" --smoke
else
    echo "Native bundle smoke skipped: $runtime differs from host $host_arch."
fi
archive_kind="dev"
if [[ "$identity" != "-" ]]; then archive_kind="signed"; fi
archive="MacMon-$app_version-$runtime-$archive_kind.zip"
ditto -c -k --sequesterRsrc --keepParent "$bundle" "$stage_root/$archive"
(cd "$stage_root" && shasum -a 256 "$archive" > "$archive.sha256")
rm -rf "$destination_bundle"
mv "$bundle" "$destination_bundle"
mv "$stage_root/$archive" "artifacts/$runtime/$archive"
mv "$stage_root/$archive.sha256" "artifacts/$runtime/$archive.sha256"
echo "Packaged app: $PWD/$destination_bundle (identity: $identity)"
echo "Archive: $PWD/artifacts/$runtime/$archive"
