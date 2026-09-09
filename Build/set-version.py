"""One command synchronizes the app/helper version fields before the release PR."""
import argparse
from pathlib import Path
import re

parser = argparse.ArgumentParser()
parser.add_argument('version')
args = parser.parse_args()
if not re.fullmatch(r'\d+\.\d+\.\d+\.\d+', args.version):
    parser.error('Expected X.X.X.X')
root = Path(__file__).resolve().parents[1]
for name in ['PitMedic', 'PitMedic.RepairHelper', 'PitMedic.SensorHelper']:
    path = root / f'Source/{name}/{name}.csproj'
    # Preserve exact existing newline style.
    raw = path.read_bytes()
    text = raw.decode('utf-8')
    for field in ['Version', 'AssemblyVersion', 'FileVersion', 'InformationalVersion']:
        text, count = re.subn(f'<{field}>[^<]+</{field}>', f'<{field}>{args.version}</{field}>', text)
        if count != 1:
            raise RuntimeError(f'Expected exactly one {field} in {path}')
    path.write_bytes(text.encode('utf-8'))
print('Synchronized project versions. Update Build/release-notes.md and shipped documentation in the release PR.')
