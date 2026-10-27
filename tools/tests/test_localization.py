import json
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
AUDIT = ROOT / "tools/LocalizationAudit/bin/Release/net10.0/LocalizationAudit.dll"


class LocalizationAuditTests(unittest.TestCase):
    def audit(self, translation, source='UiText.Format("Count {0:F1}", count);'):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            catalog = root / "src/MacMon/Localization"
            catalog.mkdir(parents=True)
            (catalog.parent / "Example.cs").write_text(source)
            (catalog / "en.json").write_text(json.dumps({"Count {0:F1}": "Count {0:F1}"}))
            (catalog / "es.json").write_text(json.dumps(translation))
            return subprocess.run(["dotnet", str(AUDIT), str(root)], text=True, capture_output=True, check=False)

    def test_accepts_translations_and_rejects_missing_keys_and_changed_formats(self):
        self.assertEqual(self.audit({"Count {0:F1}": "Cantidad {0:F1}"}).returncode, 0)
        for translation in ({}, {"Count {0:F1}": "Cantidad {0}"}, {"Count {0:F1}": "Cantidad {1:F1}"}):
            result = self.audit(translation)
            self.assertNotEqual(result.returncode, 0)
            self.assertIn("es.json", result.stderr)

    def test_detects_new_source_keys_and_interpolated_format_specifiers(self):
        result = self.audit({"Count {0:F1}": "Cantidad {0:F1}"}, 'UiText.Interpolate($"New {count:F2}");')
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("New {0:F2}", result.stderr)


if __name__ == "__main__":
    unittest.main()
