#!/usr/bin/env python3
"""Validate the implementation task graph and saved evidence without running the game."""
import collections
import hashlib
import json
import pathlib
import re
import subprocess

ROOT = pathlib.Path(__file__).resolve().parents[2]
TASK_DIR = ROOT / 'docs/tasks/official-ingame-ui'

def load(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))

def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def advance_source_history(current_sources, supplemental, require):
    """Check a historical snapshot against its explicit transitions, then track it.

    Disk hashes are checked only after the entire history has been replayed. Old
    reports retain their original hashes when a newer verified change supersedes
    a source file; a missing transition still fails at the historical boundary.
    """
    updated = dict(current_sources)
    changes = supplemental['sourceChanges']
    snapshots = supplemental['sourceHashes']
    require('uniqueSupplementalSourceChanges', len({c['path'] for c in changes}) == len(changes))
    require('uniqueSupplementalSourceHashes', len({s['path'] for s in snapshots}) == len(snapshots))
    snapshot_map = {entry['path']: entry['sha256'] for entry in snapshots}
    for change in changes:
        require('supplementalSourceTransitionMatches', change['path'] in updated and
                updated[change['path']] == change['beforeSha256'])
        require('supplementalTransitionHasSnapshot', snapshot_map.get(change['path']) == change['afterSha256'])
        updated[change['path']] = change['afterSha256']
    for entry in snapshots:
        require('supplementalSnapshotMatchesHistory', entry['path'] not in updated or
                updated[entry['path']] == entry['sha256'])
        updated[entry['path']] = entry['sha256']
    return updated

def main():
    data = load(TASK_DIR / 'tasks.json')
    tasks = data['tasks']
    checks = {}
    def require(name, condition):
        checks[name] = bool(condition)
        if not condition:
            raise ValueError(name)
    by_id = {t['id']: t for t in tasks}
    require('taskCount', len(tasks) == 28)
    require('uniqueIds', len(by_id) == len(tasks))
    require('dependenciesResolve', all(dep in by_id for t in tasks for dep in t['dependsOn']))
    order = []
    while len(order) < len(tasks):
        ready = [t['id'] for t in tasks if t['id'] not in order and all(dep in order for dep in t['dependsOn'])]
        require('dependencyGraphAcyclic', bool(ready))
        order.extend(ready)
    require('gatesResolve', all(g in data['currentInputGates'] for t in tasks for g in t['inputGates']))
    npc_source = (ROOT / 'Client/Scenes/Views/NPCDialog.cs').read_text(encoding='utf-8-sig')
    classes = set(re.findall(r'public\s+(?:sealed\s+)?class\s+(NPC\w+)\s*:\s*DXWindow', npc_source))
    owners = collections.Counter(n for t in tasks for n in t['npcWindows'])
    require('npcClassesMatchCurrentSource', classes == set(owners) and len(classes) == 17)
    require('eachNpcWindowHasExactlyOnePrimaryOwner', all(n == 1 for n in owners.values()))
    cases = set(a for t in tasks for a in t['acceptanceCases'])
    require('allAcceptanceCasesHaveSpecificOwners', cases == {f'A{i:02}' for i in range(1, 21)})
    require('sourcePlanHashMatches', sha(ROOT / data['sourcePlan']) == data['sourcePlanSha256'])
    current_head = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip()
    require('baselineCommitExists', subprocess.run(['git', 'cat-file', '-e', data['baselineCommit'] + '^{commit}'], cwd=ROOT, capture_output=True).returncode == 0)
    readme = (TASK_DIR / 'README.md').read_text(encoding='utf-8-sig')
    rows = [line.split('|')[1:-1] for line in readme.splitlines() if re.match(r'^\| UI-\d\d \|', line) and len(line.split('|')) == 8]
    require('markdownTaskCountMatches', len(rows) == 28)
    labels = {'done': '已完成', 'blocked': '阻断', 'not_started': '未开始', 'in_progress': '进行中'}
    for row in rows:
        row = [x.strip() for x in row]
        task = by_id[row[0]]
        require('markdownDependencyTableMatches', row[3] == (', '.join(task['dependsOn']) or '—') and row[4] == (', '.join(task['inputGates']) or '—'))
        require('markdownStatusesMatch', row[5] == labels[task['status']])
    counts = collections.Counter(t['status'] for t in tasks)
    require('statusCountsMatch', all(counts[k] == v for k, v in data['statusCounts'].items()))
    require('allTasksHaveWorkAndAcceptance', all(t['work'] and t['acceptance'] for t in tasks))
    for task in tasks:
        require('evidencePathsUnambiguous', all(p.startswith('docs/design/assets/official-ingame-ui/acceptance/') and '..' not in pathlib.PurePosixPath(p).parts for p in task['evidence']))
        if task['status'] not in ('done', 'blocked'):
            continue
        require('savedResultExists', all((ROOT / p).is_file() for p in task['evidence']))
        result = load(ROOT / task['evidence'][0])
        require('savedResultStatusMatches', result['taskId'] == task['id'] and result['status'] == task['status'])
        require('savedResultEvidenceExists', all((ROOT / p).is_file() for p in result['evidence']))
        if task['status'] == 'done':
            require('doneDependenciesCompleted', all(by_id[d]['status'] == 'done' for d in task['dependsOn']))
            require('doneInputGatesClosed', all(data['currentInputGates'][g] == 'closed' for g in task['inputGates']))
        else:
            require('blockerRecorded', bool(task.get('blocker')))
    report = load(ROOT / data['validationReport'])
    # Historical verification remains immutable. Later isolated work records an explicit
    # hash transition and its own commands, so stale evidence cannot silently claim new code.
    current_sources = {entry['path']: entry['sha256'] for entry in report['sourceHashes']}
    for supplemental_path in data.get('supplementalImplementationReports', []):
        supplemental = load(ROOT / supplemental_path)
        require('supplementalWorkNotReleaseAcceptance', supplemental['releaseAccepted'] is False)
        current_sources = advance_source_history(current_sources, supplemental, require)
        require('supplementalCommandsPassed', bool(supplemental['commands']) and all(
            c['result'] == 'pass' and c['exitCode'] == 0 and sha(ROOT / c['log']) == c['logSha256']
            for c in supplemental['commands']))
    require('implementationSourceHashesMatch', all(sha(ROOT / path) == expected for path, expected in current_sources.items()))
    require('evidenceHashesMatch', all(sha(ROOT / p['path']) == p['sha256'] for p in report['evidenceHashes']))
    require('recordedCommandsPassed', all(c['result'] == 'pass' and c['exitCode'] == 0 and (ROOT / c['log']).is_file() for c in report['commands']))
    require('fullGameAcceptanceNotOverclaimed', all(c['result'] == 'not_run' for c in report['gameAcceptanceCases']) and report['releaseBuildId'] is None)
    markdowns = list(TASK_DIR.glob('*.md')) + list((ROOT / 'docs/design/assets/official-ingame-ui/acceptance').rglob('*.md'))
    for md in markdowns:
        text = md.read_text(encoding='utf-8-sig')
        require('noTrailingWhitespace', all(line.rstrip() == line for line in text.splitlines()))
        for target in re.findall(r'(?<!!)\[[^\]]+\]\(([^)]+)\)', text):
            if '://' in target or target.startswith('#'):
                continue
            require('localLinksResolve', (md.parent / target.split('#')[0]).resolve().exists())
    output = dict(checkedAtLocalDate='2026-11-07', documentType='coding-task-and-foundation-evidence-validation', implementationTestsExecuted=True, currentHead=current_head, baselineCommit=data['baselineCommit'], taskCount=len(tasks), statusCounts=data['statusCounts'], checks=checks, topologicalOrder=order, limitations=report['limitations'])
    (TASK_DIR / 'validation.json').write_text(json.dumps(output, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(f"Task validation passed: {len(tasks)} tasks; {dict(counts)}; 17 NPC windows; 20 acceptance cases; evidence hashes match.")

if __name__ == '__main__':
    main()
