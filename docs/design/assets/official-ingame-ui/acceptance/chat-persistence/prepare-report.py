"""Record the independent chat-preference storage foundation; native routing remains open."""
import hashlib, json, re
from pathlib import Path
HERE=Path(__file__).resolve().parent
ROOT=HERE.parents[5]
PREVIOUS=ROOT/'docs/design/assets/official-ingame-ui/acceptance/chat-reconstruction/verification.json'
FINAL=Path('E:/workspace/mir3-ui-work/chat-persistence-store-20261108-final')
WINDOWS=Path('E:/workspace/mir3-ui-work/chat-persistence-window-set-20261108-v1')
SCENE=Path('E:/workspace/mir3-ui-work/chat-persistence-full-scene-20261108-v1')
def load(p): return json.loads(p.read_text('utf-8-sig'))
def sha(p): return hashlib.sha256(p.read_bytes()).hexdigest()
def portable(p):
    try: return p.relative_to(ROOT).as_posix()
    except ValueError: return p.as_posix()
def write(p,v): p.write_text(json.dumps(v,ensure_ascii=False,indent=2)+'\n','utf-8')
tasks=load(ROOT/'docs/tasks/official-ingame-ui/tasks.json')
previous=load(PREVIOUS)
history={}
for name in [tasks['validationReport'],*tasks['supplementalImplementationReports']]:
    if ROOT/name != HERE/'verification.json': history.update({s['path']:s['sha256'] for s in load(ROOT/name)['sourceHashes']})
baseline=load(HERE/'source-before.json'); changes=[]; newlyTracked=[]
for row in baseline:
    rel=row['path']; before=row['sha256']; after=sha(ROOT/rel)
    if sha(HERE/'source-before'/rel)!=before: raise ValueError('Baseline snapshot mismatch '+rel)
    if rel in history:
        if history[rel]!=before: raise ValueError('History baseline mismatch '+rel)
        if before!=after: changes.append(dict(path=rel,beforeSha256=before,afterSha256=after))
    else: newlyTracked.append(dict(path=rel,beforeSha256=before,afterSha256=after))
new=['Client/Envir/GameUiChatStore.cs','tests/game-ui/OfficialUiChatStoreTests.cs','tests/Run-GameUiChatStoreTests.ps1']
if any(name in history for name in new): raise ValueError('New source already in earlier history')
newlyTracked += [dict(path=name,sha256=sha(ROOT/name),createdThisRound=True) for name in new]
commands=[]; failed=[]
for p in sorted(HERE.glob('*.receipt.json')):
    receipt=load(p); log=Path(receipt['log'])
    item=dict(command=receipt['command'],result='pass' if receipt['exitCode']==0 else 'fail',exitCode=receipt['exitCode'],log=portable(log),logSha256=sha(log))
    (commands if receipt['exitCode']==0 else failed).append(item)
if {Path(row['log']).name for row in failed}!={'chat-store.log','chat-store-v2.log'}: raise ValueError('Unexpected failed attempts')
rows=(FINAL/'chat-store-results.txt').read_text('utf-8-sig').splitlines()
if len(rows)!=59 or len(set(rows))!=59: raise ValueError('Unexpected store evidence rows')
storeChecks=int(re.search(r'Chat store: (\d+) checks passed',(HERE/'chat-store-final.log').read_text('utf-8',errors='replace')).group(1))
windowChecks=int(re.search(r'Window-set integration: (\d+) checks passed',(HERE/'window-set.log').read_text('utf-8',errors='replace')).group(1))
if storeChecks!=4487 or windowChecks!=168514: raise ValueError('Unexpected checks')
for log,expected in [('chat-layout.log','Chat geometry: 940 checks passed'),('full-scene.log','Full-scene offline integration: 180 checks passed')]:
    if expected not in (HERE/log).read_text('utf-8',errors='replace'): raise ValueError('Missing regression result '+log)
if sha(ROOT/tasks['sourcePlan'])!=tasks['sourcePlanSha256']: raise ValueError('Historical plan modified')
if tasks['statusCounts']!={'done':7,'in_progress':3,'blocked':0,'not_started':18}: raise ValueError('Task count changed')
builds={}
for config in ['debug','release']:
    doc=load(HERE/(config+'-build.json'))
    binary=ROOT.parent/('Debug' if config=='debug' else 'Release')/'Client/Zircon.exe'
    if sha(binary)!=doc['client']['sha256'] or doc['catalog']['runtimeReady'] is not False: raise ValueError('Build identity/gate mismatch')
    builds[config]=dict(buildId=doc['buildId'],client=doc['client'])
write(HERE/'tested-runtime.json',[dict(name=name,sha256=sha(ROOT.parent/'Debug/Client'/name)) for name in ['Zircon.exe','Library.dll','SlimDX.dll']])
report=dict(schemaVersion=1,kind='official-chat-independent-store-foundation-verification',checkedAtLocalDate='2026-11-08',referenceResolution=[1024,768],releaseAccepted=False,
    results=dict(chatStoreChecks=storeChecks,evidenceRows=len(rows),flagCombinations=4096,resolutionRoundTrips=6,windowSetChecks=windowChecks,chatGeometryChecks=940,fullSceneChecks=180,
        nativeSaveRoutingIntegrated=False,nativeLoadRoutingIntegrated=False,startupChatRestorationIntegrated=False),
    implementation=[
        'New GameUiChatStore source is included in Client.csproj; independent OfficialV1 schema and file contain no native database objects.',
        'Stores up to six matching-resolution snapshots with stable group IDs, geometry, primary/floating state, ordered pages, selected page and the existing 12 channel/transparency/alert flags.',
        'Strict fields/types, bounded strict UTF-8 read, geometry/count/version validation, deep copies and atomic flushed file replacement; read failures never rewrite a file and failed saves preserve prior disk/cache.',
        'InvalidDataException is explicitly recoverable on this target framework; the first fixture error exposed that it does not inherit IOException. Corrected 34-character test IDs to canonical 32-character GUIDs.'],
    verifiedBehaviors=[
        'All 4096 filter combinations round-trip in page order, including Unicode names and escaped quotes/backslash/newline.',
        'Saving six resolutions retains other resolution entries; explicit empty topology remains empty; callers/readers cannot mutate cached snapshots.',
        '33 invalid-save cases retain previous disk bytes and cached state with diagnostics; exact name/geometry boundaries are accepted.',
        '18 invalid-document cases, invalid UTF-8 and UTF-16 BOM are ignored without disk writes; valid UTF-8 BOM is accepted and reload recovers after repair or deletion.',
        'Exclusive file locks exercise failed atomic replacement and failed reads, with previous bytes retained and temporary files cleaned; a file used as configuration directory fails safely.',
        'Existing window/startup/chat/full-scene regressions pass against the final Debug binary; both Debug and Release are rebuilt and manifests generated.'],
    sourceChanges=changes,newlyTrackedSources=newlyTracked,sourceHashes=[dict(path=name,sha256=sha(ROOT/name)) for name in [*[r['path'] for r in baseline],*new]],
    commands=commands,failedAttempts=failed,regressionBaselineRuntime=dict(report=portable(PREVIOUS),builds=previous['builds'],testedRuntime=load(PREVIOUS.parent/'tested-runtime.json')),
    builds=builds,runtimeRebuiltThisTurn=True,package=previous['package'],candidate=previous['candidate'],sourcePlanSha256=tasks['sourcePlanSha256'],
    limitations=[
        'Storage foundation only: GameScene.SaveChatTabs, LoadChatTabs, ChatOptionsDialog and startup/resize adapters are unchanged. Native save-all still uses its existing database path. This report does not claim Legacy chat settings are isolated by the current UI.',
        'The store test native Users.db file is a byte sentinel, not a native database integration test. Existing native DB lifecycle tests are separate regressions and do not verify Official save-all isolation.',
        'No saved Official chat topology is restored at login or explicit reload yet. Stable group IDs, live capture, native reconstruction and matching-resolution geometry routing remain to implement.',
        'The store is designed for one owning client UI session; concurrent independent store instances do not merge external writes.',
        'Live return-to-login/reconnect, real storage/services, remaining field/equipment semantics, hardware/fonts/IME and final visual acceptance remain open.',
        'Legacy remains default; RuntimeReady=false, productionBindingAllowed=false, releaseAccepted=false. No commit, push or deployment; official installation remains read-only.'])
paths={p for directory in [FINAL,WINDOWS,SCENE,Path('E:/workspace/mir3-ui-work/chat-persistence-store-20261108-v3')] for p in directory.rglob('*') if p.is_file()}
paths.update(p for p in HERE.rglob('*') if p.is_file() and p!=HERE/'verification.json')
report['evidenceHashes']=[dict(path=portable(p),sha256=sha(p)) for p in sorted(paths)]
write(HERE/'verification.json',report)
print(json.dumps(dict(sourceChanges=len(changes),newlyTracked=len(newlyTracked),successfulCommands=len(commands),failedAttempts=len(failed),evidenceFiles=len(paths),results=report['results']),indent=2))
