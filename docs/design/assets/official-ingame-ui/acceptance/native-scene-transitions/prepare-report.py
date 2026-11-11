"""Bind native scene-transition checks to immutable source, runtime and evidence history."""
import hashlib, json, re
from pathlib import Path
HERE=Path(__file__).resolve().parent
ROOT=HERE.parents[5]
PREVIOUS=HERE.parent/'chat-persistence-routing/verification.json'
SCENE=Path('E:/workspace/mir3-ui-work/native-scene-transitions-20261108-final-v2')
WINDOWS=Path('E:/workspace/mir3-ui-work/native-scene-window-set-20261108-final-v2')
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
baseline=load(HERE/'source-before.json'); changes=[]; firstTrackedChanges=[]; newlyTracked=[]
for row in baseline:
    rel=row['path']; before=row['sha256']; after=sha(ROOT/rel)
    if sha(HERE/'source-before'/rel)!=before: raise ValueError('Baseline snapshot mismatch '+rel)
    if rel in history:
        if history[rel]!=before: raise ValueError('History baseline mismatch '+rel)
        if before!=after: changes.append(dict(path=rel,beforeSha256=before,afterSha256=after))
    else:
        newlyTracked.append(dict(path=rel,sha256=after,createdThisRound=False))
        if before!=after: firstTrackedChanges.append(dict(path=rel,beforeSha256=before,afterSha256=after))
new='tests/game-ui/OfficialUiNativeSceneTransitionTests.cs'
reference='Server/Envir/SEnvir.cs'
for rel in [new,reference]:
    if rel in history: raise ValueError('Unexpected existing source tracking '+rel)
    newlyTracked.append(dict(path=rel,sha256=sha(ROOT/rel),createdThisRound=rel==new))
commands=[]; failed=[]
reasons={
    'full-scene.log':'Harness compilation accessed protected DXControl.Controls; corrected to test-only reflection without changing production visibility.',
    'full-scene-v2.log':'Real Legacy failure: native disconnect modal close returns to login, disposal restores an already disposed editor and throws NullReferenceException.',
    'full-scene-v3.log':'First edit added the helper but CRLF anchors left its call sites unchanged; cleanup again reached the original failure. Both call sites were then corrected and original-body diagnostics were added.',
    'full-scene-v5.log':'Expanded serialized disconnect coverage reproduced ArgumentOutOfRangeException for server-emitted AnotherUserPassword; native handling was then added.'}
for p in sorted(HERE.glob('*.receipt.json')):
    receipt=load(p); log=Path(receipt['log'])
    item=dict(command=receipt['command'],result='pass' if receipt['exitCode']==0 else 'fail',exitCode=receipt['exitCode'],log=portable(log),logSha256=sha(log))
    if receipt['exitCode']:
        item['explanation']=reasons.get(log.name,'UNEXPECTED'); failed.append(item)
    else: commands.append(item)
if {Path(row['log']).name for row in failed}!=set(reasons): raise ValueError('Unexpected failed attempts')
text=(HERE/'full-scene-final-v2.log').read_text('utf-8',errors='replace')
match=re.search(r'Full-scene offline integration: (\d+) checks passed; (\d+) real constructors, (\d+) presented frames, (\d+) explicit Device.Reset calls; (\d+) identical panel pixels',text)
if not match: raise ValueError('Missing final full-scene totals')
sceneChecks,constructors,frames,resets,pixels=map(int,match.groups())
if (sceneChecks,constructors,frames,resets,pixels)!=(911,25,39,9,690512): raise ValueError('Unexpected final scene totals')
windowChecks=int(re.search(r'Window-set integration: (\d+) checks passed',(HERE/'window-set-final.log').read_text('utf-8',errors='replace')).group(1))
if windowChecks!=168890: raise ValueError('Unexpected window integration total')
rows=(SCENE/'native-transition-results.txt').read_text('utf-8-sig').splitlines()
if len(rows)!=78 or sum(s.startswith('saved-chat-restored|') for s in rows)!=10: raise ValueError('Unexpected transition evidence')
for evidence in [SCENE,WINDOWS]:
    for row in load(evidence/'tested-runtime.json'):
        if sha(ROOT.parent/'Debug/Client'/row['name'])!=row['sha256']: raise ValueError('Tested runtime drift')
write(HERE/'tested-runtime.json',load(SCENE/'tested-runtime.json'))
builds={}
for config in ['debug','release']:
    doc=load(HERE/(config+'-build.json')); binary=ROOT.parent/('Debug' if config=='debug' else 'Release')/'Client/Zircon.exe'
    if sha(binary)!=doc['client']['sha256'] or doc['catalog']['runtimeReady'] is not False: raise ValueError('Build identity/gate mismatch')
    builds[config]=dict(buildId=doc['buildId'],client=doc['client'])
report=dict(schemaVersion=1,kind='official-native-scene-transitions-verification',checkedAtLocalDate='2026-11-08',referenceResolution=[1024,768],releaseAccepted=False,
    results=dict(fullSceneChecks=sceneChecks,windowSetChecks=windowChecks,realGameConstructors=constructors,presentedFrames=frames,explicitDeviceResets=resets,identicalPanelPixels=pixels,
        nativeGameEntriesViaPacket=22,serializedLoopbackPackets=64,transitionEvidenceRows=len(rows),styles=['Legacy','OfficialV1'],inGameDisconnectReasons=8,
        nativeLoginDispatchVerified=True,nativeGameLogoutDispatchVerified=True,nativeSelectLogoutDispatchVerified=True,nativeStartGameDispatchVerified=True,
        nativeReturnToLoginVerified=True,loopbackSocketEofVerified=True,modalFocusLifecycleVerified=True,realAccountLoginVerified=False,realServerReconnectVerified=False,liveImeVerified=False),
    implementation=[
        'DXMessageBox restores its prior editor through one helper for hiding and disposal. It clears HiddenBox before callbacks and rejects disposed native editors or disposing ancestors; normal live-editor focus restoration remains intact.',
        'CConnection now handles AnotherUserPassword, which Server/Envir/SEnvir.cs emits during account security handling, with the original native modal and ReturnToLogin callback. Server authority and account security logic are unchanged.',
        'Window runner records the exact SHA256 identities of the three tested runtime files alongside successful evidence.',
        'Full-scene runner compiles a new partial harness that uses actual TCP receive/deserialization and BaseConnection.Process dispatch, normal LoginScene/SelectScene/GameScene constructors, native disconnect modal buttons and CEnvir.ReturnToLogin.'
    ],
    verifiedBehaviors=[
        'Both Legacy and candidate OfficialV1 traverse serialized login/start-game/game-logout/select-logout, direct ReturnToLogin and idempotent repeated ReturnToLogin; all data and peers are synthetic and isolated.',
        'A real loopback peer EOF reaches native disconnect processing; the timeout is advanced explicitly to avoid waiting two seconds. The scene remains alive until native acknowledgement.',
        'All eight in-game disconnect enum reasons use native modal acknowledgement and return to login; ServerClosing exercises the close button, other reasons exercise OK. WrongVersion belongs to login/version handling and is outside this in-game matrix.',
        'Each retired game scene releases its controls, original WinForms editor, chat pages, Official adapters, startup borrower and static topology listener. The application owner and verified package remain alive for subsequent scene entry.',
        'Ten Official reentries restore the saved tab name and system filter, use a fresh editor with an initially empty draft and discard prior-scene history; each new system message is delivered once.',
        'Native modal OK/close/hide retain the live editor HWND/draft/selection. Disposing a previously hidden modal cannot steal newer focus. Modal disposal rejects an already disposed editor and a live editor under a disposing ancestor.',
        'Existing complete scene coverage still renders four character pages, six resolutions, actual device resets, original item/chat/input retention and identical panel pixels. Final Debug binary also passes 168890 window/startup/chat integration checks.',
        'Debug and Release compile and match new inspection manifests; source plan, package, task states and release gates are preserved.'
    ],
    sourceChanges=changes,firstTrackedSourceChanges=firstTrackedChanges,newlyTrackedSources=newlyTracked,
    sourceHashes=[dict(path=rel,sha256=sha(ROOT/rel)) for rel in [*[r['path'] for r in baseline],new,reference]],
    commands=commands,failedAttempts=failed,regressionBaselineRuntime=dict(report=portable(PREVIOUS),builds=previous['builds'],testedRuntime=load(PREVIOUS.parent/'tested-runtime.json')),
    builds=builds,runtimeRebuiltThisTurn=True,package=previous['package'],candidate=previous['candidate'],sourcePlanSha256=tasks['sourcePlanSha256'],
    limitations=[
        'The actual client scene and packet paths run on loopback peers with synthetic account/world data and existing legacy resources. No real account authentication, remote game server reconnect, server transaction or final visual acceptance is claimed.',
        'The disconnect matrix covers eight in-game reasons. Login-only WrongVersion/closing the process is outside it; no unsupported reason is fabricated. Socket EOF timeout advancement exercises the native branch without validating wall-clock timeout timing.',
        'Device.Reset is real; hardware device-loss, physical monitor/fonts and live Chinese IME candidate/composition behavior remain unverified.',
        'Real storage/service transactions, remaining UI field/equipment-slot semantics and final user visual acceptance remain open.',
        'Task counts remain 7 done / 3 in_progress / 18 not_started; G0-2/3/4/5 remain open. Legacy default; RuntimeReady=false, productionBindingAllowed=false, releaseAccepted=false. NPC card and secondary popup styles remain native. No commit/push/deployment; official installation remains read-only.'
    ])
directories=[SCENE,WINDOWS,Path('E:/workspace/mir3-ui-work/native-scene-window-set-20261108-final'),Path('E:/workspace/mir3-ui-work/native-scene-transitions-20261108-v2'),Path('E:/workspace/mir3-ui-work/native-scene-transitions-20261108-v3'),Path('E:/workspace/mir3-ui-work/native-scene-transitions-20261108-v4'),Path('E:/workspace/mir3-ui-work/native-scene-transitions-20261108-v5'),Path('E:/workspace/mir3-ui-work/native-scene-transitions-20261108-final')]
paths={p for directory in directories for p in directory.rglob('*') if p.is_file()}
paths.update(p for p in HERE.rglob('*') if p.is_file() and p!=HERE/'verification.json')
report['evidenceHashes']=[dict(path=portable(p),sha256=sha(p)) for p in sorted(paths)]
write(HERE/'verification.json',report)
print(json.dumps(dict(sourceChanges=len(changes),firstTrackedChanges=len(firstTrackedChanges),newlyTracked=len(newlyTracked),successfulCommands=len(commands),failedAttempts=len(failed),evidenceFiles=len(paths),results=report['results']),indent=2))
