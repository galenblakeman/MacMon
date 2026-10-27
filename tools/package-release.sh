#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")/.."
if [[ "${1:-}" == "--help" ]]; then
    echo "Usage: bash tools/package-release.sh osx-arm64|osx-x64 'Developer ID Application: …' KEYCHAIN_PROFILE"
    exit 0
fi
if [[ $# -ne 3 ]]; then echo "Expected runtime, Developer ID Application identity and notarytool keychain profile. Use --help." >&2; exit 1; fi
runtime="$1"
identity="$2"
profile="$3"
if [[ -n "${MACMON_PACKAGE_ROOT:-}" ]]; then echo "Release builds use the default package output directory." >&2; exit 1; fi
case "$runtime" in osx-arm64|osx-x64) ;; *) echo "Unsupported runtime." >&2; exit 1 ;; esac
if [[ "$identity" == "-" ]]; then echo "A release requires a Developer ID Application identity." >&2; exit 1; fi
python3 tools/sign-app.py --check-identity "$identity"
host_arch="$(uname -m)"
if [[ "$runtime" == "osx-arm64" && "$host_arch" != "arm64" ]] || \
   [[ "$runtime" == "osx-x64" && ( "$host_arch" != "x86_64" || "$(sysctl -n hw.optional.arm64 2>/dev/null || true)" == "1" ) ]]; then
    echo "Release verification requires matching native hardware (Intel execution cannot be substituted by Rosetta)." >&2
    exit 1
fi
if [[ -n "$(git status --porcelain)" ]]; then echo "Commit the source changes before preparing a release." >&2; exit 1; fi
bash tools/verify.sh --smoke
bash tools/package-app.sh "$runtime" --identity "$identity"
version="$(dotnet msbuild src/MacMon/MacMon.csproj -getProperty:Version -nologo)"
bundle_build="$(dotnet msbuild src/MacMon/MacMon.csproj -getProperty:MacMonBundleBuild -nologo)"
if [[ -n "${MACMON_RELEASE_TAG:-}" && "$MACMON_RELEASE_TAG" != "v$version" ]]; then
    echo "Release tag must match project version v$version." >&2; exit 1
fi
destination="artifacts/releases/$version/$runtime"
if [[ -e "$destination" ]]; then echo "Release output already exists: $destination" >&2; exit 1; fi
mkdir -p "$(dirname "$destination")"
stage="$(mktemp -d "artifacts/releases/$version/.release-$runtime.XXXXXX")"
# Preserve notarization logs if a submission fails; do not publish a partial release directory.
bundle="$stage/MacMon.app"
ditto "artifacts/$runtime/MacMon.app" "$bundle"
submission="$stage/submission.zip"
ditto -c -k --sequesterRsrc --keepParent "$bundle" "$submission"
xcrun notarytool submit "$submission" --keychain-profile "$profile" --wait --output-format json > "$stage/notarization.json"
python3 - "$stage/notarization.json" <<'PY'
import json, sys
with open(sys.argv[1]) as stream:
    result = json.load(stream)
if result.get("status") != "Accepted":
    raise SystemExit("Notarization was not accepted. Inspect notarization.json and retrieve the notarytool log; no release was produced.")
PY
xcrun stapler staple "$bundle"
xcrun stapler validate "$bundle"
codesign --verify --deep --strict --verbose=2 "$bundle"
spctl --assess --type execute --verbose=4 "$bundle"
archive="MacMon-$version-$runtime.zip"
ditto -c -k --sequesterRsrc --keepParent "$bundle" "$stage/$archive"
(cd "$stage" && shasum -a 256 "$archive" > "$archive.sha256")
python3 - "$stage/release.json" "$version" "$runtime" "$(git rev-parse HEAD)" "$bundle_build" <<'PY'
import json, sys
with open(sys.argv[1], "w") as stream:
    json.dump(dict(version=sys.argv[2], runtime=sys.argv[3], commit=sys.argv[4], build=int(sys.argv[5]), notarized=True), stream, indent=2)
PY
rm "$submission"
mv "$stage" "$destination"
echo "Verified release artifacts: $PWD/$destination"
