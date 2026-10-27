#!/usr/bin/env python3
"""Validate both architectures, upload a draft, then expose the complete stable release."""
import hashlib
import json
import os
import pathlib
import plistlib
import re
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET
import zipfile

SPARKLE = "{http://www.andymatuschak.org/xml-namespaces/sparkle}"
RUNTIMES = ("osx-arm64", "osx-x64")


def digest_file(path):
    digest = hashlib.sha256()
    with pathlib.Path(path).open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def validate(directory, version, commit, repository, public_key):
    files, builds = [], []
    for runtime in RUNTIMES:
        folder = pathlib.Path(directory) / runtime
        metadata = json.loads((folder / "release.json").read_text())
        if metadata.get("version") != version or metadata.get("commit") != commit or metadata.get("runtime") != runtime or metadata.get("notarized") is not True:
            raise ValueError("Release manifests must match the tag, commit and architecture")
        build = metadata["build"]
        if type(build) is not int or build < 1:
            raise ValueError("Expected a positive bundle build")
        builds.append(build)
        archive = folder / f"MacMon-{version}-{runtime}.zip"
        checksum = pathlib.Path(str(archive) + ".sha256")
        digest = digest_file(archive)
        if checksum.read_text().split() != [digest, archive.name]:
            raise ValueError("Release archive checksum mismatch")
        with zipfile.ZipFile(archive) as package:
            info = plistlib.loads(package.read("MacMon.app/Contents/Info.plist"))
        feed_url = f"https://github.com/{repository}/releases/latest/download/appcast-{runtime}.xml"
        if (info.get("CFBundleVersion"), info.get("CFBundleShortVersionString"), info.get("SUPublicEDKey"), info.get("SUFeedURL")) != (str(build), version, public_key, feed_url):
            raise ValueError("Bundle version, public key or feed does not match release configuration")
        feed = folder / f"appcast-{runtime}.xml"
        item = ET.parse(feed).getroot().find("channel/item")
        enclosure = item.find("enclosure")
        if (item.findtext(SPARKLE + "version"), item.findtext(SPARKLE + "shortVersionString"),
            enclosure.get("url"), enclosure.get("length")) != (
                str(build), version, f"https://github.com/{repository}/releases/download/v{version}/{archive.name}", str(archive.stat().st_size)):
            raise ValueError("Appcast metadata does not match archive")
        subprocess.run(["swift", str(pathlib.Path(__file__).with_name("verify-update.swift")),
                        public_key, enclosure.get(SPARKLE + "edSignature", ""), str(archive)], check=True)
        # GitHub assets share one namespace; give each architecture's manifest a unique name.
        manifest = folder / f"release-{runtime}.json"
        manifest.write_text(json.dumps(metadata, indent=2) + "\n")
        files.extend([archive, checksum, feed, manifest])
    if len(set(builds)) != 1:
        raise ValueError("Architecture builds must match")
    return files, builds[0]


def gh(*args):
    return subprocess.check_output(["gh", *args], text=True)


def ensure_newer(version, build, previous_feeds):
    for path in previous_feeds:
        item = ET.parse(path).getroot().find("channel/item")
        previous_version = item.findtext(SPARKLE + "shortVersionString")
        if build <= int(item.findtext(SPARKLE + "version")) or tuple(map(int, version.split("."))) <= tuple(map(int, previous_version.split("."))):
            raise ValueError("Version and MacMonBundleBuild must both increase over the previous release")


def publish(directory, tag, commit, repository, public_key):
    if not re.fullmatch(r"v\d+\.\d+\.\d+", tag):
        raise ValueError("Only stable vMAJOR.MINOR.PATCH tags can publish updates")
    if not re.fullmatch(r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+", repository) or not re.fullmatch(r"[a-f0-9]{40}", commit):
        raise ValueError("Expected repository and full commit SHA")
    files, build = validate(directory, tag[1:], commit, repository, public_key)
    releases = json.loads(gh("release", "list", "--repo", repository, "--limit", "1000", "--json", "tagName,isDraft,isPrerelease"))
    existing = next((r for r in releases if r["tagName"] == tag), None)
    if existing and not existing["isDraft"]:
        raise ValueError("Published releases are immutable; use a new version tag")
    stable = [r for r in releases if not r["isDraft"] and not r["isPrerelease"] and re.fullmatch(r"v\d+\.\d+\.\d+", r["tagName"])]
    with tempfile.TemporaryDirectory(prefix="macmon-publish-") as temporary:
        if stable:
            previous = max(stable, key=lambda r: tuple(map(int, r["tagName"][1:].split("."))))
            if tuple(map(int, tag[1:].split("."))) <= tuple(map(int, previous["tagName"][1:].split("."))):
                raise ValueError("Cannot publish an older or equal version as latest")
            assets = json.loads(gh("release", "view", previous["tagName"], "--repo", repository, "--json", "assets"))["assets"]
            feeds = [a["name"] for a in assets if a["name"] in [f"appcast-{r}.xml" for r in RUNTIMES]]
            if feeds:
                if len(feeds) != 2:
                    raise ValueError("Previous release has incomplete update feeds")
                gh("release", "download", previous["tagName"], "--repo", repository, "--pattern", "appcast-*.xml", "--dir", temporary)
                ensure_newer(tag[1:], build, pathlib.Path(temporary).glob("appcast-*.xml"))
        if not existing:
            gh("release", "create", tag, "--repo", repository, "--draft", "--verify-tag", "--target", commit,
               "--title", f"MacMon {tag[1:]}", "--generate-notes")
        gh("release", "upload", tag, "--repo", repository, "--clobber", *map(str, files))
        # Check downloaded bytes before making the release visible to Sparkle's latest feed URL.
        downloaded = pathlib.Path(temporary) / "uploaded"
        gh("release", "download", tag, "--repo", repository, "--dir", str(downloaded))
        for path in files:
            if digest_file(path) != digest_file(downloaded / path.name):
                raise ValueError("Uploaded release asset differs from validated bytes")
        gh("release", "edit", tag, "--repo", repository, "--draft=false", "--latest")


if __name__ == "__main__":
    publish(sys.argv[1], os.environ["MACMON_RELEASE_TAG"], os.environ["GITHUB_SHA"],
            os.environ["GITHUB_REPOSITORY"], os.environ["SPARKLE_PUBLIC_ED_KEY"])
