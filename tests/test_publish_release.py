"""Exercise release transaction boundaries without contacting GitHub."""
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

SCRIPT = Path(__file__).resolve().parents[1] / 'scripts' / 'publish_release.ps1'


@unittest.skipUnless(shutil.which('pwsh'), 'PowerShell 7 required')
class PublishReleaseTests(unittest.TestCase):
    def run_release(self, state='missing', fail='', package=True, metadata=True):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / 'release').mkdir()
            (root / 'release/release.json').write_text(json.dumps({
                'id': 'Gurux.AMI.SMTP', 'tag': 'Gurux.AMI.SMTP/v1.0.0',
                'version': '1.0.0', 'prerelease': False, 'package': 'smtp.zip',
                'compatibilityAsset': 'smtp.compatibility.json'}))
            (root / 'release/notes.md').write_text('Literal $(command) `text`')
            if package:
                (root / 'release/smtp.zip').write_bytes(b'zip fixture')
            if metadata:
                (root / 'release/smtp.compatibility.json').write_text(json.dumps({
                    'schemaVersion': 1, 'id': 'Gurux.AMI.SMTP', 'version': '1.0.0',
                    'targetFramework': 'net10.0', 'references': [
                        {'name': 'Gurux.DLMS.AMI.Module.dll', 'version': '4.1.2610.2703'}]}))
            stub = root / 'run.ps1'
            stub.write_text('''
$ErrorActionPreference = 'Stop'
function gh {
    $operation = ($args[0..1] -join ' ')
    Add-Content calls.txt "$operation|$env:GH_TOKEN"
    $global:LASTEXITCODE = 0
    if ($operation -eq $env:FAIL_OPERATION) { $global:LASTEXITCODE = 1; return }
    if ($operation -eq 'release view') {
        switch ($env:RELEASE_STATE) {
            'missing' { $global:LASTEXITCODE = 1 }
            'draft' { '{"isDraft":true,"assets":[]}' }
            'published' { '{"isDraft":false,"assets":[{"name":"smtp.zip"},{"name":"smtp.compatibility.json"}]}' }
            'missing-metadata' { '{"isDraft":false,"assets":[{"name":"smtp.zip"}]}' }
            'broken' { '{"isDraft":false,"assets":[]}' }
        }
    }
    if ($operation -eq 'api --method') { $input | Set-Content payload.json }
}
& $env:PUBLISH_SCRIPT
''', encoding='utf-8')
            result = subprocess.run(['pwsh', '-NoProfile', '-File', str(stub)], cwd=root,
                env={**os.environ, 'PUBLISH_SCRIPT': str(SCRIPT), 'RELEASE_STATE': state,
                     'FAIL_OPERATION': fail, 'GH_TOKEN': 'module-token', 'CATALOG_TOKEN': 'catalog-token',
                     'GITHUB_REPOSITORY': 'Gurux/Gurux.DLMS.AMI.Modules'},
                capture_output=True, text=True, encoding='utf-8')
            calls = (root / 'calls.txt').read_text().splitlines()
            payload = json.loads((root / 'payload.json').read_text()) if (root / 'payload.json').exists() else None
            return result, calls, payload

    def test_new_release_uploads_before_publish_and_notifies_last(self):
        result, calls, payload = self.run_release()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(calls, ['release view|module-token', 'release create|module-token',
                                'release upload|module-token', 'release edit|module-token',
                                'api --method|catalog-token'])
        self.assertEqual(payload['event_type'], 'module-release')

    def test_published_release_retry_needs_no_package(self):
        result, calls, _ = self.run_release('published', package=False, metadata=False)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(calls, ['release view|module-token', 'api --method|catalog-token'])

    def test_upload_failure_does_not_publish_or_notify(self):
        result, calls, payload = self.run_release('draft', fail='release upload')
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(calls[-1], 'release upload|module-token')
        self.assertIsNone(payload)

    def test_missing_metadata_never_publishes_or_notifies(self):
        result, calls, payload = self.run_release('draft', metadata=False)
        self.assertNotEqual(result.returncode, 0)
        self.assertFalse(any(call.startswith(('release edit', 'api --method')) for call in calls))
        self.assertIsNone(payload)

    def test_published_release_missing_metadata_is_rejected(self):
        result, calls, payload = self.run_release('missing-metadata', package=False, metadata=False)
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(len(calls), 1)
        self.assertIsNone(payload)

    def test_published_release_with_missing_zip_is_rejected(self):
        result, calls, payload = self.run_release('broken', package=False)
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(len(calls), 1)
        self.assertIsNone(payload)
