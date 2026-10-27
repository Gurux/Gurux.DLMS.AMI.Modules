"""Prepare one module release from its tag and path-filtered Git history."""
import argparse
import json
import os
from pathlib import Path
import re
import subprocess

VERSION = r'(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)(?:\.[0-9]+)?(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?(?:\+[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?'


def git(root, *args):
    return subprocess.check_output(['git', *args], cwd=root, text=True, encoding='utf-8').strip()


def prepare_release(root, tag, published_tags):
    root = Path(root).resolve()
    match = re.fullmatch(r'(Gurux\.AMI\.[A-Za-z0-9_.-]+)/v(' + VERSION + ')', tag)
    if not match:
        raise ValueError('Expected tag <Gurux.AMI.Module>/v<version>')
    identifier, version = match.groups()
    modules = json.loads((root / 'modules.json').read_text(encoding='utf-8'))['modules']
    if identifier not in modules:
        raise ValueError(f'Unknown module: {identifier}')
    module = modules[identifier]
    project = module['project']
    paths = module['changelogPaths']
    if not isinstance(paths, list) or not paths:
        raise ValueError('changelogPaths must contain module and shared source paths')
    for path in [project, *paths]:
        if (not isinstance(path, str) or not path or path.startswith(('-', ':'))
                or not (root / path).resolve().is_relative_to(root)):
            raise ValueError('Module paths must stay within the repository')
    if not (root / project).is_file():
        raise ValueError(f'Module project not found: {project}')
    # Only reachable tags can be a baseline. Stable notes include changes shipped in RCs.
    prerelease = '-' in version.split('+')[0]
    candidates = []
    reachable = set(git(root, 'tag', '--merged', tag, '--list', identifier + '/v*').splitlines())
    for candidate in sorted(reachable.intersection(published_tags)):
        suffix = candidate[len(identifier) + 2:]
        if candidate != tag and re.fullmatch(VERSION, suffix):
            if prerelease or '-' not in suffix.split('+')[0]:
                candidates.extend(['--match', candidate])
    previous = git(root, 'describe', '--tags', '--abbrev=0', *candidates, tag) if candidates else None
    revision = f'{previous}..{tag}' if previous else tag
    history = git(root, 'log', '--no-merges', '--format=%h%x09%s', revision, '--', *paths)
    lines = []
    for line in history.splitlines():
        sha, subject = line.split('\t', 1)
        lines.append(f'- {subject} ({sha})')
    notes = f'{identifier} {version}\n\n' + ('\n'.join(lines) or 'No module source changes.') + '\n'
    return {'id': identifier, 'version': version, 'tag': tag, 'project': project,
            'prerelease': prerelease, 'previousTag': previous,
            'package': f'{identifier}-{version}.zip',
            'compatibilityAsset': f'{identifier}-{version}.compatibility.json'}, notes


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--tag', required=True)
    parser.add_argument('--output', default='release')
    args = parser.parse_args()
    repository = os.environ['GITHUB_REPOSITORY']
    published = subprocess.check_output(
        ['gh', 'api', f'repos/{repository}/releases', '--paginate', '--jq',
         '.[] | select(.draft == false) | .tag_name'], text=True, encoding='utf-8').splitlines()
    metadata, notes = prepare_release(Path.cwd(), args.tag, published)
    output = Path(args.output)
    output.mkdir(parents=True, exist_ok=True)
    (output / 'release.json').write_text(json.dumps(metadata, indent=2) + '\n', encoding='utf-8')
    (output / 'notes.md').write_text(notes, encoding='utf-8')


if __name__ == '__main__':
    main()
