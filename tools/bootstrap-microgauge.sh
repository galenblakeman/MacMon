#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")/.."
macmon_root="$PWD"
expected_commit=710092272d5be4e27536b296ec7d52dae569d54c
package_version=1.1.0.1-macmon.1
build_root="$(mktemp -d /tmp/macmon-microgauge.XXXXXX)"
build_root="$(cd "$build_root" && pwd -P)"
trap 'rm -rf "$build_root"' EXIT
git clone --quiet --no-checkout https://github.com/galenblakeman/MicroGauge.git "$build_root/source"
git -C "$build_root/source" checkout --quiet --detach "$expected_commit"
mkdir -p "$macmon_root/artifacts/local-feed"
dotnet test "$build_root/source/Test/MicroGauge.Avalonia.Tests/MicroGauge.Avalonia.Tests.csproj" -c Release \
    -m:1 -p:UseSharedCompilation=false -p:PathMap="$build_root/source=/_/MicroGauge"
dotnet pack "$build_root/source/Library/MicroGauge.Avalonia/MicroGauge.Avalonia.csproj" -c Release \
    -m:1 -p:UseSharedCompilation=false -p:PathMap="$build_root/source=/_/MicroGauge" \
    -p:Deterministic=true -p:DebugType=None -p:DebugSymbols=false -p:Version="$package_version" -p:PackageVersion="$package_version" -p:RepositoryCommit="$expected_commit" \
    -o "$macmon_root/artifacts/local-feed"
dotnet pack "$build_root/source/Library/MicroGauge/MicroGauge.csproj" -c Release \
    -m:1 -p:UseSharedCompilation=false -p:PathMap="$build_root/source=/_/MicroGauge" \
    -p:Deterministic=true -p:DebugType=None -p:DebugSymbols=false -p:Version="$package_version" -p:PackageVersion="$package_version" -p:RepositoryCommit="$expected_commit" \
    -o "$macmon_root/artifacts/local-feed"
for package in MicroGauge MicroGauge.Avalonia; do
    python3 tools/normalize-local-package.py "$macmon_root/artifacts/local-feed/$package.$package_version.nupkg"
done
echo "MicroGauge.Avalonia $package_version verified and packaged from $expected_commit"
