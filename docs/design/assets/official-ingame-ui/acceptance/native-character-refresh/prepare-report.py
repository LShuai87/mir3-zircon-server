"""Bind native character refresh checks to source, tested binaries and immutable evidence."""
import hashlib, json, re
from collections import Counter
from pathlib import Path
HERE=Path(__file__).resolve().parent
ROOT=HERE.parents[5]
PREVIOUS=HERE.parent/'native-scene-transitions/verification.json'
SCENE=Path('E:/workspace/mir3-ui-work/native-character-refresh-20261108-final')
ATTEMPT=Path('E:/workspace/mir3-ui-work/native-character-refresh-20261108-v1')
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
new='tests/game-ui/OfficialUiNativeCharacterRefreshTests.cs'
if new in history: raise ValueError('Unexpected prior tracking of new test')
newlyTracked.append(dict(path=new,sha256=sha(ROOT/new),createdThisRound=True))
commands=[]; failed=[]
for p in sorted(HERE.glob('*.receipt.json')):
    receipt=load(p); log=Path(receipt['log'])
    item=dict(command=receipt['command'],result='pass' if receipt['exitCode']==0 else 'fail',exitCode=receipt['exitCode'],log=portable(log),logSha256=sha(log))
    if receipt['exitCode']:
        if log.name!='full-scene.log' or 'Get-FileHash' not in log.read_text('utf-8',errors='replace'): raise ValueError('Unexpected failed command')
        item['explanation']='All 4328 client checks passed, but Windows PowerShell lacked Get-FileHash when saving evidence. The test runner now uses an explicit SHA256 stream; a fresh complete run passed and saved binary hashes. The final fixture also preserves valid HP/MP/capacities when testing omitted attribute clearing.'
        failed.append(item)
    else: commands.append(item)
if len(failed)!=1: raise ValueError('Missing initial runner environment failure')
text=(HERE/'full-scene-final.log').read_text('utf-8',errors='replace')
match=re.search(r'Full-scene offline integration: (\d+) checks passed; (\d+) real constructors, (\d+) presented frames, (\d+) explicit Device.Reset calls; (\d+) identical panel pixels',text)
if not match: raise ValueError('Missing final full-scene totals')
sceneChecks,constructors,frames,resets,pixels=map(int,match.groups())
summary=dict(line.split('=',1) for line in (SCENE/'native-character-refresh-summary.txt').read_text('utf-8-sig').splitlines())
if {k:summary[k] for k in ['checkpoints','serializedPackets','legacyParityFields','originalAttributeLabels','fieldsPerCheckpoint','style','releaseAccepted']} != dict(checkpoints='34',serializedPackets='52',legacyParityFields='1156',originalAttributeLabels='57',fieldsPerCheckpoint='68',style='official',releaseAccepted='false'): raise ValueError('Refresh summary mismatch')
rows=(SCENE/'native-character-refresh-fields.txt').read_text('utf-8-sig').splitlines()
if rows[0]!='style|checkpoint|field|text|foregroundArgb|iconArgb': raise ValueError('Wrong field evidence header')
fieldRows=[line.split('|') for line in rows[1:]]
if any(len(row)!=6 for row in fieldRows): raise ValueError('Malformed refresh row')
groups=Counter((row[0],row[1]) for row in fieldRows)
if len(groups)!=34 or set(groups.values())!={68}: raise ValueError('Incomplete field checkpoint matrix')
legacy={tuple(row[1:3]):row[3:] for row in fieldRows if row[0]=='legacy'}
official={tuple(row[1:3]):row[3:] for row in fieldRows if row[0]=='official'}
if legacy!=official or len(official)!=1156: raise ValueError('Independent field parity mismatch')
for row in load(SCENE/'tested-runtime.json'):
    if sha(ROOT.parent/'Debug/Client'/row['name'])!=row['sha256']: raise ValueError('Tested runtime drift')
write(HERE/'tested-runtime.json',load(SCENE/'tested-runtime.json'))
if load(HERE/'tested-runtime.json')!=load(PREVIOUS.parent/'tested-runtime.json'): raise ValueError('Unexpected production runtime change')
for config,build in previous['builds'].items():
    if sha(ROOT.parent/('Debug' if config=='debug' else 'Release')/'Client'/build['client']['path'])!=build['client']['sha256']: raise ValueError('Prior runtime changed')
if sha(ROOT/tasks['sourcePlan'])!=tasks['sourcePlanSha256']: raise ValueError('Immutable plan changed')
report=dict(schemaVersion=1,kind='official-native-character-refresh-verification',checkedAtLocalDate='2026-11-08',referenceResolution=[1024,768],releaseAccepted=False,
    results=dict(fullSceneChecks=sceneChecks,realGameConstructors=constructors,presentedFrames=frames,explicitDeviceResets=resets,identicalPanelPixels=pixels,
        nativeCharacterCheckpoints=34,checkpointsPerStyle=17,serializedCharacterSuitePackets=52,originalAttributeLabels=57,parityFieldsPerCheckpoint=68,legacyParityFieldComparisons=1156,
        styles=['Legacy','OfficialV1'],nativeAttributeReplacementVerified=True,nativeResistanceSignColorAndIconsVerified=True,nativeMaxHealthManaAndClampSequenceVerified=True,
        nativeWeightOverloadRecoveryVerified=True,nativeHermitClearingVerified=True,nativeLevelExperienceGainLossAndCapVerified=True,hintOnlyExperienceRefreshVerified=True,
        originalControlAndCellIdentityVerified=True,nativeChatEditorRetentionVerified=True,productionSourceChanged=False,realServerBehaviorVerified=False,finalVisualAcceptance=False),
    implementation=[
        'Add a partial real-scene harness driven by serialized loopback StatsUpdate, WeightUpdate, HealthChanged, ManaChanged, LevelChanged and GainedExperience packets, with normal login/start/logout packets and original client dispatch.',
        'Compare all 57 native attribute labels, six named character text labels, inventory weight and four HUD strings at 17 checkpoints per style, including text, foreground and tagged element-icon colors. Independently assert sign/range/weight/HP/MP/experience boundary outcomes.',
        'Verify four Official character readouts and full-value hints mirror their native HUD sources after updates, including an experience raw hint change while its rounded percentage string stays identical.',
        'The full-scene runner compiles the new partial and saves tested SHA256 identities with .NET streams to work in Windows PowerShell without the Get-FileHash cmdlet.'
    ],
    verifiedBehaviors=[
        'Positive resistance -> negative -> zero -> positive resets old text, foreground and icon colors in all original attack/resistance groups. Omitted base/Hermit fields in fresh serialized dictionaries clear old values without mutating the previous dictionary.',
        'StatsUpdate -> WeightUpdate -> HP/MP deltas reproduces the server RefreshStats enqueue order. A transient current value above its newly reduced maximum is shown until the native clamp delta arrives; the UI does not invent server state.',
        'Inventory/wear/hand weight capacities refresh through their native WeightUpdate, including overloaded red -> exactly at capacity white -> normal values.',
        'Hermit attacks, other Hermit attributes and remaining points update and clear through their original controls. Original label/item-cell identity and native chat HWND/draft/selection survive every checkpoint.',
        'Level and experience packets exercise gain, loss, a percentage-unchanged raw hint change, the no-next-level raw-value branch, and resumption of percentage display.',
        'The scene renders character/stats/Hermit pages for both styles and the existing full-scene reset/reentry/native disconnect regressions continue to pass. Native logout releases each new suite scene.',
        'No production source or game binary was changed. Debug/Release executable and three tested Debug runtime identities match the prior finalized build. Legacy default and all release gates remain unchanged.'
    ],
    sourceChanges=changes,firstTrackedSourceChanges=firstTrackedChanges,newlyTrackedSources=newlyTracked,
    sourceHashes=[dict(path=rel,sha256=sha(ROOT/rel)) for rel in [*[r['path'] for r in baseline],new]],
    commands=commands,failedAttempts=failed,regressionBaselineRuntime=dict(report=portable(PREVIOUS),builds=previous['builds'],testedRuntime=load(PREVIOUS.parent/'tested-runtime.json')),
    builds=previous['builds'],runtimeRebuiltThisTurn=False,package=previous['package'],candidate=previous['candidate'],sourcePlanSha256=tasks['sourcePlanSha256'],
    limitations=[
        'Loopback peers use synthetic packets and isolated account/world data. Packet production by an actual server, item transactions, reconnect to a real account and final visual acceptance remain unverified.',
        'This verifies 57 native attribute labels and four mirrors; it does not establish unsupported official field semantics or proof for every equipment-slot mapping. Labels such as character/guild identity are compared but their server-driven rename flow is not exercised here.',
        'The no-next-level branch is a deterministic client boundary fixture based on ExperienceList.Count; it is not evidence that a live server permits this level.',
        'Four character mirrors retain native text/hints; truncation visuals, physical fonts and live IME composition are outside this packet test. Hardware device-loss remains unverified.',
        'Task counts remain 7 done / 3 in_progress / 18 not_started; G0-2/3/4/5 remain open. RuntimeReady=false, productionBindingAllowed=false, releaseAccepted=false. NPC interaction cards and secondary popup styles remain native. Official installation is read-only; no commit/push/deployment.'
    ])
paths={p for directory in [SCENE,ATTEMPT] for p in directory.rglob('*') if p.is_file()}
paths.update(p for p in HERE.rglob('*') if p.is_file() and p!=HERE/'verification.json')
report['evidenceHashes']=[dict(path=portable(p),sha256=sha(p)) for p in sorted(paths)]
write(HERE/'verification.json',report)
print(json.dumps(dict(sourceChanges=len(changes),firstTrackedChanges=len(firstTrackedChanges),newlyTracked=len(newlyTracked),successfulCommands=len(commands),failedAttempts=len(failed),evidenceFiles=len(paths),results=report['results']),indent=2))
