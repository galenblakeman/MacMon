#!/usr/bin/env python3
"""Sign nested Mach-O code before the app bundle; --deep is used for verification only."""
import pathlib
import re
import subprocess
import sys


def validate_identity(identity):
    if identity == "-":
        return
    identities = subprocess.check_output(["security", "find-identity", "-v", "-p", "codesigning"], text=True)
    for digest, name in re.findall(r'([0-9A-Fa-f]{40})\s+"([^"]+)"', identities):
        if name.startswith("Developer ID Application:") and (identity == name or identity.lower() == digest.lower()):
            return
    raise SystemExit("No valid Developer ID Application identity matches the requested certificate. Configure a certificate with its private key first.")


def sign(bundle, identity, entitlements):
    bundle = pathlib.Path(bundle).resolve()
    validate_identity(identity)
    flags = ["--force", "--sign", identity]
    flags += ["--timestamp=none"] if identity == "-" else ["--timestamp", "--options", "runtime"]
    magic = {bytes.fromhex(value) for value in ("cffaedfe", "feedfacf", "cefaedfe", "feedface", "cafebabe", "bebafeca", "cafebabf", "bfbafeca")}
    main = bundle / "Contents" / "MacOS" / "MacMon"
    nested = []
    for path in bundle.rglob("*"):
        if path.is_symlink() or not path.is_file() or path == main:
            continue
        with path.open("rb") as stream:
            header = stream.read(4)
            if header in magic:
                nested.append(path)
    for path in sorted(nested, key=lambda item: len(item.parts), reverse=True):
        subprocess.run(["codesign", *flags, str(path)], check=True)
    containers = [path for path in bundle.rglob("*") if path.is_dir() and not path.is_symlink() and path.suffix in (".app", ".framework", ".xpc")]
    for path in sorted(containers, key=lambda item: len(item.parts), reverse=True):
        subprocess.run(["codesign", *flags, str(path)], check=True)
    subprocess.run(["codesign", *flags, "--entitlements", str(entitlements), str(bundle)], check=True)
    subprocess.run(["codesign", "--verify", "--deep", "--strict", "--verbose=2", str(bundle)], check=True)


if __name__ == "__main__":
    if len(sys.argv) == 3 and sys.argv[1] == "--check-identity":
        validate_identity(sys.argv[2])
    elif len(sys.argv) == 4:
        sign(*sys.argv[1:])
    else:
        raise SystemExit("Usage: sign-app.py APP IDENTITY ENTITLEMENTS | --check-identity IDENTITY")
