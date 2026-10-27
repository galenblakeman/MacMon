#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")/.."
output="$1"
runtime="${2:-}"
arch="$(uname -m)"
if [[ "$runtime" == "osx-x64" ]]; then arch="x86_64"; fi
if [[ "$runtime" == "osx-arm64" ]]; then arch="arm64"; fi
mkdir -p "$output"
source_path="src/MacMon.Native/DesktopIntegration.swift"
update_path="src/MacMon.Native/UpdateIntegration.swift"
library="$output/libMacMonIntegration.dylib"
framework_flags=(-Xlinker -install_name -Xlinker @rpath/libMacMonIntegration.dylib)
mode="development"
if [[ "${MACMON_ENABLE_UPDATES:-0}" == "1" ]]; then
    sparkle="$PWD/artifacts/sparkle/2.10.1"
    if [[ ! -d "$sparkle/Sparkle.framework" ]]; then echo "Run bash tools/bootstrap-sparkle.sh first." >&2; exit 1; fi
    framework_flags+=(-D MACMON_UPDATES -F "$sparkle" -framework Sparkle -Xlinker -rpath -Xlinker @loader_path/../Frameworks)
    mode="sparkle-2.10.1"
fi
if [[ ! -f "$library" || "$source_path" -nt "$library" || "$update_path" -nt "$library" || "$0" -nt "$library" || "$(cat "$library.mode" 2>/dev/null || true)" != "$mode-$arch" ]]; then
    swiftc -swift-version 5 -O -emit-library -module-name MacMonIntegration \
        -target "$arch-apple-macos13.0" "$source_path" "$update_path" -o "$library" \
        "${framework_flags[@]}"
    echo "$mode-$arch" > "$library.mode"
fi
