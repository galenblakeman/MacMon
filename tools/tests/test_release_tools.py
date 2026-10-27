import os
import pathlib
import shutil
import subprocess
import sys
import tempfile
import unittest
import zipfile


@unittest.skipUnless(sys.platform == "darwin", "Release archive orchestration uses macOS ditto")
class ReleaseWorkflowTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="macmon-release-test-")
        self.root = pathlib.Path(self.temporary.name)
        source = pathlib.Path(__file__).resolve().parents[2]
        (self.root / "tools").mkdir()
        for name in ("package-app.sh", "package-release.sh", "sign-app.py"):
            shutil.copy(source / "tools" / name, self.root / "tools" / name)
        (self.root / "tools" / "verify.sh").write_text('#!/bin/bash\necho "verify.sh" >> "$MACMON_TEST_LOG"\n')
        (self.root / "dependencies" / "winmon-ui").mkdir(parents=True)
        shutil.copy(source / "dependencies" / "winmon-ui" / "LICENSE", self.root / "dependencies" / "winmon-ui")
        (self.root / "src/MacMon/Assets").mkdir(parents=True)
        shutil.copy(source / "src/MacMon/Assets/MacMon.icns", self.root / "src/MacMon/Assets/MacMon.icns")
        (self.root / "packaging").mkdir()
        shutil.copy(source / "packaging" / "MacMon.entitlements", self.root / "packaging")
        (self.root / "artifacts" / "local-feed").mkdir(parents=True)
        (self.root / "artifacts" / "local-feed" / "MonCore.3.4.1-macos.1.nupkg").touch()
        self.bin = self.root / "mock-bin"
        self.bin.mkdir()
        self.log = self.root / "commands.log"
        self.environment = dict(os.environ, PATH=str(self.bin) + os.pathsep + os.environ["PATH"],
                                MACMON_TEST_LOG=str(self.log), PYTHONDONTWRITEBYTECODE="1", MACMON_ENABLE_UPDATES="0",
                                MACMON_RELEASE_TAG="", MACMON_PACKAGE_ROOT="")
        self.script("security", 'echo \'1) AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA "Developer ID Application: Test (TEST)"\'')
        self.script("uname", 'echo arm64')
        self.script("git", 'if [[ "$1" == "rev-parse" ]]; then echo aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa; fi')
        self.script("dotnet", '''
if [[ "$1" == "msbuild" ]]; then
  if [[ "$*" == *MacMonBundleBuild* ]]; then echo 2; else echo 0.2.0; fi
elif [[ "$1" == "publish" ]]; then
  while [[ $# -gt 0 ]]; do
    if [[ "$1" == "-o" ]]; then shift; mkdir -p "$1"; printf '#!/bin/bash\\nexit 0\\n' > "$1/MacMon"; chmod +x "$1/MacMon"; break; fi
    shift
  done
fi''')
        self.script("codesign", "exit 0")
        self.script("spctl", "exit 0")
        self.script("ditto", 'exec /usr/bin/ditto "$@"')
        self.script("xcrun", '''
if [[ "$1" == "notarytool" ]]; then
  echo "{\\"status\\":\\"${MACMON_TEST_NOTARY_STATUS:-Accepted}\\",\\"id\\":\\"test-id\\"}"
elif [[ "$1" == "stapler" && "$2" == "staple" ]]; then
  echo ticket > "${@: -1}/Contents/stapled-ticket"
fi''')

    def tearDown(self):
        self.temporary.cleanup()

    def script(self, name, body):
        path = self.bin / name
        path.write_text('#!/bin/bash\nset -e\necho "' + name + ' $*" >> "$MACMON_TEST_LOG"\n' + body + '\n')
        path.chmod(0o755)

    def run_release(self, status="Accepted"):
        environment = dict(self.environment, MACMON_TEST_NOTARY_STATUS=status)
        return subprocess.run(["bash", "tools/package-release.sh", "osx-arm64", "Developer ID Application: Test (TEST)", "test-profile"],
                              cwd=self.root, env=environment, capture_output=True, text=True)

    def test_release_archive_is_rebuilt_after_stapling_and_assessment(self):
        result = self.run_release()
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        destination = self.root / "artifacts/releases/0.2.0/osx-arm64"
        archive = destination / "MacMon-0.2.0-osx-arm64.zip"
        with zipfile.ZipFile(archive) as package:
            self.assertIn("MacMon.app/Contents/stapled-ticket", package.namelist())
        self.assertTrue(pathlib.Path(str(archive) + ".sha256").is_file())
        commands = self.log.read_text().splitlines()
        submit = next(i for i, command in enumerate(commands) if "notarytool submit" in command)
        staple = next(i for i, command in enumerate(commands) if "stapler staple" in command)
        assess = next(i for i, command in enumerate(commands) if command.startswith("spctl "))
        final_archive = max(i for i, command in enumerate(commands) if command.startswith("ditto -c"))
        self.assertLess(submit, staple)
        self.assertLess(staple, assess)
        self.assertLess(assess, final_archive)
        signs = [command for command in commands if command.startswith("codesign --force")]
        self.assertTrue(all("--options runtime" in command and "--timestamp" in command for command in signs))
        self.assertTrue(all("--deep" not in command for command in signs))

    def test_non_developer_id_certificate_is_rejected_before_build_or_submission(self):
        self.script("security", 'echo \'1) AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA "Apple Distribution: Test (TEST)"\'')
        result = self.run_release()
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("No valid Developer ID Application identity", result.stderr)
        self.assertNotIn("dotnet publish", self.log.read_text())
        self.assertNotIn("notarytool submit", self.log.read_text())

    def test_rejected_notarization_preserves_logs_without_publishing_release(self):
        result = self.run_release("Invalid")
        self.assertNotEqual(result.returncode, 0)
        self.assertFalse((self.root / "artifacts/releases/0.2.0/osx-arm64").exists())
        self.assertEqual(len(list((self.root / "artifacts/releases/0.2.0").glob(".release-*/notarization.json"))), 1)
        self.assertNotIn("stapler staple", self.log.read_text())
        self.assertNotIn("spctl ", self.log.read_text())


if __name__ == "__main__":
    unittest.main()
