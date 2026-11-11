"""Record native Official chat routing against immutable source/report history."""
import hashlib, json, re
from pathlib import Path
HERE=Path(__file__).resolve().parent
ROOT=HERE.parents[5]
PREVIOUS=HERE.parent/'chat-persistence/verification.json'
WINDOWS=Path('E:/workspace/mir3-ui-work/chat-routing-window-set-20261108-final')
STORE=Path('E:/workspace/mir3-ui-work/chat-routing-store-20261108-v1')
SCENE=Path('E:/workspace/mir3-ui-work/chat-routing-full-scene-20261108-v1')
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
baseline=load(HERE/'source-before.json'); changes=[]
for row in baseline:
    rel=row['path']; before=row['sha256']; after=sha(ROOT/rel)
    if sha(HERE/'source-before'/rel)!=before: raise ValueError('Baseline snapshot mismatch '+rel)
    if history.get(rel)!=before: raise ValueError('History baseline mismatch '+rel)
    if before!=after: changes.append(dict(path=rel,beforeSha256=before,afterSha256=after))
new=['tests/game-ui/OfficialUiChatPersistenceTests.cs']
if any(name in history for name in new): raise ValueError('New source already tracked')
commands=[]; failed=[]
for p in sorted(HERE.glob('*.receipt.json')):
    receipt=load(p); log=Path(receipt['log'])
    item=dict(command=receipt['command'],result='pass' if receipt['exitCode']==0 else 'fail',exitCode=receipt['exitCode'],log=portable(log),logSha256=sha(log))
    (commands if receipt['exitCode']==0 else failed).append(item)
if {Path(row['log']).name for row in failed}!={'window-set.log','window-set-v2.log','window-set-v3.log'}: raise ValueError('Unexpected failed attempts')
rows=(WINDOWS/'chat-persistence-results.txt').read_text('utf-8-sig').splitlines()
if len(rows)!=35 or len(set(rows))!=35: raise ValueError('Unexpected routing evidence rows')
def checks(name,pattern):
    return int(re.search(pattern,(HERE/name).read_text('utf-8',errors='replace')).group(1))
windowChecks=checks('window-set-final.log',r'Window-set integration: (\d+) checks passed')
storeChecks=checks('chat-store.log',r'Chat store: (\d+) checks passed')
geometryChecks=checks('chat-geometry.log',r'Chat geometry: (\d+) checks passed')
sceneChecks=checks('full-scene.log',r'Full-scene offline integration: (\d+) checks passed')
if (windowChecks,storeChecks,geometryChecks,sceneChecks)!=(168890,4487,940,180): raise ValueError('Unexpected test totals')
builds={}
for config in ['debug','release']:
    doc=load(HERE/(config+'-build.json')); binary=ROOT.parent/('Debug' if config=='debug' else 'Release')/'Client/Zircon.exe'
    if sha(binary)!=doc['client']['sha256'] or doc['catalog']['runtimeReady'] is not False: raise ValueError('Build identity/gate mismatch')
    builds[config]=dict(buildId=doc['buildId'],client=doc['client'])
write(HERE/'tested-runtime.json',[dict(name=name,sha256=sha(ROOT.parent/'Debug/Client'/name)) for name in ['Zircon.exe','Library.dll','SlimDX.dll']])
report=dict(schemaVersion=1,kind='official-chat-native-persistence-routing-verification',checkedAtLocalDate='2026-11-08',referenceResolution=[1024,768],releaseAccepted=False,
    results=dict(windowSetChecks=windowChecks,chatStoreChecks=storeChecks,chatGeometryChecks=geometryChecks,fullSceneChecks=sceneChecks,routingEvidenceRows=len(rows),storeEvidenceRows=59,
        nativeSaveRoutingIntegrated=True,nativeLoadRoutingIntegrated=True,startupChatRestorationIntegrated=True,matchingTopologyResizeRestorationIntegrated=True,
        nativeDatabaseByteIsolationVerified=True,realAccountLoginVerified=False,liveImeVerified=False),
    implementation=[
        'Official save-all captures actual native groups and header order into GameUiChat.OfficialV1.json; Legacy retains native DB save/load code. A failed or busy save returns false and the real button reports failure.',
        'Group IDs survive six resolution saves. Capture includes names, all 12 native flags, selected header, geometry and primary/floating state; empty topology is explicit.',
        'After the Official scene owner is installed, matching preferences reconstruct original ChatTab and ChatOptionsPanel types via AddNewTab. Explicit native reload uses the same path. Missing/invalid snapshots use native behavior without rewriting JSON.',
        'Ordinary resize restores saved geometry only for matching group IDs and ordered names/count; native page instances, current selection/filters/history and the editor/HWND/draft/selection remain live.',
        'Fixed a real multi-page removal null dereference: native removal now keeps the options panel alive until native tab disposal and selection/layout callbacks finish.',
        'Restore preserves the valid no-selected-page state across frame refresh and capture, while a native header click selects a page normally. Binding selection handlers are removed on detach.',
        'Saved reconstruction failure follows existing startup failure containment: restart required, adapters detached, verified package retained until scene disposal.'],
    verifiedBehaviors=[
        'Real save/reload/reset buttons and original confirmation controls; reordered headers, three native pages, two floating groups and all 12 flag bits over six resolutions.',
        'Six saved-resolution geometry recoveries retain original tabs, histories, editor, HWND, draft and selection range; live filter/selected-page edits are not replayed on resize.',
        'Rename, header reorder and added page invalidate stale geometry; native update blocks saving without changing bytes.',
        'Explicit reload reconstructs flags/selection/header order/geometry while preserving input. Cancellation retains pages. No leaked temporary groups or duplicated topology listeners.',
        'Actual exclusive JSON file lock causes false save result and button failure announcement; previous file remains byte-identical. Corrupt JSON falls back to native DB and is not rewritten; repaired file reloads.',
        'Empty topology saves and reloads after reset; empty startup works for the same owner and a fresh owner reading disk. Six matching resolutions restore on same-owner scene reentry and fresh-owner disk startup without preference writes.',
        'Docked primary remains exactly 574x106 at the compiled dock through six resolutions; missing matching resolution uses native defaults without losing the saved entry.',
        'Valid selected=-1 survives explicit reload, frame refresh and resave; actual native header selection still works.',
        'An injected saved-topology callback failure retains the startup owner/package, requires restart and cleans up on scene disposal.',
        'Real native Users.db is seeded using Legacy save. Official save/reset/reload/reentry/disposal followed by Session.Save(true) leaves its bytes unchanged.',
        'Final Debug binary passed store, chat geometry, window/startup and full offline scene suites; Debug/Release builds and inspection manifests generated.'],
    sourceChanges=changes,newlyTrackedSources=[dict(path=name,sha256=sha(ROOT/name),createdThisRound=True) for name in new],
    sourceHashes=[dict(path=name,sha256=sha(ROOT/name)) for name in [*[r['path'] for r in baseline],*new]],
    commands=commands,failedAttempts=failed,regressionBaselineRuntime=dict(report=portable(PREVIOUS),builds=previous['builds'],testedRuntime=load(PREVIOUS.parent/'tested-runtime.json')),
    builds=builds,runtimeRebuiltThisTurn=True,package=previous['package'],candidate=previous['candidate'],sourcePlanSha256=tasks['sourcePlanSha256'],
    limitations=[
        'Native controls and real databases are exercised in isolated processes. Socket/world setup is bypassed in the focused suite; the full-scene suite uses synthetic user/map and legacy resources. These are not real account login or server transaction acceptance.',
        'Explicit reconstruction follows native behavior and replaces pages/history; ordinary resize retains them. Native reset may replace the editor; explicit saved reload does not.',
        'The store remains single-owner; independent concurrent instances do not merge external writes. Matching topology uses stable group IDs and ordered page names/count; filter and current selection intentionally do not participate.',
        'Live return-to-login/disconnect/reconnect, real storage/service transactions, remaining field/equipment semantics, hardware/fonts/IME and final visual acceptance remain open.',
        'Task counts remain 7 done / 3 in_progress / 18 not_started and G0-2/3/4/5 remain open. Legacy default; RuntimeReady=false, productionBindingAllowed=false, releaseAccepted=false. No commit, push or deployment; official install remains read-only.'])
paths={p for directory in [WINDOWS,STORE,SCENE,Path('E:/workspace/mir3-ui-work/chat-routing-window-set-20261108-v4'),Path('E:/workspace/mir3-ui-work/chat-routing-window-set-20261108-v5')] for p in directory.rglob('*') if p.is_file()}
paths.update(p for p in HERE.rglob('*') if p.is_file() and p!=HERE/'verification.json')
report['evidenceHashes']=[dict(path=portable(p),sha256=sha(p)) for p in sorted(paths)]
write(HERE/'verification.json',report)
print(json.dumps(dict(sourceChanges=len(changes),newlyTracked=len(new),successfulCommands=len(commands),failedAttempts=len(failed),evidenceFiles=len(paths),results=report['results']),indent=2))
