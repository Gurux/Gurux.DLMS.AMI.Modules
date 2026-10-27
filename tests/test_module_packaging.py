"""Verify shared assembly filtering without an AMI host source tree."""
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest
from xml.sax.saxutils import escape

TARGETS = Path(__file__).resolve().parents[1] / 'Directory.Build.targets'


@unittest.skipUnless(shutil.which('dotnet'), '.NET SDK required')
class ModulePackagingTests(unittest.TestCase):
    def test_filters_shared_contracts_and_stale_satellites_but_keeps_private_files(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            publish = root / 'publish'
            (publish / 'fi').mkdir(parents=True)
            names = ['Example.Module.dll', 'Private.Library.dll', 'Private.Library.xml',
                     'Gurux.DLMS.AMI.Module.dll', 'Gurux.DLMS.AMI.Module.pdb',
                     'Microsoft.Extensions.Options.dll', 'System.Text.Json.dll',
                     'Gurux.UI.Components.xml', 'fi/Gurux.UI.Components.resources.dll', 'module.json',
                     'Microsoft.Private.Dependency.dll', 'System.Private.Dependency.dll']
            for name in names:
                (publish / name).write_text('fixture')
            references = ['Gurux.DLMS.AMI.Module', 'Gurux.UI.Components',
                          'Microsoft.Extensions.Options', 'System.Text.Json', 'Private.Library',
                          'Microsoft.Private.Dependency', 'System.Private.Dependency']
            project = root / 'verify.proj'
            project.write_text('''<Project>
  <PropertyGroup>
    <Configuration>Release</Configuration>
    <PublishDir>''' + escape(publish.as_posix()) + '''/</PublishDir>
  </PropertyGroup>
  <ItemGroup>
''' + ''.join(f'    <ReferencePath Include="refs/{name}.dll"' +
                (' FrameworkReferenceName="Microsoft.NETCore.App"' if name in
                 ('Microsoft.Extensions.Options', 'System.Text.Json') else '') + ' />\n' for name in references) +
                ''.join(f'    <ResolvedFileToPublish Include="publish/{name}" />\n' for name in names) + '''
  </ItemGroup>
  <Target Name="ResolveReferences" />
  <Import Project="''' + escape(TARGETS.as_posix()) + '''" />
  <Target Name="Verify" DependsOnTargets="AmiFilterModulePublishFiles;AmiRemoveStaleHostPublishFiles">
    <WriteLinesToFile File="report.txt" Lines="@(ResolvedFileToPublish)" Overwrite="true" />
  </Target>
</Project>''', encoding='utf-8')
            result = subprocess.run(['dotnet', 'msbuild', str(project), '-t:Verify', '-nologo'],
                                    cwd=root, capture_output=True, text=True)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            kept = (root / 'report.txt').read_text().replace('\\', '/').splitlines()
            expected = ['Example.Module.dll', 'Private.Library.dll', 'Private.Library.xml', 'module.json',
                        'Microsoft.Private.Dependency.dll', 'System.Private.Dependency.dll']
            self.assertCountEqual(kept, ['publish/' + name for name in expected])
            self.assertCountEqual([str(p.relative_to(publish)).replace('\\', '/')
                                   for p in publish.rglob('*') if p.is_file()], expected)
