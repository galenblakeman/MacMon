#!/usr/bin/env python3
"""Configure only explicitly enabled release bundles; development builds remain offline."""
import base64
import os
import pathlib
import plistlib
import re
import sys


def configure(bundle, runtime, public_key, repository):
    if runtime not in ("osx-arm64", "osx-x64"):
        raise ValueError("Unsupported update architecture")
    if len(base64.b64decode(public_key, validate=True)) != 32:
        raise ValueError("SPARKLE_PUBLIC_ED_KEY must be a base64-encoded 32-byte public key")
    if not re.fullmatch(r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+", repository):
        raise ValueError("Expected owner/repository")
    path = pathlib.Path(bundle) / "Contents/Info.plist"
    with path.open("rb") as stream:
        info = plistlib.load(stream)
    info.update(SUFeedURL=f"https://github.com/{repository}/releases/latest/download/appcast-{runtime}.xml",
                SUPublicEDKey=public_key, SUEnableAutomaticChecks=True, SUAutomaticallyUpdate=False,
                SUVerifyUpdateBeforeExtraction=True, SUSendProfileInfo=False)
    with path.open("wb") as stream:
        plistlib.dump(info, stream)


if __name__ == "__main__":
    configure(sys.argv[1], sys.argv[2], os.environ["SPARKLE_PUBLIC_ED_KEY"],
              os.environ.get("MACMON_UPDATE_REPOSITORY", "galenblakeman/MacMon"))
