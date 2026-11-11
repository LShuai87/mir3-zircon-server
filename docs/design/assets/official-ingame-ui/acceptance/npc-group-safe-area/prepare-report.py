"""Assemble the bounded report from saved receipts, final runtime and immutable history."""
import hashlib, json, re
from pathlib import Path
HERE=Path(__file__).resolve().parent
ROOT=HERE.parents[5]
FINAL=Path('E:/workspace/mir3-ui-work/npc-group-safe-area-20261108-v10')
SCENE=Path('E:/workspace/mir3-ui-work/npc-group-full-scene-20261108-v1')
WINDOW=Path('E:/workspace/mir3-ui-work/npc-group-window-set-20261108-v1')
def load(p): return json.loads(p.read_text('utf-8-sig'))
def sha(p): return hashlib.sha256(p.read_bytes()).hexdigest()
def portable(p):
    try: return p.relative_to(ROOT).as_posix()
    except ValueError: return p.as_posix()
def write(p,obj): p.write_text(json.dumps(obj,ensure_ascii=False,indent=2)+'\n','utf-8')
tasks=load(ROOT/'docs/tasks/official-ingame-ui/tasks.json')
history={}
for name in [tasks['validationReport'],*tasks['supplementalImplementationReports']]:
    path=ROOT/name
    if path==HERE/'verification.json': continue
    history.update({s['path']:s['sha256'] for s in load(path)['sourceHashes']})
baseline=load(HERE/'source-before.json')
sources=[b['path'] for b in baseline]+['Client/Envir/GameUiNpcGroupLayout.cs']
changes=[]; newly=[]
for entry in baseline:
    source=entry['path']; before=entry['sha256']; after=sha(ROOT/source)
    saved=HERE/'source-before'/source
    if sha(saved)!=before: raise ValueError('Baseline mismatch: '+source)
    if source in history:
        if history[source]!=before: raise ValueError('History mismatch: '+source)
        if before!=after: changes.append(dict(path=source,beforeSha256=before,afterSha256=after))
    else: newly.append(dict(path=source,beforeSha256=before,baselineSnapshot=portable(saved)))
newly.append(dict(path=sources[-1],baseline='new source file'))
commands=[]; failed=[]
for file in sorted(HERE.glob('*.receipt.json')):
    receipt=load(file); log=Path(receipt['log'])
    item=dict(command=receipt['command'],result='pass' if receipt['exitCode']==0 else 'fail',exitCode=receipt['exitCode'],log=portable(log),logSha256=sha(log))
    (commands if receipt['exitCode']==0 else failed).append(item)
(FINAL/'tested-runtime.json').is_file() or (_ for _ in ()).throw(ValueError('Missing final tested runtime'))
(HERE/'tested-runtime.json').write_bytes((FINAL/'tested-runtime.json').read_bytes())
npc=(HERE/'npc-v10.log').read_text('utf-8',errors='replace')
m=re.search(r'(\d+) checks; (\d+) actual scenes; (\d+) coordinate hits; (\d+) presented frames; (\d+) resets; (\d+) identical card pixels',npc)
if not m: raise ValueError('Final NPC result missing')
nums=list(map(int,m.groups()))
window=(HERE/'window-set.log').read_text('utf-8',errors='replace')
scene=(HERE/'full-scene.log').read_text('utf-8',errors='replace')
builds={}
for config in ['debug','release']:
    manifest=load(HERE/('release-manifest-'+config+'.json'))
    builds[config]=dict(buildId=manifest['buildId'],client=manifest['client'])
timings=[]
for path in FINAL.glob('*group-layouts.txt'):
    timings.extend(int(line.split('|')[1]) for line in path.read_text('utf-8-sig').splitlines() if line.startswith('response-ms/'))
report=dict(schemaVersion=1,kind='native-npc-service-auxiliary-safe-area-group-layout-verification',checkedAtLocalDate='2026-11-08',releaseAccepted=False,referenceResolution=[1024,768],results=dict(npcChecks=nums[0],npcScenes=nums[1],npcCoordinateHits=nums[2],npcFrames=nums[3],npcResets=nums[4],identicalNativeNpcPixels=nums[5],groupResponseMilliseconds=timings,windowSetSummary=window.strip().splitlines()[-1],fullSceneSummary=scene.strip().splitlines()[-1],pythonTests=172,packageFrames=94),sourceChanges=changes,sourceHashes=[dict(path=p,sha256=sha(ROOT/p)) for p in sources],newlyTrackedSources=newly,commands=commands,builds=builds,package=dict(path='E:/workspace/mir3-ui-work/warehouse-combined-20261108-v2/zl/OfficialV1.Zl',frameCount=94),candidate=dict(path='E:/workspace/mir3-ui-work/warehouse-assets-20261108-v1/zl-v2/OfficialV1.Zl',frameCount=1),sourcePlanSha256=tasks['sourcePlanSha256'],finalValidation=dict(npc='npc-v10.log',debug='build-debug-v3.log',release='build-release.log',python='python-tests.log',windowSet='window-set.log',fullScene='full-scene.log'),limitations=['OfficialV1 group-only geometry changes; Legacy remains default. RuntimeReady=false, productionBindingAllowed=false and releaseAccepted=false.','Complete window bounds and close routing are checked for 15 dialog types in six resolutions and repeated scene entry. None/BuySell group fixtures also perform real device resets.','Layering preserves primary title/close strips and foreground service bodies, not simultaneous visibility of every underlying body. Native modal and tooltip root ordering and stationary hover refresh have targeted checks.','Independent quest opening callbacks, all concurrent quest/service combinations and full lifecycle/manual foreground sequences remain unaccepted.','Thirty synthetic 80-line response fixtures took 2902-3328 ms end to end. The source of this delay is unprofiled; the 5000 ms fixture ceiling is not an interactive performance acceptance criterion.','No live server, real storage/service acceptance, live IME, target-hardware performance or final visual/release/rollback acceptance. No commit, push or deployment; official installation is read-only.'],failedAttempts=failed)
for key in ['package','candidate']: report[key]['sha256']=sha(Path(report[key]['path']))
paths=set(p for d in [FINAL,SCENE,WINDOW] for p in d.rglob('*') if p.is_file())
paths.update(p for p in HERE.rglob('*') if p.is_file() and p!=HERE/'verification.json')
report['evidenceHashes']=[dict(path=portable(p),sha256=sha(p)) for p in sorted(paths)]
write(HERE/'verification.json',report)
print(json.dumps(dict(sources=len(sources),changes=len(changes),successfulCommands=len(commands),failedAttempts=len(failed),evidenceFiles=len(paths),results=report['results']),ensure_ascii=False,indent=2))
