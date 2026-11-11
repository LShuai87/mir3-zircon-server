"""Bind title clipping/restoration to exact source transitions and runtime evidence."""
import hashlib,json,re
from pathlib import Path
HERE=Path(__file__).resolve().parent
ROOT=HERE.parents[5]
PREVIOUS=HERE.parent/'equipment-gesture-matrix/verification.json'
EXTERNAL=Path('E:/workspace/mir3-ui-work')
def load(p): return json.loads(p.read_text('utf-8-sig'))
def sha(p): return hashlib.sha256(p.read_bytes()).hexdigest()
def portable(p):
    try: return p.relative_to(ROOT).as_posix()
    except ValueError: return p.as_posix()
def write(p,v): p.write_text(json.dumps(v,ensure_ascii=False,indent=2)+'\n','utf-8')
tasks=load(ROOT/'docs/tasks/official-ingame-ui/tasks.json'); previous=load(PREVIOUS)
history={}
for name in [tasks['validationReport'],*tasks['supplementalImplementationReports']]:
    if ROOT/name != HERE/'verification.json': history.update({s['path']:s['sha256'] for s in load(ROOT/name)['sourceHashes']})
baseline=load(HERE/'source-before.json'); changes=[]
for row in baseline:
    rel=row['path']; before=row['sha256']; after=sha(ROOT/rel)
    if sha(HERE/'source-before'/rel)!=before or history.get(rel)!=before: raise ValueError('Baseline mismatch '+rel)
    if before!=after: changes.append(dict(path=rel,beforeSha256=before,afterSha256=after))
if len(changes)!=4: raise ValueError('Expected four reviewed source changes')
explanations={
 'before-fix.log':'Test development compilation error: DXWindow is abstract. Fixed using a minimal concrete TitleTestWindow. This does not establish a production defect.',
 'before-fix-runtime.log':'Test development compilation error: WindowType required its Client.UserModels namespace. Corrected the test. This does not establish a production defect.',
 'before-fix-confirmed.log':'Behavioral regression reproduced against previous client: short label 55x19 was centered at (46,1) outside the declared (8,2,132,16) title area.',
 'before-fix-long-title.log':'Behavioral regression reproduced against previous client with long title first: label 742x19 at (-297,1) exceeded the declared (8,2,132,16) title area and close region.'}
commands=[]; failed=[]
for p in sorted(HERE.glob('*.receipt.json')):
    receipt=load(p); log=Path(receipt['log'])
    item=dict(command=receipt['command'],result='pass' if receipt['exitCode']==0 else 'fail',exitCode=receipt['exitCode'],log=portable(log),logSha256=sha(log))
    if receipt['exitCode']:
        if log.name not in explanations: raise ValueError('Unexpected failed command '+log.name)
        item['explanation']=explanations[log.name]; failed.append(item)
    else: commands.append(item)
if len(failed)!=4: raise ValueError('Missing retained attempts')
builds={}
for config in ['debug','release']:
    manifest=load(HERE/(config+'-build.json')); directory=ROOT.parent/('Debug' if config=='debug' else 'Release')/'Client'
    for entry in [manifest['client'],*manifest['dependencies']]:
        if sha(directory/entry['path'])!=entry['sha256']: raise ValueError('Runtime drift '+config+entry['path'])
    if manifest['catalog']['runtimeReady'] is not False: raise ValueError('Readiness gate changed')
    if manifest['package']['sha256']!=previous['package']['sha256']: raise ValueError('Package changed')
    builds[config]=dict(buildId=manifest['buildId'],client=manifest['client'])
for key in ['package','candidate']:
    if sha(Path(previous[key]['path']))!=previous[key]['sha256']: raise ValueError('External artifact drift '+key)
directories=[EXTERNAL/('window-title-bounds-20261108-'+s) for s in ['minimap-v1','minimap-v2','windowset-v1','fullscene-v1']]
tested=None
for directory in directories:
    runtime=load(directory/'tested-runtime.json')
    for entry in runtime:
        if sha(ROOT.parent/'Debug/Client'/entry['name'])!=entry['sha256']: raise ValueError('Tested runtime mismatch '+directory.name)
    if tested is not None and runtime!=tested: raise ValueError('Suites used different runtimes')
    tested=runtime
write(HERE/'tested-runtime.json',tested)
results=dict(minimap=load(directories[1]/'metrics.json'))
for key,log,pattern in [
 ('minimapChecks','minimap-final.log',r'Minimap layout: (\d+) checks passed'),
 ('windowSetChecks','window-set.log',r'Window-set integration: (\d+) checks passed'),
 ('fullSceneChecks','full-scene.log',r'Full-scene offline integration: (\d+) checks passed')]:
    match=re.search(pattern,(HERE/log).read_text('utf-8',errors='replace'))
    if not match: raise ValueError('Missing result '+key)
    results[key]=int(match.group(1))
if results['minimapChecks']!=194052 or results['windowSetChecks']!=168890 or results['fullSceneChecks']!=4328: raise ValueError('Unexpected final suite results')
if sha(ROOT/tasks['sourcePlan'])!=tasks['sourcePlanSha256']: raise ValueError('Immutable plan changed')
report=dict(schemaVersion=1,kind='official-window-title-bounds-verification',checkedAtLocalDate='2026-11-08',referenceResolution=[1024,768],releaseAccepted=False,
 results=dict(suites=results,productionSourceChanged=True,realServerBehaviorVerified=False,finalVisualAcceptance=False),
 implementation=['Bound skinned DXWindow title labels to the declared skin title rectangle. Use single-line centered text, end ellipsis and literal ampersands.', 'Capture native AutoSize, DrawFormat and fixed size once per native-to-skin binding; preserve snapshots on skin replacement and restore them using current text on rollback.', 'Add repeated auto/fixed title binding, long/short/empty/multiline text, font identity, input exclusion, headerless skin, HasTitle and current-text rollback checks. Add real GPU pixel clipping checks and runtime evidence.'],
 verifiedBehaviors=['Long title exceeded 742 pixels before the fix; after the fix the title occupies exactly (8,2,132,16) in the minimap fixture.', 'Six GPU title frames at two positions show visible text and 193368 identical pixels outside the title, including the collapse region. Inspected long-title screenshot with visible ellipsis.', '206040 viewport pixel comparisons across two placements and three map states remain correct.', '194052 minimap, 168890 window-set and 4328 full-scene checks pass against the rebuilt Debug runtime. Native title settings and current text measurements restore after repeated skin transitions.'],
 userReference=dict(path=portable(HERE/'user-resolution-reference.png'),imagePixels=[1280,960],configuredResolution=[1024,768],windowed=True,note='Uploaded image pixels differ from the configured game size. Layout baseline follows the user-confirmed 1024x768 setting, not the uploaded image dimensions. No repeat settings, character or bag screenshot requested.'),
 sourceChanges=changes,firstTrackedSourceChanges=[],newlyTrackedSources=[],sourceHashes=[dict(path=r['path'],sha256=sha(ROOT/r['path'])) for r in baseline],commands=commands,failedAttempts=failed,
 builds=builds,runtimeRebuiltThisTurn=True,runtimeManifestReferences={c:portable(HERE/(c+'-build.json')) for c in ['debug','release']},package=previous['package'],candidate=previous['candidate'],sourcePlanSha256=tasks['sourcePlanSha256'],
 limitations=['Synthetic minimap artwork verifies clipping and ordering, not final official-art acceptance. Pixel checks include the collapse region but do not draw the native collapse glyph in the synthetic fixture; window-set/full-scene checks exercise real controls.', 'Loopback/synthetic scene tests do not establish real account/server transactions, live IME or final visual acceptance. No claim of zero bugs.', 'Tasks remain 7 done / 3 in_progress / 18 not_started and G0-2/3/4/5 remain open. RuntimeReady=false, productionBindingAllowed=false, releaseAccepted=false. Default Legacy. NPC cards, secondary popups and settings retain existing style. Official installation is read-only; no commit, push or deployment.'])
paths={p for directory in directories for p in directory.rglob('*') if p.is_file()}
paths.update(p for p in HERE.rglob('*') if p.is_file() and p!=HERE/'verification.json')
report['evidenceHashes']=[dict(path=portable(p),sha256=sha(p)) for p in sorted(paths)]
write(HERE/'verification.json',report)
print(json.dumps(dict(sourceChanges=len(changes),successfulCommands=len(commands),failedAttempts=len(failed),evidenceFiles=len(paths),results=results),indent=2))
