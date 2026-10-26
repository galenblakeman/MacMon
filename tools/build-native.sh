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
library="$output/libMacMonIntegration.dylib"
if [[ ! -f "$library" || "$source_path" -nt "$library" || "$0" -nt "$library" ]]; then
    swiftc -swift-version 5 -O -emit-library -module-name MacMonIntegration \
        -target "$arch-apple-macos13.0" "$source_path" -o "$library" \
        -Xlinker -install_name -Xlinker @rpath/libMacMonIntegration.dylib
fi
