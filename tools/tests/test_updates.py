import base64
import hashlib
import importlib.util
import json
import pathlib
import plistlib
import shutil
import subprocess
import sys
import tempfile
import unittest
import xml.etree.ElementTree as ET
from unittest.mock import patch
import zipfile

ROOT = pathlib.Path(__file__).resolve().parents[2]


def module(name):
    spec = importlib.util.spec_from_file_location(name.replace("-", "_"), ROOT / "tools" / (name + ".py"))
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


configure = module("configure-updates")
appcast = module("create-appcast")
publisher = module("publish-release")
REPOSITORY = "galenblakeman/MacMon"
COMMIT = "a" * 40
# Public RFC 8032 Ed25519 test vector; these are never used for production releases.
SEED = base64.b64encode(bytes.fromhex("9d61b19deffd5a60ba844af492ec2cc44449c5697b326919703bac031cae7f60")).decode()
PUBLIC = base64.b64encode(bytes.fromhex("d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a")).decode()
SIGNATURE = base64.b64encode(b"s" * 64).decode()


class UpdateTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="macmon-update-tests-")
        self.root = pathlib.Path(self.temporary.name)
        self.key = self.root / "test.key"
        self.key.write_text(SEED)

    def tearDown(self):
        self.temporary.cleanup()

    def fixture(self, runtime="osx-arm64", real_signing=False):
        folder = self.root / runtime
        bundle = folder / "MacMon.app"
        (bundle / "Contents").mkdir(parents=True)
        info = bundle / "Contents/Info.plist"
        info.write_bytes(plistlib.dumps(dict(CFBundleVersion="42", CFBundleShortVersionString="0.14.0")))
        configure.configure(bundle, runtime, PUBLIC, REPOSITORY)
        archive = folder / f"MacMon-0.14.0-{runtime}.zip"
        with zipfile.ZipFile(archive, "w") as package:
            package.write(info, "MacMon.app/Contents/Info.plist")
        pathlib.Path(str(archive) + ".sha256").write_text(hashlib.sha256(archive.read_bytes()).hexdigest() + "  " + archive.name + "\n")
        (folder / "release.json").write_text(json.dumps(dict(version="0.14.0", runtime=runtime, commit=COMMIT, build=42, notarized=True)))
        if real_signing:
            appcast.create(folder, REPOSITORY, PUBLIC, self.key)
        else:
            with patch.object(appcast.subprocess, "check_output", return_value=SIGNATURE), patch.object(appcast.subprocess, "run"):
                appcast.create(folder, REPOSITORY, PUBLIC, self.key)
        return folder

    def test_bundle_and_feed_use_same_architecture_public_key_build_and_immutable_archive_url(self):
        for runtime in publisher.RUNTIMES:
            folder = self.fixture(runtime)
            info = plistlib.loads((folder / "MacMon.app/Contents/Info.plist").read_bytes())
            self.assertTrue(info["SUEnableAutomaticChecks"])
            self.assertFalse(info["SUAutomaticallyUpdate"])
            self.assertTrue(info["SUVerifyUpdateBeforeExtraction"])
            self.assertIn(f"appcast-{runtime}.xml", info["SUFeedURL"])
        with patch.object(publisher.subprocess, "run"):
            files, build = publisher.validate(self.root, "0.14.0", COMMIT, REPOSITORY, PUBLIC)
        self.assertEqual(build, 42)
        self.assertEqual(len(files), 8)
        self.assertEqual(len({file.name for file in files}), 8)

    def test_invalid_public_keys_and_repositories_are_rejected(self):
        for key, repository in [("not-base64", REPOSITORY), (base64.b64encode(b"short").decode(), REPOSITORY), (PUBLIC, "owner/repo/extra")]:
            with self.assertRaises(ValueError):
                configure.configure(self.root, "osx-arm64", key, repository)

    def test_archive_tampering_and_cross_architecture_feed_are_rejected(self):
        arm = self.fixture()
        intel = self.fixture("osx-x64")
        target = intel / "appcast-osx-x64.xml"
        original = target.read_bytes()
        target.write_bytes((arm / "appcast-osx-arm64.xml").read_bytes())
        with patch.object(publisher.subprocess, "run"), self.assertRaisesRegex(ValueError, "Appcast"):
            publisher.validate(self.root, "0.14.0", COMMIT, REPOSITORY, PUBLIC)
        target.write_bytes(original)
        with (arm / "MacMon-0.14.0-osx-arm64.zip").open("ab") as stream:
            stream.write(b"changed")
        with self.assertRaisesRegex(ValueError, "checksum"):
            publisher.validate(self.root, "0.14.0", COMMIT, REPOSITORY, PUBLIC)

    def test_both_build_and_version_must_increase(self):
        folder = self.fixture()
        feeds = [folder / "appcast-osx-arm64.xml"]
        for version, build in [("0.14.0", 43), ("0.15.0", 42), ("0.13.0", 99)]:
            with self.assertRaisesRegex(ValueError, "both increase"):
                publisher.ensure_newer(version, build, feeds)
        publisher.ensure_newer("0.15.0", 43, feeds)

    def test_manifest_commit_and_bundled_key_must_match(self):
        self.fixture()
        self.fixture("osx-x64")
        with self.assertRaisesRegex(ValueError, "manifests"):
            publisher.validate(self.root, "0.14.0", "b" * 40, REPOSITORY, PUBLIC)
        with self.assertRaisesRegex(ValueError, "public key"):
            publisher.validate(self.root, "0.14.0", COMMIT, REPOSITORY, base64.b64encode(b"x" * 32).decode())

    def test_failed_upload_leaves_draft_and_never_changes_latest(self):
        calls = []

        def gh(*args):
            calls.append(args)
            if args[:2] == ("release", "list"):
                return "[]"
            if args[:2] == ("release", "upload"):
                raise subprocess.CalledProcessError(1, "gh release upload")
            return ""

        with patch.object(publisher, "validate", return_value=([self.key], 42)), patch.object(publisher, "gh", side_effect=gh):
            with self.assertRaises(subprocess.CalledProcessError):
                publisher.publish(self.root, "v0.14.0", COMMIT, REPOSITORY, PUBLIC)
        self.assertIn("--draft", calls[1])
        self.assertFalse(any(call[:2] == ("release", "edit") for call in calls))

    def test_published_releases_cannot_be_overwritten(self):
        with patch.object(publisher, "validate", return_value=([], 42)), patch.object(publisher, "gh", return_value=json.dumps([
            dict(tagName="v0.14.0", isDraft=False, isPrerelease=False)])) as command:
            with self.assertRaisesRegex(ValueError, "immutable"):
                publisher.publish(self.root, "v0.14.0", COMMIT, REPOSITORY, PUBLIC)
            self.assertEqual(command.call_count, 1)

    def test_success_publishes_only_after_all_uploaded_bytes_are_downloaded_and_checked(self):
        payload = self.root / "payload.zip"
        payload.write_bytes(b"verified release bytes")
        calls = []

        def gh(*args):
            calls.append(args)
            if args[:2] == ("release", "list"):
                return "[]"
            if args[:2] == ("release", "download"):
                destination = pathlib.Path(args[args.index("--dir") + 1])
                destination.mkdir()
                shutil.copy(payload, destination)
            return ""

        with patch.object(publisher, "validate", return_value=([payload], 42)), patch.object(publisher, "gh", side_effect=gh):
            publisher.publish(self.root, "v0.14.0", COMMIT, REPOSITORY, PUBLIC)
        self.assertEqual([call[1] for call in calls], ["list", "create", "upload", "download", "edit"])
        self.assertIn("--draft=false", calls[-1])
        self.assertIn("--latest", calls[-1])

    def test_corrupted_uploaded_asset_never_becomes_latest(self):
        payload = self.root / "payload.zip"
        payload.write_bytes(b"verified release bytes")
        calls = []

        def gh(*args):
            calls.append(args)
            if args[:2] == ("release", "list"):
                return "[]"
            if args[:2] == ("release", "download"):
                destination = pathlib.Path(args[args.index("--dir") + 1])
                destination.mkdir()
                (destination / payload.name).write_bytes(b"wrong bytes")
            return ""

        with patch.object(publisher, "validate", return_value=([payload], 42)), patch.object(publisher, "gh", side_effect=gh):
            with self.assertRaisesRegex(ValueError, "Uploaded release asset"):
                publisher.publish(self.root, "v0.14.0", COMMIT, REPOSITORY, PUBLIC)
        self.assertFalse(any(call[1] == "edit" for call in calls))

    @unittest.skipUnless(sys.platform == "darwin" and (ROOT / "artifacts/sparkle/2.10.1/bin/sign_update").exists(), "Requires bootstrapped Sparkle on macOS")
    def test_real_sparkle_signature_verifies_and_wrong_public_key_fails(self):
        folder = self.fixture(real_signing=True)
        archive = folder / "MacMon-0.14.0-osx-arm64.zip"
        item = ET.parse(folder / "appcast-osx-arm64.xml").getroot().find("channel/item/enclosure")
        signature = item.get(publisher.SPARKLE + "edSignature")
        result = subprocess.run(["swift", str(ROOT / "tools/verify-update.swift"),
                                 base64.b64encode(b"x" * 32).decode(), signature, str(archive)], capture_output=True)
        self.assertNotEqual(result.returncode, 0)
        self.assertIn(b"does not match", result.stderr)


if __name__ == "__main__":
    unittest.main()
