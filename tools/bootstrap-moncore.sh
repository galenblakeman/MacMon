#!/bin/bash
set -euo pipefail

macmon_root="$(cd "$(dirname "$0")/.." && pwd)"
winmon_root="${1:-$(dirname "$macmon_root")/WinMon}"
expected_commit=395a1a29ec83917d60beb380465c7e16500fcd6f
package_version=3.4.1-macos.1

if [[ ! -d "$winmon_root/.git" ]]; then
    git clone https://github.com/galenblakeman/WinMon.git "$winmon_root"
fi
git -C "$winmon_root" cat-file -e "$expected_commit^{commit}"
mkdir -p "$macmon_root/artifacts/local-feed"
build_root="$(mktemp -d "$macmon_root/artifacts/moncore-build.XXXXXX")"
trap 'rm -rf "$build_root"' EXIT

# Build a pinned base plus the handoff patch without altering the shared checkout's work in progress.
git clone --shared --no-checkout "$winmon_root" "$build_root/source"
git -C "$build_root/source" checkout --detach "$expected_commit"
git -C "$build_root/source" apply --check "$macmon_root/dependencies/moncore/portable-process-identity.patch"
git -C "$build_root/source" apply "$macmon_root/dependencies/moncore/portable-process-identity.patch"

# Resolve MacMon's SDK and normalize compiler paths so temporary checkouts do not change package content.
cd "$macmon_root"
dotnet test "$build_root/source/tests/MonCore.Tests/MonCore.Tests.csproj" -c Release \
    -m:1 -p:UseSharedCompilation=false -p:PathMap="$build_root/source=/_/WinMon"
dotnet pack "$build_root/source/src/MonCore/MonCore.csproj" -c Release \
    -m:1 -p:UseSharedCompilation=false -p:PathMap="$build_root/source=/_/WinMon" \
    -p:RepositoryCommit="$expected_commit" -o "$macmon_root/artifacts/local-feed"
python3 tools/normalize-local-package.py "$macmon_root/artifacts/local-feed/MonCore.$package_version.nupkg"
echo "MonCore $package_version verified and packaged from $expected_commit plus the portable identity patch"
