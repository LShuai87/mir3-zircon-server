"""Bind the bounded quest-layout implementation to exact source and runtime evidence."""
import hashlib
import json
import re
from pathlib import Path
HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[5]
FINAL = Path('E:/workspace/mir3-ui-work/npc-quest-open-20261108-v6')
WINDOW = Path('E:/workspace/mir3-ui-work/npc-quest-open-window-set-20261108-v1')
SCENE = Path('E:/workspace/mir3-ui-work/npc-quest-open-full-scene-20261108-v1')
def load(p): return json.loads(p.read_text('utf-8-sig'))
def sha(p): return hashlib.sha256(p.read_bytes()).hexdigest()
def portable(p):
    try: return p.relative_to(ROOT).as_posix()
    except ValueError: return p.as_posix()
def write(p, obj): p.write_text(json.dumps(obj, ensure_ascii=False, indent=2)+'\n', 'utf-8')
tasks = load(ROOT/'docs/tasks/official-ingame-ui/tasks.json')
history = {}
for name in [tasks['validationReport'], *tasks['supplementalImplementationReports']]:
    if ROOT/name != HERE/'verification.json':
        history.update({s['path']: s['sha256'] for s in load(ROOT/name)['sourceHashes']})
baseline = load(HERE/'source-before.json')
changes = []
for entry in baseline:
    path = entry['path']; before = entry['sha256']; after = sha(ROOT/path)
    if history.get(path) != before or sha(HERE/'source-before'/path) != before:
        raise ValueError('Baseline/history mismatch: '+path)
    if before != after: changes.append(dict(path=path, beforeSha256=before, afterSha256=after))
commands, failed = [], []
for p in sorted(HERE.glob('*.receipt.json')):
    receipt=load(p); log=Path(receipt['log'])
    item=dict(command=receipt['command'], result='pass' if receipt['exitCode']==0 else 'fail',
        exitCode=receipt['exitCode'], log=portable(log), logSha256=sha(log))
    (commands if receipt['exitCode']==0 else failed).append(item)
(HERE/'tested-runtime.json').write_bytes((FINAL/'tested-runtime.json').read_bytes())
text=(HERE/'npc-open-v6.log').read_text('utf-8', errors='replace')
match=re.search(r'(\d+) checks; (\d+) actual scenes; (\d+) coordinate hits; (\d+) presented frames; (\d+) resets; (\d+) identical card pixels', text)
if not match: raise ValueError('Final NPC summary missing')
values=list(map(int, match.groups()))
layouts={}
for style in ['official', 'official-reentry']:
    rows=(FINAL/(style+'-quest-open-layouts.txt')).read_text('utf-8-sig').splitlines()
    if len(rows)!=210: raise ValueError('Incomplete independent quest matrix: '+style)
    buy=[r for r in rows if '/BuySell/' in r]
    if len(buy)!=14 or not all('NPCGoodsDialog=' in r and 'NPCSellDialog=' in r and 'NPCQuestDialog=' in r for r in buy):
        raise ValueError('Buy/sell/quest fixture did not display all three windows')
    layouts[style]=len(rows)
assertions=(FINAL/'assertions.txt').read_text('utf-8-sig').splitlines()
warehouse=[r for r in assertions if 'opening NPC respects active warehouse layout ownership;' in r]
builds={}
for config in ['debug', 'release']:
    manifest=load(HERE/('release-manifest-'+config+'.json'))
    builds[config]=dict(buildId=manifest['buildId'], client=manifest['client'])
report=dict(schemaVersion=1, kind='native-npc-independent-quest-open-and-service-concurrency-verification',
    checkedAtLocalDate='2026-11-08', referenceResolution=[1024,768], releaseAccepted=False,
    results=dict(npcChecks=values[0], npcScenes=values[1], npcCoordinateHits=values[2], npcFrames=values[3],
        npcResets=values[4], identicalNativeNpcPixels=values[5], independentQuestLayoutRows=layouts,
        questBuySellConcurrentRows=28, loadedWarehouseChecks=warehouse,
        windowSetSummary=(HERE/'window-set.log').read_text('utf-8',errors='replace').strip().splitlines()[-1],
        fullSceneSummary=(HERE/'full-scene.log').read_text('utf-8',errors='replace').strip().splitlines()[-1], pythonTests=172),
    sourceChanges=changes, sourceHashes=[dict(path=e['path'],sha256=sha(ROOT/e['path'])) for e in baseline],
    commands=commands, failedAttempts=failed, builds=builds,
    package=dict(path='E:/workspace/mir3-ui-work/warehouse-combined-20261108-v2/zl/OfficialV1.Zl',frameCount=94),
    candidate=dict(path='E:/workspace/mir3-ui-work/warehouse-assets-20261108-v1/zl-v2/OfficialV1.Zl',frameCount=1),
    sourcePlanSha256=tasks['sourcePlanSha256'],
    finalValidation=dict(npc='npc-open-v6.log', debug='build-debug-v4.log', release='build-release.log',
        python='python-tests.log',windowSet='window-set.log',fullScene='full-scene.log'),
    limitations=[
        'OfficialV1 only; native quest/NPC/secondary styles, original controls, sizes, visibility and packets retained. Legacy remains default; RuntimeReady=false, productionBindingAllowed=false, releaseAccepted=false.',
        'Quest is independently packed like storage/inventory. Bodies may overlap when full windows cannot fit; default reflow preserves complete bounds and reachable close strips. Paired service bodies remain disjoint.',
        '420 fixed layouts cover 15 response types, two opening orders, six unique resolutions and return to 1024x768 over two candidate scene entries. 28 rows contain actual visible buy/sell/quest windows.',
        'Synthetic matrix runs under a test-only preference write suppression scope; it neither positions nor raises windows. Production visibility-preference semantics are not newly accepted by this report.',
        'Reentry warehouse assertion compares the actual loaded position before opening with the position after opening; it no longer assumes the original default after earlier user-state fixtures.',
        'Arbitrary foreground switching, all seventeen native window lifecycles and all fonts/paragraph shapes remain pending. Hardware device-loss boundary remains unclosed.',
        'No real server, real service/storage transactions, live IME, target hardware, final visual or release/rollback acceptance. No commit, push or deployment. Official installation remains read-only.'])
for key in ['package','candidate']: report[key]['sha256']=sha(Path(report[key]['path']))
external=[WINDOW,SCENE]+[Path('E:/workspace/mir3-ui-work/npc-quest-open-20261108-v'+str(n)) for n in range(1,7)]
paths={p for d in external for p in d.rglob('*') if p.is_file()}
paths.update(p for p in HERE.rglob('*') if p.is_file() and p!=HERE/'verification.json')
report['evidenceHashes']=[dict(path=portable(p),sha256=sha(p)) for p in sorted(paths)]
write(HERE/'verification.json', report)
print(json.dumps(dict(changedSources=len(changes), successfulCommands=len(commands), failedAttempts=len(failed),
    evidenceFiles=len(paths), npcChecks=values[0], layouts=layouts),ensure_ascii=False,indent=2))
