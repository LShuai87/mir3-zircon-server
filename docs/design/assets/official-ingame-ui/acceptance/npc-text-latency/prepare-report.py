"""Reconcile this bounded change with immutable history and exact saved evidence."""
import hashlib, json, re
from pathlib import Path
HERE=Path(__file__).resolve().parent
ROOT=HERE.parents[5]
BEFORE=Path('E:/workspace/mir3-ui-work/npc-text-latency-before-20261108-v1')
FINAL=Path('E:/workspace/mir3-ui-work/npc-text-latency-after-20261108-v3')
SCENE=Path('E:/workspace/mir3-ui-work/npc-text-latency-full-scene-20261108-v1')
WINDOW=Path('E:/workspace/mir3-ui-work/npc-text-latency-window-set-20261108-v1')
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
changes=[]
for item in baseline:
    source=item['path']; before=item['sha256']; after=sha(ROOT/source)
    if sha(HERE/'source-before'/source)!=before or history.get(source)!=before:
        raise ValueError('Baseline/history mismatch: '+source)
    if before!=after: changes.append(dict(path=source,beforeSha256=before,afterSha256=after))
commands=[]; failed=[]
for file in sorted(HERE.glob('*.receipt.json')):
    receipt=load(file); log=Path(receipt['log'])
    item=dict(command=receipt['command'],result='pass' if receipt['exitCode']==0 else 'fail',exitCode=receipt['exitCode'],log=portable(log),logSha256=sha(log))
    if file.name=='npc-profile-before.receipt.json': item['purpose']='profile unchanged baseline runtime'
    (commands if receipt['exitCode']==0 else failed).append(item)
(HERE/'tested-runtime.json').write_bytes((FINAL/'tested-runtime.json').read_bytes())
npc=(HERE/'npc-after-v3.log').read_text('utf-8',errors='replace')
m=re.search(r'(\d+) checks; (\d+) actual scenes; (\d+) coordinate hits; (\d+) presented frames; (\d+) resets; (\d+) identical card pixels',npc)
if not m: raise ValueError('Final NPC result missing')
nums=list(map(int,m.groups()))
comparison=load(HERE/'latency-comparison.json')
latency={}
for phase in ['before','after']:
    group=[row for row in comparison if row['phase']==phase]
    values=[r['milliseconds'] for row in group for r in row['responses']]
    latency[phase]=dict(count=len(values),minMs=min(values),maxMs=max(values),meanMs=sum(values)/len(values),optionLayoutMs=[r['optionLayoutMs'] for r in group])
assertions=(FINAL/'assertions.txt').read_text('utf-8-sig').splitlines()
builds={}
for config in ['debug','release']:
    manifest=load(HERE/('release-manifest-'+config+'.json'))
    builds[config]=dict(buildId=manifest['buildId'],client=manifest['client'])
report=dict(schemaVersion=1,kind='native-npc-paragraph-measurement-latency-and-crlf-verification',checkedAtLocalDate='2026-11-08',releaseAccepted=False,referenceResolution=[1024,768],results=dict(npcChecks=nums[0],npcScenes=nums[1],npcCoordinateHits=nums[2],npcFrames=nums[3],npcResets=nums[4],identicalNativeNpcPixels=nums[5],responseLatency=latency,nativeGeometryChecks=sum('paragraph-local option equals native full-document geometry/text:' in l for l in assertions),independentTextPixelComparisons=sum('viewport text texture equals independent full-document render' in l for l in assertions),windowSetSummary=(HERE/'window-set.log').read_text('utf-8',errors='replace').strip().splitlines()[-1],fullSceneSummary=(HERE/'full-scene.log').read_text('utf-8',errors='replace').strip().splitlines()[-1],pythonTests=172,packageFrames=94),sourceChanges=changes,sourceHashes=[dict(path=s['path'],sha256=sha(ROOT/s['path'])) for s in baseline],commands=commands,failedAttempts=failed,builds=builds,package=dict(path='E:/workspace/mir3-ui-work/warehouse-combined-20261108-v2/zl/OfficialV1.Zl',frameCount=94),candidate=dict(path='E:/workspace/mir3-ui-work/warehouse-assets-20261108-v1/zl-v2/OfficialV1.Zl',frameCount=1),sourcePlanSha256=tasks['sourcePlanSha256'],finalValidation=dict(npc='npc-after-v3.log',debug='build-debug-v2.log',release='build-release-v2.log',python='python-tests.log',windowSet='window-set.log',fullScene='full-scene.log'),limitations=['OfficialV1 only; native NPC/secondary styles, labels, option IDs and callbacks retained. Legacy remains default; RuntimeReady=false, productionBindingAllowed=false and releaseAccepted=false.','Same-machine fixed synthetic fixtures and a 1000 ms regression ceiling do not constitute target-hardware or universal text performance acceptance.','Native full-document geometry oracle plus independent full-document text rendering cover five selected paragraph cases across two candidate scene entries. All fonts and paragraph shapes remain unaccepted.','Independent quest opening callbacks, concurrent quest/service groups, manual foreground sequences and full lifecycle work remain pending.','No live-server, real service/storage, live IME, final visual, hardware device-loss or release/rollback acceptance. No commit, push or deployment; official installation is read-only.'])
for key in ['package','candidate']: report[key]['sha256']=sha(Path(report[key]['path']))
external=[BEFORE,FINAL,SCENE,WINDOW,Path('E:/workspace/mir3-ui-work/npc-text-latency-after-20261108-v1'),Path('E:/workspace/mir3-ui-work/npc-text-latency-after-20261108-v2')]
paths=set(p for d in external for p in d.rglob('*') if p.is_file())
paths.update(p for p in HERE.rglob('*') if p.is_file() and p!=HERE/'verification.json')
report['evidenceHashes']=[dict(path=portable(p),sha256=sha(p)) for p in sorted(paths)]
write(HERE/'verification.json',report)
print(json.dumps(dict(sources=len(baseline),changes=len(changes),successfulCommands=len(commands),failedAttempts=len(failed),evidenceFiles=len(paths),latency=latency),ensure_ascii=False,indent=2))
