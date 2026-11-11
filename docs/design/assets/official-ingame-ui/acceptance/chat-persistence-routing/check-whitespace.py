"""Check new lines against the saved baseline, preserving pre-existing whitespace."""
import difflib
import hashlib
import json
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[6]
HERE = Path(__file__).parent
subprocess.run(['git', '-c', 'core.whitespace=cr-at-eol', 'diff', '--check'], cwd=ROOT, check=True)
baseline = json.loads((HERE / 'source-before.json').read_text('utf-8'))
errors = []
changed_files = 0
for entry in baseline:
    path = ROOT / entry['path']
    if hashlib.sha256(path.read_bytes()).hexdigest() == entry['sha256']:
        continue
    changed_files += 1
    old = (HERE / 'source-before' / entry['path']).read_text('utf-8-sig').splitlines()
    new = path.read_text('utf-8-sig').splitlines()
    for operation, _, _, start, end in difflib.SequenceMatcher(None, old, new, autojunk=False).get_opcodes():
        if operation not in ('insert', 'replace'):
            continue
        for index in range(start, end):
            if new[index].rstrip() != new[index]:
                errors.append(str(path) + ':' + str(index + 1))
files = [ROOT / 'docs/tasks/official-ingame-ui/README.md', ROOT / 'docs/tasks/official-ingame-ui/tasks.json']
files += [ROOT / p for p in ['tests/game-ui/OfficialUiChatPersistenceTests.cs']]
files += [p for p in HERE.iterdir() if p.is_file() and p.suffix in ('.py', '.md', '.json')]
for path in files:
    for number, line in enumerate(path.read_text('utf-8-sig').splitlines(), 1):
        if line.rstrip() != line:
            errors.append(str(path) + ':' + str(number))
if errors:
    raise SystemExit('Introduced trailing whitespace: ' + repr(errors))
print('Tracked git diff, new lines in ' + str(changed_files) + ' modified sources, and ' + str(len(files)) + ' current documents/helpers have no trailing whitespace.')
