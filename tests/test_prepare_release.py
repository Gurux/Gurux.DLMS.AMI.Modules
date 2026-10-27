import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
import unittest

SCRIPT = Path(__file__).resolve().parents[1] / 'scripts' / 'prepare_release.py'


class PrepareReleaseTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.git('init', '-b', 'main')
        self.git('config', 'user.email', 'test@example.com')
        self.git('config', 'user.name', 'Test')
        self.published = []
        (self.root / 'SMTP').mkdir()
        (self.root / 'SMTP/module.csproj').write_text('<Project/>')
        (self.root / 'modules.json').write_text(json.dumps({'modules': {
            'Gurux.AMI.SMTP': {'project': 'SMTP/module.csproj',
                               'changelogPaths': ['SMTP', 'Directory.Build.targets']}}}))
        self.commit('SMTP/file.txt', 'feat: initial SMTP')

    def git(self, *args):
        return subprocess.check_output(['git', *args], cwd=self.root, text=True, encoding='utf-8').strip()

    def commit(self, file, message):
        path = self.root / file
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(message, encoding='utf-8')
        self.git('add', '.')
        self.git('commit', '-m', message)

    def prepare(self, version):
        tag = 'Gurux.AMI.SMTP/v' + version
        self.git('tag', tag)
        spec = importlib.util.spec_from_file_location('prepare_release', SCRIPT)
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        return module.prepare_release(self.root, tag, self.published)

    def test_first_release_only_contains_module_and_shared_commits(self):
        self.commit('Other/file', 'feat: unrelated')
        self.commit('Directory.Build.targets', 'build: shared')
        data, notes = self.prepare('1.0.0')
        self.assertEqual(data['version'], '1.0.0')
        self.assertFalse(data['prerelease'])
        self.assertEqual(data['package'], 'Gurux.AMI.SMTP-1.0.0.zip')
        self.assertIn('feat: initial SMTP', notes)
        self.assertIn('build: shared', notes)
        self.assertNotIn('unrelated', notes)

    def test_stable_baseline_spans_rc_and_ignores_other_modules(self):
        self.git('tag', 'Gurux.AMI.SMTP/v1.0.0')
        self.published.append('Gurux.AMI.SMTP/v1.0.0')
        self.commit('SMTP/file.txt', 'fix(security): authenticate')
        self.git('tag', 'Gurux.AMI.SMTP/v2.0.0-rc1')
        self.published.append('Gurux.AMI.SMTP/v2.0.0-rc1')
        self.commit('Other/file', 'feat: other release')
        self.git('tag', 'Other/v99.0.0')
        self.commit('SMTP/file.txt', 'fix: final SMTP')
        data, notes = self.prepare('2.0.0')
        self.assertEqual(data['previousTag'], 'Gurux.AMI.SMTP/v1.0.0')
        self.assertIn('fix(security): authenticate', notes)
        self.assertNotIn('initial SMTP', notes)
        self.assertNotIn('other release', notes)

    def test_rc_uses_previous_rc_and_subjects_are_literal(self):
        self.git('tag', 'Gurux.AMI.SMTP/v1.0.0-rc1')
        self.published.append('Gurux.AMI.SMTP/v1.0.0-rc1')
        self.commit('SMTP/file.txt', 'fix: $(secret) `command` ääkköset')
        data, notes = self.prepare('1.0.0-rc2')
        self.assertTrue(data['prerelease'])
        self.assertEqual(data['previousTag'], 'Gurux.AMI.SMTP/v1.0.0-rc1')
        self.assertIn('$(secret) `command` ääkköset', notes)

    def test_rejects_legacy_unknown_and_malformed_tags(self):
        spec = importlib.util.spec_from_file_location('prepare_release', SCRIPT)
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        for tag in ('v1.0.0', 'Unknown/v1.0.0', 'Gurux.AMI.SMTP/vbad', 'Gurux.AMI.SMTP/v1.0.0;echo'):
            with self.subTest(tag=tag), self.assertRaises(ValueError):
                module.prepare_release(self.root, tag, self.published)

    def test_unpublished_tags_do_not_truncate_changes(self):
        self.git('tag', 'Gurux.AMI.SMTP/v1.0.0')
        self.published.append('Gurux.AMI.SMTP/v1.0.0')
        self.commit('SMTP/file.txt', 'fix: must be included')
        self.git('tag', 'Gurux.AMI.SMTP/v1.1.0')
        self.commit('SMTP/file.txt', 'fix: newest')
        data, notes = self.prepare('1.2.0')
        self.assertEqual(data['previousTag'], 'Gurux.AMI.SMTP/v1.0.0')
        self.assertIn('fix: must be included', notes)


if __name__ == '__main__':
    unittest.main()
