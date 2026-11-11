import subprocess,json,hashlib
from pathlib import Path
ROOT=Path(__file__).resolve().parents[6]
HERE=Path(__file__).parent
subprocess.run(['git','-c','core.whitespace=cr-at-eol','diff','--check'],cwd=ROOT,check=True)
baseline=json.loads((HERE/'source-before.json').read_text('utf-8'))
files=[ROOT/e['path'] for e in baseline if hashlib.sha256((ROOT/e['path']).read_bytes()).hexdigest()!=e['sha256']]
files += [ROOT/'build/game-ui/warehouse-combined-composition-recipe.json',ROOT/'tests/game-ui/test_official_ui_frame_order.py']
files += [p for p in HERE.iterdir() if p.is_file() and p.suffix in ['.py','.md','.json']]
errors=[]
for p in files:
    for number,line in enumerate(p.read_text('utf-8-sig').splitlines(),1):
        if line.rstrip()!=line:errors.append(str(p)+':'+str(number))
if errors:raise SystemExit('Trailing whitespace: '+repr(errors))
print('Tracked git diff and '+str(len(files))+' current source/document files have no trailing whitespace.')
