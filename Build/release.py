"""Release metadata and immutable asset promotion. Standard library only."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import time
import urllib.request

ROOT = Path(__file__).resolve().parents[1]


def gh(*args):
    return subprocess.check_output(['gh', *args], text=True).strip()


def digest(path):
    with open(path, 'rb') as source:
        return hashlib.file_digest(source, 'sha256').hexdigest()


def check_assets(files, release):
    existing = {a['name']: a for a in release['assets']}
    for path in files:
        if path.name in existing:
            if existing[path.name].get('digest') != 'sha256:' + digest(path):
                raise RuntimeError('Existing asset differs: ' + path.name)
        elif not release['draft']:
            raise RuntimeError('Cannot add assets to a published release')
    return existing


def promote(tag, repo):
    payload = ROOT / 'Artifacts/release'
    release = json.loads(gh('api', f'repos/{repo}/releases/tags/{tag}'))
    files = sorted(p for p in payload.iterdir() if p.is_file())
    if not files or not (payload / 'PitMedic-Setup-x64.exe').exists():
        raise RuntimeError('Verified installer is missing')
    existing = check_assets(files, release)
    for path in files:
        if path.name not in existing:
            gh('release', 'upload', tag, str(path), '--repo', repo)
    notes = ROOT / 'Artifacts/signed-release-notes.txt'
    notes.write_text(f'PitMedic {tag[1:]} — verified signed Windows release.\n\n'
                     + (ROOT / 'Build/release-notes.md').read_text()
                     + '\nApp, helpers and installer signed, timestamped and verified. SHA-256 manifests included.\n',
                     encoding='utf-8')
    if release['draft']:
        gh('release', 'edit', tag, '--repo', repo, '--title', f'PitMedic {tag[1:]}',
           '--notes-file', str(notes), '--draft=false', '--prerelease=false')


def prepare(tag, repo, installer):
    manifest_path = ROOT / 'website/update.json'
    old = json.loads(manifest_path.read_text())['latestVersion']
    new = tag[1:]
    if tuple(map(int, new.split('.'))) < tuple(map(int, old.split('.'))):
        raise RuntimeError('Refusing updater downgrade')
    index = ROOT / 'website/index.html'
    index.write_text(index.read_text().replace(old, new), encoding='utf-8', newline='\n')
    generator = ROOT / 'Tools/DiagnosticLibrary/generate.py'
    generator.write_text(re.sub(r'v\d+\.\d+\.\d+\.\d+/PitMedic-Setup-x64.exe',
                               tag + '/PitMedic-Setup-x64.exe', generator.read_text()),
                         encoding='utf-8', newline='\n')
    subprocess.run(['python', str(generator)], check=True, cwd=ROOT)
    manifest = dict(schemaVersion=1, latestVersion=new, title=f'PitMedic {new} signed release',
                    downloadUrl=f'https://github.com/{repo}/releases/download/{tag}/PitMedic-Setup-x64.exe',
                    releaseUrl=f'https://github.com/{repo}/releases/tag/{tag}', sha256=digest(installer))
    manifest_path.write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8', newline='\n')


def verify():
    expected = json.loads((ROOT / 'website/update.json').read_text())
    for attempt in range(12):
        try:
            query = f'?release-check={time.time_ns()}'
            with urllib.request.urlopen('https://pitmedic.com/update.json' + query, timeout=30) as r:
                actual = json.load(r)
            with urllib.request.urlopen('https://pitmedic.com/' + query, timeout=30) as r:
                home = r.read().decode()
            if actual != expected or f'Download v{expected["latestVersion"]}' not in home or expected['downloadUrl'] not in home:
                raise RuntimeError('Live website/updater does not match deployment')
            break
        except Exception:
            if attempt == 11:
                raise
            time.sleep(10)
    h = hashlib.sha256()
    with urllib.request.urlopen(expected['downloadUrl'], timeout=60) as r:
        for block in iter(lambda: r.read(1024 * 1024), b''):
            h.update(block)
    if h.hexdigest() != expected['sha256']:
        raise RuntimeError('Downloaded installer checksum differs')
    summary = f'Verified {expected["latestVersion"]}: homepage, updater, download and SHA-256 all match.\n'
    print(summary)
    if os.environ.get('GITHUB_STEP_SUMMARY'):
        with open(os.environ['GITHUB_STEP_SUMMARY'], 'a') as output:
            output.write(summary)


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('action', choices=['promote', 'prepare', 'verify'])
    parser.add_argument('--tag')
    parser.add_argument('--repo', default=os.environ.get('GITHUB_REPOSITORY'))
    parser.add_argument('--installer', type=Path)
    args = parser.parse_args()
    if args.action != 'verify' and not re.fullmatch(r'v\d+\.\d+\.\d+\.\d+', args.tag or ''):
        parser.error('An exact vX.X.X.X tag is required')
    if args.action == 'promote':
        promote(args.tag, args.repo)
    elif args.action == 'prepare':
        prepare(args.tag, args.repo, args.installer)
    else:
        verify()
