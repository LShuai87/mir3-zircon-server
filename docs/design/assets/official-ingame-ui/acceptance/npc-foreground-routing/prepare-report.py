"""Bind native foreground regression to source transitions and unchanged runtimes."""
import hashlib
import json
import re
from pathlib import Path
HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[5]
FINAL = Path('E:/workspace/mir3-ui-work/npc-foreground-routing-20261108-v2')
PREVIOUS = HERE.parent / 'npc-quest-open' / 'verification.json'
def load(p): return json.loads(p.read_text('utf-8-sig'))
def sha(p): return hashlib.sha256(p.read_bytes()).hexdigest()
def portable(p):
    try: return p.relative_to(ROOT).as_posix()
    except ValueError: return p.as_posix()
def write(p, obj): p.write_text(json.dumps(obj, ensure_ascii=False, indent=2)+'\n', 'utf-8')
tasks = load(ROOT/'docs/tasks/official-ingame-ui/tasks.json')
previous = load(PREVIOUS)
history = {}
for name in [tasks['validationReport'], *tasks['supplementalImplementationReports']]:
    if ROOT/name != HERE/'verification.json':
        history.update({s['path']: s['sha256'] for s in load(ROOT/name)['sourceHashes']})
baseline = load(HERE/'source-before.json')
changes = []
for entry in baseline:
    path=entry['path']; before=entry['sha256']; after=sha(ROOT/path)
    if history.get(path)!=before or sha(HERE/'source-before'/path)!=before:
        raise ValueError('Baseline/history mismatch: '+path)
    if before!=after: changes.append(dict(path=path,beforeSha256=before,afterSha256=after))
commands, failed = [], []
for p in sorted(HERE.glob('*.receipt.json')):
    receipt=load(p); log=Path(receipt['log'])
    item=dict(command=receipt['command'],result='pass' if receipt['exitCode']==0 else 'fail',
        exitCode=receipt['exitCode'],log=portable(log),logSha256=sha(log))
    (commands if receipt['exitCode']==0 else failed).append(item)
(HERE/'tested-runtime.json').write_bytes((FINAL/'tested-runtime.json').read_bytes())
text=(HERE/'npc-foreground-v2.log').read_text('utf-8',errors='replace')
match=re.search(r'(\d+) checks; (\d+) actual scenes; (\d+) coordinate hits; (\d+) presented frames; (\d+) resets; (\d+) identical card pixels',text)
if not match: raise ValueError('Final NPC summary missing')
values=list(map(int,match.groups()))
rows={}
for style in ['official','official-reentry']:
    lines=(FINAL/(style+'-quest-open-layouts.txt')).read_text('utf-8-sig').splitlines()
    counts={name:sum(line.startswith(prefix) for line in lines) for name,prefix in
        [('defaultLayoutRows','quest-open/'),('foregroundSwitches','foreground/'),('capturedDrags','drag/')]}
    if counts!={'defaultLayoutRows':210,'foregroundSwitches':30,'capturedDrags':12}:
        raise ValueError('Incomplete foreground/drag matrix: '+repr(counts))
    rows[style]=counts
for config,entry in previous['builds'].items():
    directory=ROOT.parent/('Debug' if config=='debug' else 'Release')/'Client'
    if sha(directory/entry['client']['path'])!=entry['client']['sha256']:
        raise ValueError('Unchanged runtime assumption failed: '+config)
report=dict(schemaVersion=1,kind='native-npc-foreground-switch-and-captured-drag-regression',
    checkedAtLocalDate='2026-11-08',referenceResolution=[1024,768],releaseAccepted=False,
    results=dict(npcChecks=values[0],npcScenes=values[1],npcCoordinateHits=values[2],npcFrames=values[3],
        npcResets=values[4],identicalNativeNpcPixels=values[5],layoutAndInputRows=rows,
        orderedBusinessTraces='Legacy/Official/reentry identical'),
    sourceChanges=changes,sourceHashes=[dict(path=e['path'],sha256=sha(ROOT/e['path'])) for e in baseline],
    commands=commands,failedAttempts=failed,builds=previous['builds'],
    buildProvenance=dict(reusedFrom=portable(PREVIOUS),reason='Test-only change; production source and Debug/Release binaries unchanged. NPC runner compiled current test sources against the recorded Debug runtime.'),
    package=previous['package'],candidate=previous['candidate'],sourcePlanSha256=tasks['sourcePlanSha256'],
    limitations=[
        'Manual body overlap is allowed by the immutable plan, line 289. Native foreground order is preserved; no automatic relocation or added production handlers.',
        '60 coordinate title switches and 24 captured drags cover None/BuySell, 1024x768 and 1920x1080 plus return to 1024x768, and two Official scene entries.',
        'Each switch begins in default reflow; a real quest visibility cycle restores all close-button access. No guarantee that all underlying close buttons stay simultaneously exposed after arbitrary manual stacking.',
        'Synthetic socket-free replies only. All17window lifecycles, live server transactions/storage, final character semantics/visual acceptance, target hardware, fonts, IME, device-loss and release/rollback gates remain open.',
        'Legacy remains default; RuntimeReady=false, productionBindingAllowed=false, releaseAccepted=false. No commit, push or deployment; official installation remains read-only.'])
external=[Path('E:/workspace/mir3-ui-work/npc-foreground-routing-20261108-v'+str(n)) for n in [1,2]]
paths={p for d in external for p in d.rglob('*') if p.is_file()}
paths.update(p for p in HERE.rglob('*') if p.is_file() and p!=HERE/'verification.json')
report['evidenceHashes']=[dict(path=portable(p),sha256=sha(p)) for p in sorted(paths)]
write(HERE/'verification.json',report)
print(json.dumps(dict(sourceChanges=len(changes),successfulCommands=len(commands),failedAttempts=len(failed),
    evidenceFiles=len(paths),npcChecks=values[0],rows=rows),indent=2))
