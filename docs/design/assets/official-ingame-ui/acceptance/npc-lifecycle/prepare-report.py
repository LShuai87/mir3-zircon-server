"""Bind native NPC lifecycle fixes to source, runtime and regression evidence."""
import hashlib, json, re
from pathlib import Path
HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[5]
PREVIOUS = ROOT/'docs/design/assets/official-ingame-ui/acceptance/npc-foreground-routing/verification.json'
FINAL = Path('E:/workspace/mir3-ui-work/npc-lifecycle-fixed-20261108-v1')
SCENE = Path('E:/workspace/mir3-ui-work/npc-lifecycle-full-scene-20261108-v1')
def load(p): return json.loads(p.read_text('utf-8-sig'))
def sha(p): return hashlib.sha256(p.read_bytes()).hexdigest()
def portable(p):
    try: return p.relative_to(ROOT).as_posix()
    except ValueError: return p.as_posix()
def write(p, obj): p.write_text(json.dumps(obj, ensure_ascii=False, indent=2)+'\n', 'utf-8')
tasks=load(ROOT/'docs/tasks/official-ingame-ui/tasks.json')
previous=load(PREVIOUS)
history={}
for name in [tasks['validationReport'], *tasks['supplementalImplementationReports']]:
    if ROOT/name != HERE/'verification.json':
        history.update({s['path']:s['sha256'] for s in load(ROOT/name)['sourceHashes']})
baseline=load(HERE/'source-before.json')
changes=[]
for entry in baseline:
    rel=entry['path']; before=entry['sha256']; after=sha(ROOT/rel)
    if history.get(rel)!=before or sha(HERE/'source-before'/rel)!=before:
        raise ValueError('Source baseline/history mismatch: '+rel)
    if before!=after: changes.append(dict(path=rel,beforeSha256=before,afterSha256=after))
commands, failed=[], []
for p in sorted(HERE.glob('*.receipt.json')):
    receipt=load(p); log=Path(receipt['log'])
    item=dict(command=receipt['command'],result='pass' if receipt['exitCode']==0 else 'fail',
        exitCode=receipt['exitCode'],log=portable(log),logSha256=sha(log))
    (commands if receipt['exitCode']==0 else failed).append(item)
expected={
    'npc-baseline-v2.log':'AccessoryReset/leave-option',
    'npc-baseline-v3.log':'WeaponCraft/close-button'}
for name, message in expected.items():
    text=(HERE/name).read_text('utf-8',errors='replace')
    if 'hidden service releases both link directions without locking: '+message not in text:
        raise ValueError('Expected baseline failure missing: '+name)
text=(HERE/'npc-fixed-v1.log').read_text('utf-8',errors='replace')
match=re.search(r'(\d+) checks; (\d+) actual scenes; (\d+) coordinate hits; (\d+) presented frames; (\d+) resets; (\d+) identical card pixels',text)
if not match: raise ValueError('Final NPC pass summary missing')
values=list(map(int,match.groups()))
assertions=(FINAL/'assertions.txt').read_text('utf-8-sig').splitlines()
matrix={}
for style in ['legacy','official','official-reentry']:
    rows=(FINAL/(style+'-lifecycle.txt')).read_text('utf-8-sig').splitlines()
    routes={r.split('|')[1] for r in rows}
    if len(rows)!=75 or len({r.split('|')[0] for r in rows})!=15 or len(routes)!=5:
        raise ValueError('Incomplete lifecycle matrix: '+style)
    linked=[r for r in rows if 'links=0|' not in r]
    repeats=[r for r in assertions if r.startswith(style+'|repeated close leaves no orphan NPC service:')]
    disposed=[r for r in assertions if r.startswith(style+'|scene exit disposes all seventeen')]
    if len(linked)!=10 or len(repeats)!=100 or len(disposed)!=1:
        raise ValueError('Incomplete selection/repeat/disposal checks: '+style)
    matrix[style]=dict(responseTypes=15,closeRoutes=sorted(routes),matrixRows=75,linkedSelectionCases=10,repeatedCoordinateCloses=100,sceneDisposals=1)
(HERE/'tested-runtime.json').write_bytes((FINAL/'tested-runtime.json').read_bytes())
for entry in load(HERE/'tested-runtime.json'):
    if sha(ROOT.parent/'Debug/Client'/entry['name'])!=entry['sha256']:
        raise ValueError('Tested runtime mismatch: '+entry['name'])
builds={}
for config in ['debug','release']:
    manifest=load(HERE/('release-manifest-'+config+'.json'))
    directory=ROOT.parent/('Debug' if config=='debug' else 'Release')/'Client'
    if sha(directory/manifest['client']['path'])!=manifest['client']['sha256']:
        raise ValueError('Manifest/runtime mismatch: '+config)
    builds[config]=dict(buildId=manifest['buildId'],client=manifest['client'])
report=dict(schemaVersion=1,kind='native-npc-lifecycle-selection-cleanup-and-scene-disposal-verification',
    checkedAtLocalDate='2026-11-08',referenceResolution=[1024,768],releaseAccepted=False,
    results=dict(npcChecks=values[0],npcScenes=values[1],npcCoordinateHits=values[2],npcFrames=values[3],
        npcResets=values[4],identicalNativeNpcPixels=values[5],lifecycleMatrix=matrix,
        orderedBusinessTraces='Legacy/Official/reentry identical',
        windowSetSummary=(HERE/'window-set.log').read_text('utf-8',errors='replace').strip().splitlines()[-1],
        fullSceneSummary=(HERE/'full-scene.log').read_text('utf-8',errors='replace').strip().splitlines()[-1]),
    fixes=[
        dict(window='NPCAccessoryResetDialog',rootCause='Linked target lacked hide cleanup; item association could survive main-card leave.',change='Clear original AccessoryGrid links on effective hide.'),
        dict(window='NPCWeaponCraftWindow',rootCause='Seven linked grids lacked hide cleanup; associations could survive main-card close.',change='Clear original template and six material grid links on effective hide.')],
    sourceChanges=changes,sourceHashes=[dict(path=e['path'],sha256=sha(ROOT/e['path'])) for e in baseline],
    commands=commands,failedAttempts=failed,
    expectedRegressionFailures=expected,
    authoringCorrection='npc-baseline.log records an initial missing local test variable compilation error; corrected before the two runtime reproductions.',
    regressionBaselineRuntime=dict(reusedFrom=portable(PREVIOUS),builds=previous['builds'],
        testedRuntime=load(PREVIOUS.parent/'tested-runtime.json')),
    builds=builds,package=previous['package'],candidate=previous['candidate'],sourcePlanSha256=tasks['sourcePlanSha256'],
    limitations=[
        'All 17 native NPC window identities are captured and checked after scene disposal. Active link cleanup probes specifically seed accessory reset and all seven craft grids; other service interactions retain their earlier suite coverage.',
        '225 type/route cases and 300 repeated coordinate closes across Legacy/Official/reentry use synthetic socket-free server replies. This is not live server transaction acceptance.',
        'ObjectRemove is decoded through the actual connection handler and removes a real fixture NPC map object. Page replacement retains the independently visible quest until the main card subsequently closes.',
        'Decoded TimedOut disconnect retains the native return-to-login modal and active NPC until scene exit. Tests then dispose GameScene directly with live selection; clicking the modal into LoginScene, socket teardown, and real reconnect are not covered.',
        'No service skins, sizes, slots, inventory capacities, request payloads or server behavior changed. Both Legacy and Official share the two cleanup fixes.',
        'Visibility preference persistence, live storage/services, character/equipment semantics, final visuals, target hardware, fonts, IME and release/rollback gates remain open.',
        'Legacy remains default; RuntimeReady=false, productionBindingAllowed=false, releaseAccepted=false. No commit, push or deployment; official installation remains read-only.'])
external=[FINAL,SCENE,Path('E:/workspace/mir3-ui-work/npc-lifecycle-baseline-20261108-v2'),Path('E:/workspace/mir3-ui-work/npc-lifecycle-baseline-20261108-v3')]
paths={p for directory in external for p in directory.rglob('*') if p.is_file()}
paths.update(p for p in HERE.rglob('*') if p.is_file() and p!=HERE/'verification.json')
report['evidenceHashes']=[dict(path=portable(p),sha256=sha(p)) for p in sorted(paths)]
write(HERE/'verification.json',report)
print(json.dumps(dict(sourceChanges=len(changes),successfulCommands=len(commands),failedAttempts=len(failed),evidenceFiles=len(paths),npcChecks=values[0],matrix=matrix),indent=2))
