"""Independently replay every report, command receipt and evidence digest."""
import argparse
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[6]

def load(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))

def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--output', type=Path)
    args = parser.parse_args()
    tasks = load(ROOT / 'docs/tasks/official-ingame-ui/tasks.json')
    paths = [tasks['validationReport'], *tasks['supplementalImplementationReports']]
    current, errors = {}, []
    commands = evidence = transitions = 0
    for index, path in enumerate(paths):
        report = load(ROOT / path)
        snapshots = {s['path']: s['sha256'] for s in report['sourceHashes']}
        if len(snapshots) != len(report['sourceHashes']):
            errors.append(path + ': duplicate source snapshot')
        changes = report.get('sourceChanges', [])
        if len({c['path'] for c in changes}) != len(changes):
            errors.append(path + ': duplicate source transition')
        for change in changes:
            transitions += 1
            if current.get(change['path']) != change['beforeSha256'] or snapshots.get(change['path']) != change['afterSha256']:
                errors.append(path + ': invalid transition ' + change['path'])
            current[change['path']] = change['afterSha256']
        for source, expected in snapshots.items():
            if source in current and current[source] != expected:
                errors.append(path + ': missing transition ' + source)
            current[source] = expected
        if index and report['releaseAccepted'] is not False:
            errors.append(path + ': supplemental release overclaim')
        for command in report['commands']:
            commands += 1
            log = ROOT / command['log']
            if command['result'] != 'pass' or command['exitCode'] != 0 or not log.is_file():
                errors.append(path + ': failed or missing command receipt')
            elif 'logSha256' in command and digest(log) != command['logSha256']:
                errors.append(str(log) + ': command hash mismatch')
        for item in report.get('evidenceHashes', []):
            evidence += 1
            file = ROOT / item['path']
            if not file.is_file() or digest(file) != item['sha256']:
                errors.append(str(file) + ': evidence hash mismatch')
    for path, expected in current.items():
        file = ROOT / path
        if not file.is_file() or digest(file) != expected:
            errors.append(path + ': current source hash mismatch')
    latest = load(ROOT / paths[-1])
    for config, entry in latest['builds'].items():
        directory = ROOT.parent / ('Debug' if config == 'debug' else 'Release') / 'Client'
        if digest(directory / entry['client']['path']) != entry['client']['sha256']:
            errors.append(config + ': current executable hash mismatch')
    tested = load(Path(__file__).parent / 'tested-runtime.json')
    for entry in tested:
        if digest(ROOT.parent / 'Debug/Client' / entry['name']) != entry['sha256']:
            errors.append(entry['name'] + ': tested runtime mismatch')
    for key in ['package', 'candidate']:
        entry = latest[key]
        if digest(Path(entry['path'])) != entry['sha256']:
            errors.append(key + ': external package mismatch')
    if digest(ROOT / tasks['sourcePlan']) != tasks['sourcePlanSha256']:
        errors.append('Historical source plan changed')
    result = dict(reportCount=len(paths), commandReceipts=commands, evidenceDigests=evidence,
                  sourceTransitions=transitions, currentSources=len(current), mismatches=errors,
                  releaseAccepted=False, note='Receipt covers the history at execution time; later attachment of this receipt is checked by a subsequent stdout audit.')
    if args.output:
        args.output.write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(result, indent=2))
    if errors:
        raise SystemExit(1)

if __name__ == '__main__':
    main()
