#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")/.."
version=2.10.1
digest=1a856d2a9edb8e628de88121d7be04cf0e2167f8641650b78fca00b14244cf27
destination="artifacts/sparkle/$version"
if [[ -f "$destination/.verified-$digest" ]]; then exit 0; fi
mkdir -p artifacts/sparkle
stage="$(mktemp -d artifacts/sparkle/.download.XXXXXX)"
trap 'rm -rf "$stage"' EXIT
curl --fail --location --retry 3 "https://github.com/sparkle-project/Sparkle/releases/download/$version/Sparkle-$version.tar.xz" -o "$stage/sparkle.tar.xz"
echo "$digest  $stage/sparkle.tar.xz" | shasum -a 256 --check
mkdir "$stage/distribution"
tar -xJf "$stage/sparkle.tar.xz" -C "$stage/distribution"
touch "$stage/distribution/.verified-$digest"
rm -rf "$destination"
mv "$stage/distribution" "$destination"
echo "Verified Sparkle $version: $destination"
