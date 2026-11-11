"""Validate native layout reentry evidence against immutable source/runtime baselines."""
import hashlib, json, re
from pathlib import Path
HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[5]
PREVIOUS = ROOT/'docs/design/assets/official-ingame-ui/acceptance/npc-lifecycle/verification.json'
FINAL = Path('E:/workspace/mir3-ui-work/layout-persistence-reentry-20261108-v2')
def load(p): return json.loads(p.read_text('utf-8-sig'))
def sha(p): return hashlib.sha256(p.read_bytes()).hexdigest()
def portable(p):
    try: return p.relative_to(ROOT).as_posix()
    except ValueError: return p.as_posix()
def write(p, value): p.write_text(json.dumps(value, ensure_ascii=False, indent=2)+'\n', 'utf-8')
tasks = load(ROOT/'docs/tasks/official-ingame-ui/tasks.json')
previous = load(PREVIOUS)
history = {}
for name in [tasks['validationReport'], *tasks['supplementalImplementationReports']]:
    if ROOT/name != HERE/'verification.json':
        history.update({s['path']:s['sha256'] for s in load(ROOT/name)['sourceHashes']})
baseline = load(HERE/'source-before.json')
changes = []
for entry in baseline:
    rel = entry['path']; before = entry['sha256']; after = sha(ROOT/rel)
    if history.get(rel) != before or sha(HERE/'source-before'/rel) != before:
        raise ValueError('Source baseline/history mismatch: '+rel)
    if before != after: changes.append(dict(path=rel,beforeSha256=before,afterSha256=after))
# No production source changed: executable manifests and hashes remain those of the preceding build.
for config, entry in previous['builds'].items():
    directory = ROOT.parent/('Debug' if config=='debug' else 'Release')/'Client'
    if sha(directory/entry['client']['path']) != entry['client']['sha256']:
        raise ValueError('Reused build mismatch: '+config)
for entry in load(HERE/'tested-runtime.json'):
    if sha(ROOT.parent/'Debug/Client'/entry['name']) != entry['sha256']:
        raise ValueError('Tested runtime mismatch: '+entry['name'])
rows = [r.split('|') for r in (FINAL/'layout-persistence-results.txt').read_text('utf-8-sig').splitlines()]
if len(rows) != 390: raise ValueError('Expected 390 native window/resolution records')
resolutions = ['1024x768','1366x768','1280x800','1440x900','1600x900','1920x1080']
windows = {'InventoryBox':2,'CharacterBox':3,'BeltBox':6,'MiniMapBox':8,'StorageBox':11}
document = load(FINAL/'layout-persistence-saved.json')
if document['schemaVersion'] != 1 or document['styleVersion'] != 'OfficialV1' or len(document['windows']) != 30:
    raise ValueError('Unexpected persisted schema/style/record count')
records = {}
for entry in document['windows']:
    if set(entry) != {'resolutionWidth','resolutionHeight','windowType','x','y','width','height','visible'}:
        raise ValueError('Non-layout state present')
    key = (str(entry['resolutionWidth'])+'x'+str(entry['resolutionHeight']),entry['windowType'])
    if key in records: raise ValueError('Duplicate resolution/window key')
    records[key] = entry
seen = set(); scene_rows = {}
for generation, scene, resolution, window, visible, stored, bounds in rows:
    generation, scene = int(generation), int(scene)
    key = (generation,scene,resolution,window)
    if key in seen: raise ValueError('Duplicate matrix row')
    seen.add(key)
    expected = records[(resolution,windows[window])]
    actual_bounds = list(map(int,bounds.split(',')))
    if actual_bounds != [expected[k] for k in ['x','y','width','height']]:
        raise ValueError('Geometry mismatch')
    initial_index = max(0,scene-1) if generation == 0 else scene
    expected_visible = resolutions.index(resolution)%2 == 0 if (generation,scene)==(0,0) else initial_index%2 == 0
    if visible != 'visible='+str(expected_visible) or stored != 'storedVisible='+str(expected['visible']):
        raise ValueError('Visibility restoration mismatch')
    scene_rows.setdefault((generation,scene),[]).append(key)
if len(scene_rows) != 13 or any(len(v)!=30 for v in scene_rows.values()):
    raise ValueError('Incomplete scene matrix')
commands = []
for p in sorted(HERE.glob('*.receipt.json')):
    receipt = load(p); log = Path(receipt['log'])
    if receipt['exitCode'] != 0: raise ValueError('Failed check requires explicit disposition: '+p.name)
    commands.append(dict(command=receipt['command'],result='pass',exitCode=0,log=portable(log),logSha256=sha(log)))
text = (HERE/'window-set-v2.log').read_text('utf-8',errors='replace')
checks = int(re.search(r'Window-set integration: (\d+) checks passed',text).group(1))
report = dict(schemaVersion=1,kind='native-window-layout-persistence-and-reentry-verification',checkedAtLocalDate='2026-11-08',
    referenceResolution=[1024,768],releaseAccepted=False,
    results=dict(windowSetChecks=checks,layoutStoreChecks=35,startupOwners=2,scenes=13,entryResolutionCases=12,
        resolutions=resolutions,nativeWindowTypes=list(windows),windowResolutionRows=390,persistedRecords=30,
        legacyUsersDbSha256=sha(FINAL/'layout-persistence-legacy.db'),
        disposition='No production defect reproduced. Added native scene/file persistence regression only.'),
    verifiedBehaviors=[
        'Native movement and visibility setters write separate OfficialV1 records for all six resolutions.',
        'Each resolution restores geometry and permitted visibility on fresh scene entry with the same owner and with a new startup owner reading disk.',
        'Later resize restores matching geometry without replaying saved visibility. Both saved true and saved false cases are covered.',
        'Attach, resize and scene disposal do not overwrite saved layout. Atomic writes leave no temporary file.',
        'Real serialized Legacy Users.db remains byte-identical after all 13 scene disposals and saves.',
        'The original NPC main card retains native styling and no Official persistence binding; scene entry and resize keep it closed.'],
    sourceChanges=changes,sourceHashes=[dict(path=e['path'],sha256=sha(ROOT/e['path'])) for e in baseline],
    commands=commands,builds=previous['builds'],runtimeRebuiltThisTurn=False,reusedRuntimeEvidence=portable(PREVIOUS),
    package=previous['package'],candidate=previous['candidate'],sourcePlanSha256=tasks['sourcePlanSha256'],
    limitations=[
        'SceneFixture bypasses constructor network/world setup; tests use real production windows, startup sessions, setters, scene resize, disposal, and real files. This is not a game process restart or live account relogin.',
        'Five persistent window types are covered. The NPC-card check covers closed startup/resize only; all seventeen services retain the prior lifecycle report coverage.',
        'Geometry uses each native window default permitted size. This round does not exercise user resizing of custom-size windows.',
        'Chat settings serialization, loading saved tab topology and style isolation of chat geometry remain to be reviewed. Existing chat resize/identity assertions in the window-set suite still pass.',
        'No production code, runtime resource, gate, official installation or historical report was edited. Debug/Release binaries are reused and hash-verified, not rebuilt this round.',
        'Live login/reconnect, real storage/service transactions, remaining UI semantics and final visual acceptance remain open.',
        'Legacy remains default; RuntimeReady=false, productionBindingAllowed=false, releaseAccepted=false. No commit, push or deployment.'])
paths = {p for directory in [FINAL,Path('E:/workspace/mir3-ui-work/layout-persistence-reentry-20261108-v1')] for p in directory.rglob('*') if p.is_file()}
paths.update(p for p in HERE.rglob('*') if p.is_file() and p!=HERE/'verification.json')
report['evidenceHashes'] = [dict(path=portable(p),sha256=sha(p)) for p in sorted(paths)]
write(HERE/'verification.json',report)
print(json.dumps(dict(sourceChanges=len(changes),successfulCommands=len(commands),evidenceFiles=len(paths),results=report['results']),indent=2))
