"""Bind expanded equipment gesture coverage to exact sources and tested runtimes."""
import hashlib,json,re
from pathlib import Path
HERE=Path(__file__).resolve().parent
ROOT=HERE.parents[5]
PREVIOUS=HERE.parent/'hud-font-ownership/verification.json'
EVIDENCE_ROOT=Path('E:/workspace/mir3-ui-work')
SUITES={'itemInteractions':'final','itemDrop':'drop','itemTooltip':'tooltip','npcPreservation':'npc','storageScene':'storage'}
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
if len(changes)!=6: raise ValueError('Expected six reviewed test source changes')
explanations={
 'item-interactions.log':'Compilation failed before execution because five dependent test runners omitted two shared native-scene partial sources. All five source lists are repaired and separately executed.',
 'item-interactions-v2.log':'Expanded test incorrectly expected ordinary equipped-item double-click to remove equipment. It failed on native Legacy; no production behavior was changed.',
 'item-interactions-diagnostics.log':'Diagnostic identifies automatic remove double/Weapon/Weapon/False on Legacy. Corrected the test to expect no removal or packet from ordinary equipped-item double-click; right-click removal remains covered.'}
commands=[]; failed=[]
for p in sorted(HERE.glob('*.receipt.json')):
    receipt=load(p); log=Path(receipt['log'])
    item=dict(command=receipt['command'],result='pass' if receipt['exitCode']==0 else 'fail',exitCode=receipt['exitCode'],log=portable(log),logSha256=sha(log))
    if receipt['exitCode']:
        if log.name not in explanations: raise ValueError('Unexpected failed command '+log.name)
        item['explanation']=explanations[log.name]; failed.append(item)
    else: commands.append(item)
if len(failed)!=3: raise ValueError('Missing retained attempts')
builds={}
for config in ['debug','release']:
    manifest=load(PREVIOUS.parent/(config+'-build.json')); directory=ROOT.parent/('Debug' if config=='debug' else 'Release')/'Client'
    for entry in [manifest['client'],*manifest['dependencies']]:
        if sha(directory/entry['path'])!=entry['sha256']: raise ValueError('Runtime drift '+config+entry['path'])
    if manifest['catalog']['runtimeReady'] is not False: raise ValueError('Readiness gate changed')
    if manifest['package']['sha256']!=previous['package']['sha256']: raise ValueError('Package changed')
    builds[config]=dict(buildId=manifest['buildId'],client=manifest['client'])
for key in ['package','candidate']:
    if sha(Path(previous[key]['path']))!=previous[key]['sha256']: raise ValueError('External artifact drift '+key)
results={}; directories=[]; tested=None
for key,suffix in SUITES.items():
    directory=EVIDENCE_ROOT/('equipment-gesture-matrix-20261108-'+suffix+'-v1'); directories.append(directory)
    runtime=load(directory/'tested-runtime.json')
    for entry in runtime:
        if sha(ROOT.parent/'Debug/Client'/entry['name'])!=entry['sha256']: raise ValueError('Tested runtime mismatch '+key)
    if tested is not None and runtime!=tested: raise ValueError('Suites used different runtimes')
    tested=runtime
    if key!='storageScene': results[key]=load(directory/'metrics.json')
match=re.search(r'Storage candidate offline scene: (\d+) checks; (\d+) presented frames; (\d+) device resets; (\d+) identical warehouse pixels',(HERE/'storage-scene.log').read_text('utf-8',errors='replace'))
if not match: raise ValueError('Missing storage result')
results['storageScene']=dict(zip(['checks','presentedFrames','deviceResets','comparedPixels'],map(int,match.groups())))
if results['itemInteractions']['checks']!=20359: raise ValueError('Missing final interaction run')
write(HERE/'tested-runtime.json',tested)
if sha(ROOT/tasks['sourcePlan'])!=tasks['sourcePlanSha256']: raise ValueError('Immutable plan changed')
report=dict(schemaVersion=1,kind='official-equipment-gesture-matrix-verification',checkedAtLocalDate='2026-11-08',referenceResolution=[1024,768],releaseAccepted=False,
 results=dict(suites=results,productionSourceChanged=False,realServerBehaviorVerified=False,finalVisualAcceptance=False),
 implementation=['Expand coordinate-driven automatic wear coverage from Weapon to all 16 slots plus DarkStone in Amulet, using right-click and double-click.', 'Verify native paired-slot selection and right-slot replacement when both bracelet/ring slots are occupied; preserve ordinary equipped-item double-click no-removal behavior.', 'Repair five test entrypoints to include shared native scene-transition and character-refresh partial source dependencies.'],
 verifiedBehaviors=['Legacy, Official and Official scene reentry retain identical ordered item request traces.', 'Automatic wear keeps the target page hidden; pending requests cannot duplicate, refusal unlocks without speculative mutation, success updates exact object identities, and the other 15 slots remain untouched.', 'Removal uses right-click and responses are checked with the equipment page hidden. Existing manual movement, bag, belt, merge and split checks remain.', 'Inspected 1024x768 equipment and extra-page renders. Repeated potion icons are synthetic item fixtures and do not establish real equipment-icon selection. Black world is synthetic.', 'All five modified test entrypoints compile and execute using identical previously built Debug runtime files. Debug and Release dependencies, package and immutable plan hashes are rechecked.'],
 sourceChanges=changes,firstTrackedSourceChanges=[],newlyTrackedSources=[],sourceHashes=[dict(path=r['path'],sha256=sha(ROOT/r['path'])) for r in baseline],commands=commands,failedAttempts=failed,
 builds=builds,runtimeRebuiltThisTurn=False,runtimeManifestReferences={c:portable(PREVIOUS.parent/(c+'-build.json')) for c in ['debug','release']},package=previous['package'],candidate=previous['candidate'],sourcePlanSha256=tasks['sourcePlanSha256'],
 limitations=['Socket-free queues and synthetic server replies do not establish real account/server transactions, real equipment icons or live IME behavior.', 'Expanded gesture coverage is A03 supporting evidence; live-server and final visual acceptance remain open. No claim of zero bugs.', 'Tasks remain 7 done / 3 in_progress / 18 not_started and G0-2/3/4/5 remain open. RuntimeReady=false, productionBindingAllowed=false, releaseAccepted=false. Default Legacy. NPC cards, secondary popups and settings retain existing style. Official installation is read-only; no commit, push or deployment.'])
paths={p for directory in directories for p in directory.rglob('*') if p.is_file()}
paths.update(p for p in HERE.rglob('*') if p.is_file() and p!=HERE/'verification.json')
report['evidenceHashes']=[dict(path=portable(p),sha256=sha(p)) for p in sorted(paths)]
write(HERE/'verification.json',report)
print(json.dumps(dict(sourceChanges=len(changes),successfulCommands=len(commands),failedAttempts=len(failed),evidenceFiles=len(paths),results=results),indent=2))
