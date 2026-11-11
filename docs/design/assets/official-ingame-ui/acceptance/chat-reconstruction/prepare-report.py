"""Archive the reproduced chat reconstruction defect and its bounded lifecycle fix."""
import hashlib, json, re
from pathlib import Path
HERE=Path(__file__).resolve().parent
ROOT=HERE.parents[5]
PREVIOUS=ROOT/'docs/design/assets/official-ingame-ui/acceptance/layout-persistence-reentry/verification.json'
FINAL=Path('E:/workspace/mir3-ui-work/chat-reconstruction-20261108-v2')
SCENE=Path('E:/workspace/mir3-ui-work/chat-reconstruction-full-scene-20261108-v1')
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
commands=[]; failed=[]
for p in sorted(HERE.glob('*.receipt.json')):
    receipt=load(p); log=Path(receipt['log'])
    item=dict(command=receipt['command'],result='pass' if receipt['exitCode']==0 else 'fail',exitCode=receipt['exitCode'],log=portable(log),logSha256=sha(log))
    (commands if receipt['exitCode']==0 else failed).append(item)
if len(failed)!=1 or failed[0]['log']!=portable(HERE/'reproduction.log'): raise ValueError('Unexpected failed attempts')
if 'Rebuilt native input lost Official adapter:' not in (HERE/'reproduction.log').read_text('utf-8',errors='replace'): raise ValueError('Baseline runtime failure missing')
rows=(FINAL/'chat-reconstruction-results.txt').read_text('utf-8-sig').splitlines()
if len(rows)!=65 or len(set(rows))!=65: raise ValueError('Chat lifecycle matrix incomplete')
if sum(r.startswith('OfficialV1|') for r in rows)!=54 or sum(r.startswith('Legacy|') for r in rows)!=9 or sum(r.startswith('fault|') for r in rows)!=2: raise ValueError('Unexpected matrix distribution')
checks=int(re.search(r'Window-set integration: (\d+) checks passed',(HERE/'window-set-final.log').read_text('utf-8',errors='replace')).group(1))
builds={}
for config in ['debug','release']:
    doc=load(HERE/(config+'-build.json'))
    binary=ROOT.parent/('Debug' if config=='debug' else 'Release')/'Client/Zircon.exe'
    if sha(binary)!=doc['client']['sha256'] or doc['catalog']['runtimeReady'] is not False: raise ValueError('Build identity/gate mismatch')
    builds[config]=dict(buildId=doc['buildId'],client=doc['client'])
runtime=[dict(name=name,sha256=sha(ROOT.parent/'Debug/Client'/name)) for name in ['Zircon.exe','Library.dll','SlimDX.dll']]
write(HERE/'tested-runtime.json',runtime)
report=dict(schemaVersion=1,kind='native-chat-explicit-reconstruction-lifecycle-verification',checkedAtLocalDate='2026-11-08',referenceResolution=[1024,768],releaseAccepted=False,
    results=dict(windowSetChecks=checks,chatGeometryChecks=940,fullSceneChecks=180,nativeOperations=18,officialResolutionRows=54,legacyOperationRows=9,faultCases=2,disposition='Reproduced stale borrowed input after native reset; fixed reset/load transaction lifecycle.'),
    fixes=[dict(rootCause='Native reset or empty load replaces ChatTextBox while Official adapter still borrows the disposed input; successful load exposes half-built group topology to the old adapter.',change='Detach adapters before explicit native reconstruction; install against the current input/groups after it completes; refresh WinForms bounds once; preserve existing scene fault/package ownership containment.')],
    verifiedBehaviors=[
        'Native reset/reload buttons and Yes/No confirmation handlers run unchanged native dialogs; cancel preserves input/tabs.',
        'Three repeated cycles per style cover reset, saved two-page reload and resolution without saved settings. Reset/empty reload replace native input; successful reload retains it.',
        'Saved native page names, filters, transparency, alert and selected page restore; reset/load do not write native Users.db.',
        'Official current input and history dock correctly at six resolutions; resize retains HWND, draft, selection, messages, tabs and one topology subscription; temporary empty groups do not survive.',
        'Injected native action failure and resize reentry require restart without releasing the borrowed package; scene disposal releases all tabs/adapters/listeners.',
        'Three normal GameScene constructors and seven explicit Device.Reset calls pass the existing offline full-scene suite.'],
    sourceChanges=changes,newlyTrackedSources=newlyTracked,sourceHashes=[dict(path=r['path'],sha256=sha(ROOT/r['path'])) for r in baseline],
    commands=commands,failedAttempts=failed,regressionBaselineRuntime=dict(report=portable(PREVIOUS),builds=previous['builds'],testedRuntime=load(PREVIOUS.parent/'tested-runtime.json')),
    builds=builds,runtimeRebuiltThisTurn=True,package=previous['package'],candidate=previous['candidate'],sourcePlanSha256=tasks['sourcePlanSha256'],
    limitations=[
        'Explicit reset/load intentionally rebuild native tabs and may clear their messages as before. Ordinary Official resize does not rebuild or clear chat.',
        'The 65-row new lifecycle/fault matrix uses SceneFixture with real native config/options/input/tab controls, DB serialization, buttons and confirmation handlers; socket/world construction is bypassed. The separate 180-check suite uses normal constructors and synthetic data.',
        'Style-isolated chat saving is still open: SaveChatTabs currently shares native chat DB records. This report does not claim Official save-all preserves Legacy chat geometry or that floating Official geometry persists across relogin.',
        'No networking, message rendering, filter schema, NPC cards, secondary popup styles, display settings, resource package or server rules changed.',
        'Live return-to-login/reconnect, real storage/services, remaining field/equipment semantics, hardware/fonts/IME and final visual acceptance remain open.',
        'Legacy remains default; RuntimeReady=false, productionBindingAllowed=false, releaseAccepted=false. No commit, push or deployment; official installation remains read-only.'])
paths={p for directory in [FINAL,SCENE,Path('E:/workspace/mir3-ui-work/chat-reconstruction-20261108-v1')] for p in directory.rglob('*') if p.is_file()}
paths.update(p for p in HERE.rglob('*') if p.is_file() and p!=HERE/'verification.json')
report['evidenceHashes']=[dict(path=portable(p),sha256=sha(p)) for p in sorted(paths)]
write(HERE/'verification.json',report)
print(json.dumps(dict(sourceChanges=len(changes),newlyTracked=len(newlyTracked),successfulCommands=len(commands),evidenceFiles=len(paths),results=report['results']),indent=2))
