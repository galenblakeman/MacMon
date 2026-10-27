#!/usr/bin/env python3
"""Sign the final notarized ZIP and produce an architecture-specific Sparkle feed."""
import base64
import datetime
import email.utils
import json
import os
import pathlib
import re
import subprocess
import sys
import xml.etree.ElementTree as ET

SPARKLE = "http://www.andymatuschak.org/xml-namespaces/sparkle"
ET.register_namespace("sparkle", SPARKLE)


def create(directory, repository, public_key, private_key_file):
    directory = pathlib.Path(directory)
    metadata = json.loads((directory / "release.json").read_text())
    version, runtime, build = (metadata[key] for key in ("version", "runtime", "build"))
    if not re.fullmatch(r"\d+\.\d+\.\d+", version) or runtime not in ("osx-arm64", "osx-x64") or type(build) is not int or build < 1:
        raise ValueError("Expected stable version, supported architecture and positive bundle build")
    if not re.fullmatch(r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+", repository) or metadata.get("notarized") is not True:
        raise ValueError("Expected GitHub repository and notarized release")
    archive = directory / f"MacMon-{version}-{runtime}.zip"
    root = pathlib.Path(__file__).resolve().parents[1]
    signature = subprocess.check_output([str(root / "artifacts/sparkle/2.10.1/bin/sign_update"),
                                         "--ed-key-file", str(private_key_file), "-p", str(archive)], text=True).strip()
    if len(base64.b64decode(signature, validate=True)) != 64:
        raise ValueError("Invalid Ed25519 signature")
    subprocess.run(["swift", str(root / "tools/verify-update.swift"), public_key, signature, str(archive)], check=True)
    rss = ET.Element("rss", version="2.0")
    channel = ET.SubElement(rss, "channel")
    ET.SubElement(channel, "title").text = f"MacMon ({runtime})"
    item = ET.SubElement(channel, "item")
    ET.SubElement(item, "title").text = f"MacMon {version}"
    ET.SubElement(item, "pubDate").text = email.utils.format_datetime(datetime.datetime.now(datetime.timezone.utc))
    ET.SubElement(item, f"{{{SPARKLE}}}version").text = str(build)
    ET.SubElement(item, f"{{{SPARKLE}}}shortVersionString").text = version
    ET.SubElement(item, f"{{{SPARKLE}}}minimumSystemVersion").text = "13.0.0"
    ET.SubElement(item, "description").text = f"MacMon {version}. See https://github.com/{repository}/releases/tag/v{version} for release notes."
    ET.SubElement(item, "enclosure", {"url": f"https://github.com/{repository}/releases/download/v{version}/{archive.name}",
                  f"{{{SPARKLE}}}edSignature": signature, "length": str(archive.stat().st_size), "type": "application/octet-stream"})
    path = directory / f"appcast-{runtime}.xml"
    ET.indent(rss)
    ET.ElementTree(rss).write(path, encoding="utf-8", xml_declaration=True)
    return path


if __name__ == "__main__":
    print(create(sys.argv[1], os.environ.get("MACMON_UPDATE_REPOSITORY", "galenblakeman/MacMon"),
                 os.environ["SPARKLE_PUBLIC_ED_KEY"], os.environ["SPARKLE_PRIVATE_KEY_FILE"]))
