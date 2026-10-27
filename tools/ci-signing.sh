#!/bin/bash
# Only used on disposable GitHub-hosted macOS runners. Never enable shell tracing here.
set -euo pipefail
: "${RUNNER_TEMP:?}" "${APPLE_CERTIFICATE_P12_BASE64:?}" "${APPLE_CERTIFICATE_PASSWORD:?}"
: "${APPLE_NOTARY_KEY_ID:?}" "${APPLE_NOTARY_ISSUER_ID:?}" "${APPLE_NOTARY_PRIVATE_KEY:?}"
: "${SPARKLE_PRIVATE_ED_KEY:?}"
umask 077
keychain="$RUNNER_TEMP/macmon-release.keychain-db"
certificate="$RUNNER_TEMP/macmon-certificate.p12"
notary_key="$RUNNER_TEMP/macmon-notary.p8"
export SPARKLE_PRIVATE_KEY_FILE="$RUNNER_TEMP/macmon-sparkle.key"
python3 - "$certificate" "$notary_key" "$SPARKLE_PRIVATE_KEY_FILE" <<'PY'
import base64, os, pathlib, sys
pathlib.Path(sys.argv[1]).write_bytes(base64.b64decode(os.environ['APPLE_CERTIFICATE_P12_BASE64'], validate=True))
pathlib.Path(sys.argv[2]).write_text(os.environ['APPLE_NOTARY_PRIVATE_KEY'])
pathlib.Path(sys.argv[3]).write_text(os.environ['SPARKLE_PRIVATE_ED_KEY'])
PY
password="$(openssl rand -hex 32)"
security create-keychain -p "$password" "$keychain"
security set-keychain-settings -lut 21600 "$keychain"
security unlock-keychain -p "$password" "$keychain"
security import "$certificate" -k "$keychain" -P "$APPLE_CERTIFICATE_PASSWORD" -T /usr/bin/codesign -T /usr/bin/security >/dev/null
security set-key-partition-list -S apple-tool:,apple:,codesign: -s -k "$password" "$keychain" >/dev/null
security list-keychains -d user -s "$keychain" "$HOME/Library/Keychains/login.keychain-db"
security default-keychain -d user -s "$keychain"
xcrun notarytool store-credentials MacMon-CI --key "$notary_key" --key-id "$APPLE_NOTARY_KEY_ID" --issuer "$APPLE_NOTARY_ISSUER_ID" >/dev/null
rm -f "$certificate" "$notary_key"
