"""Bind HUD font ownership regression, runtimes and immutable source transitions."""
import hashlib,json,re
from pathlib import Path
HERE=Path(__file__).resolve().parent
ROOT=HERE.parents[5]
SCENE=Path('E:/workspace/mir3-ui-work/hud-font-ownership-20261108-fullscene-v1')
PREVIOUS=HERE.parent/'hud-level-bezel/verification.json'
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
if len(changes)!=2: raise ValueError('Expected two reviewed source changes')
explanations={
 'before-fix.log':'Expected regression on the previous Debug runtime: restoring Legacy disposes native readout fonts because temporary captions share ownership. GetHeight(Graphics) throws ArgumentException. The fixed runtime gives captions cloned fonts.'}
commands=[]; failed=[]
for p in sorted(HERE.glob('*.receipt.json')):
    receipt=load(p); log=Path(receipt['log'])
    item=dict(command=receipt['command'],result='pass' if receipt['exitCode']==0 else 'fail',exitCode=receipt['exitCode'],log=portable(log),logSha256=sha(log))
    if receipt['exitCode']:
        if log.name not in explanations: raise ValueError('Unexpected failed command '+log.name)
        item['explanation']=explanations[log.name]; failed.append(item)
    else: commands.append(item)
if len(failed)!=1: raise ValueError('Missing retained attempts')
match=re.search(r'Full-scene offline integration: (\d+) checks passed; (\d+) real constructors, (\d+) presented frames, (\d+) explicit Device.Reset calls; (\d+) identical panel pixels',(HERE/'full-scene.log').read_text('utf-8',errors='replace'))
if not match: raise ValueError('Missing full scene result')
full,constructors,frames,resets,pixels=map(int,match.groups())
if '796 checks passed' not in (HERE/'hud-layout.log').read_text('utf-8',errors='replace'): raise ValueError('Missing final HUD pass')
builds={}
for config in ['debug','release']:
    manifest=load(HERE/(config+'-build.json')); directory=ROOT.parent/('Debug' if config=='debug' else 'Release')/'Client'
    for entry in [manifest['client'],*manifest['dependencies']]:
        if sha(directory/entry['path'])!=entry['sha256']: raise ValueError('Runtime drift '+config+entry['path'])
    if manifest['catalog']['runtimeReady'] is not False: raise ValueError('Readiness gate changed')
    if manifest['package']['sha256']!=previous['package']['sha256']: raise ValueError('Package changed')
    builds[config]=dict(buildId=manifest['buildId'],client=manifest['client'])
tested=load(SCENE/'tested-runtime.json')
for entry in tested:
    if sha(ROOT.parent/'Debug/Client'/entry['name'])!=entry['sha256']: raise ValueError('Tested runtime mismatch')
write(HERE/'tested-runtime.json',tested)
if sha(ROOT/tasks['sourcePlan'])!=tasks['sourcePlanSha256']: raise ValueError('Immutable plan changed')
report=dict(schemaVersion=1,kind='official-hud-font-ownership-verification',checkedAtLocalDate='2026-11-08',referenceResolution=[1024,768],releaseAccepted=False,
 results=dict(hudChecks=796,fullSceneChecks=full,realGameConstructors=constructors,presentedFrames=frames,explicitDeviceResets=resets,identicalPanelPixels=pixels,productionSourceChanged=True,realServerBehaviorVerified=False,finalVisualAcceptance=False),
 implementation=['Clone each temporary readout caption font so disposing the caption cannot dispose the native label font.','Check reference identity and native font metrics after restore, ten switches, failed initial installation and four external lifecycle paths. Check that cloned caption fonts are released.'],
 verifiedBehaviors=['The new behavioral regression failed against the prior Debug runtime with ArgumentException from Font.GetHeight(Graphics); the fixed runtime passes all 796 HUD checks.','The 1024x768 candidate render was inspected: the level remains in the lower-left ring and captions retain their appearance. Black world pixels belong to the isolated fixture.','Full-scene offline regressions cover character packets, Legacy parity, native input retention, scene entry and rendering. Real account/server behavior remains unverified.','Debug and Release were rebuilt; manifests inspect the actual client, dependencies, compiled catalog and unchanged 94-frame package. Release gates remain false.'],
 sourceChanges=changes,firstTrackedSourceChanges=[],newlyTrackedSources=[],sourceHashes=[dict(path=r['path'],sha256=sha(ROOT/r['path'])) for r in baseline],commands=commands,failedAttempts=failed,
 regressionBaselineRuntime=dict(report=portable(PREVIOUS),builds=previous['builds'],testedRuntime=load(PREVIOUS.parent/'tested-runtime.json')),
 builds=builds,runtimeRebuiltThisTurn=True,package=previous['package'],candidate=previous['candidate'],sourcePlanSha256=tasks['sourcePlanSha256'],
 limitations=['Offline synthetic world and serialized loopback do not establish real account/server behavior, real item transactions, live IME or hardware device loss.','Source-art coordinates and candidate render are local evidence; final visual acceptance and physical font/DPI coverage remain open. Glyph fit for 1/99/999 is checked in the current renderer context; unusually long values retain native full-value hints.','Tasks remain 7 done / 3 in_progress / 18 not_started and G0-2/3/4/5 remain open. RuntimeReady=false, productionBindingAllowed=false, releaseAccepted=false. Default Legacy. NPC cards, secondary popups and settings retain existing style. Official installation is read-only; no commit, push or deployment.'])
paths={p for directory in [SCENE] for p in directory.rglob('*') if p.is_file()}
paths.update(p for p in HERE.rglob('*') if p.is_file() and p!=HERE/'verification.json')
report['evidenceHashes']=[dict(path=portable(p),sha256=sha(p)) for p in sorted(paths)]
write(HERE/'verification.json',report)
print(json.dumps(dict(sourceChanges=len(changes),successfulCommands=len(commands),failedAttempts=len(failed),evidenceFiles=len(paths),results=report['results']),indent=2))
