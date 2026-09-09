"""Executed only after full validation and explicit release dispatch."""
import json
import os
import re
import subprocess
import xml.etree.ElementTree as ET
from pathlib import Path

def gh(*args):
    return subprocess.check_output(['gh', *args], text=True).strip()

version = os.environ['APPROVED_VERSION']
if not re.fullmatch(r'\d+\.\d+\.\d+\.\d+', version):
    raise RuntimeError('Invalid approved version')
for name in ['PitMedic', 'PitMedic.RepairHelper', 'PitMedic.SensorHelper']:
    project = ET.parse(f'Source/{name}/{name}.csproj')
    for field in ['Version', 'AssemblyVersion', 'FileVersion', 'InformationalVersion']:
        if project.findtext('.//' + field) != version:
            raise RuntimeError(f'{name} {field} differs from approved version')
if not Path('Build/release-notes.md').read_text().strip():
    raise RuntimeError('Release notes are missing')
repo = os.environ['GITHUB_REPOSITORY']
sha = os.environ['GITHUB_SHA']
tag = 'v' + version
# Listing succeeds or raises: network/auth failures never mean "not found".
tags = gh('api', '--paginate', f'repos/{repo}/git/matching-refs/tags/{tag}')
matches = [r for r in json.loads(tags) if r['ref'] == 'refs/tags/' + tag]
if matches:
    if matches[0]['object']['sha'] != sha:
        raise RuntimeError('Existing tag belongs to another commit')
else:
    gh('api', '--method', 'POST', f'repos/{repo}/git/refs',
       '-f', 'ref=refs/tags/' + tag, '-f', 'sha=' + sha)
releases = json.loads(gh('api', '--paginate', '--slurp', f'repos/{repo}/releases'))
existing = [r for page in releases for r in page if r['tag_name'] == tag]
if existing and not existing[0]['draft']:
    raise RuntimeError('Already published: rerun only failed publication jobs, never re-sign')
if not existing:
    gh('release', 'create', tag, '--repo', repo, '--verify-tag', '--draft',
       '--title', 'PitMedic ' + version, '--notes-file', 'Build/release-notes.md')
with open(os.environ['GITHUB_OUTPUT'], 'a') as output:
    output.write('tag=' + tag + '\n')
