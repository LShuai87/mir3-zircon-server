"""Verify saved reference integrity; does not validate runtime behavior or close G0."""
import argparse
import hashlib
import json
import struct
from datetime import datetime, timezone
from pathlib import Path

from PIL import Image


def load(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def verify(reference, repeat):
    root = Path(__file__).resolve().parents[6]
    inspection = load(reference / 'runtime-inspection.json')
    external = Path(inspection['externalInspection'])
    inventory = load(external / 'inventory.json')
    checks = []

    def require(name, ok):
        if not ok:
            raise ValueError(name)
        checks.append(name)

    def hashcheck(path, expected):
        require('sha256:' + str(path), sha(path) == expected)

    baseline_path = reference / inspection['sourceBaseline']
    hashcheck(baseline_path, inspection['sourceBaselineSha256'])
    hashcheck(external / 'inventory.json', inspection['inventorySha256'])
    hashcheck(root / inspection['inspector'], inspection['inspectorSha256'])
    decoder = root / 'build/game-ui/Decode-OfficialUi.py'
    hashcheck(decoder, inspection['strictDecoderSha256'])
    require('inventoryToolAndBaseline', inventory['baselineSha256'] == sha(baseline_path)
            and inventory['decoderSha256'] == sha(decoder))
    require('diagnosticOnly', inventory['buildable'] is False
            and inventory['alphaVerified'] is False
            and inspection['buildable'] is False and inspection['alphaVerified'] is False)
    libraries = load(baseline_path)['libraries']
    snapshot = external.parent / 'Data'
    uib = load(reference / 'uib-inspection.json')
    live_source = Path(uib['source'])
    for lib in libraries:
        for extension in ['mix', 'miz']:
            name = lib['name'] + '.' + extension.upper()
            hashcheck(snapshot / name, lib[extension + 'Sha256'])
            # Current installation may acquire records; compare it to its own saved recheck.
            # The diagnostic baseline remains pinned to the earlier snapshot.
    recheck = load(reference / 'live-source-recheck.json')
    hashcheck(reference / recheck['baselinePath'], recheck['baselineSha256'])
    for item in recheck['files']:
        hashcheck(Path(item['snapshotPath']), item['sha256'])
        hashcheck(Path(item['path']), item['sha256'])
    for candidate in recheck['matchingCandidateRecords']:
        paths = {i['name']: Path(i['snapshotPath']) for i in recheck['files']}
        with paths[candidate['library'] + '.MIX'].open('rb') as index_file:
            index_file.seek(24 + candidate['index'] * 4)
            offset = struct.unpack('<I', index_file.read(4))[0]
        require('currentRecordOffset:' + str(candidate['index']), offset == candidate['currentSourceOffset'])
        with paths[candidate['library'] + '.MIZ'].open('rb') as source_file:
            source_file.seek(offset)
            header = source_file.read(16)
            length = struct.unpack_from('<I', header, 8)[0]
            raw = header + source_file.read(length)
        require('currentRecordHash:' + str(candidate['index']), hashlib.sha256(raw).hexdigest() == candidate['rawRecordSha256'])
    records = {}
    decoded = rejected = 0
    for lib in inventory['libraries']:
        require('inventoryLibrary:' + lib['name'], all(lib[k] == next(
            b for b in libraries if b['name'] == lib['name'])[k]
            for k in ['slots', 'present', 'mixSha256', 'mizSha256']))
        for item in lib['items']:
            if item is None:
                continue
            records[(lib['name'], item['index'])] = item
            if item['status'] == 'decoded':
                decoded += 1
                hashcheck(external / item['png'], item['pngSha256'])
            else:
                rejected += 1
    require('inventoryCounts', decoded == inspection['decodedCount'] == inventory['decodedCount']
            and rejected == inspection['rejectedCount'] == inventory['rejectedCount'])
    captures = {c['id']: c for c in load(reference / 'captures/capture-session.json')}
    request = load(reference / 'capture-request.json')
    require('captureIdsUnique', len(captures) == len(request['captures'])
            and len({c['id'] for c in request['captures']}) == len(captures))
    hashcheck(root / request['sourceLibraryBaseline'], request['sourceLibraryBaselineSha256'])
    for capture in request['captures']:
        original = captures[capture['id']]
        for field in ['imageRelativePath', 'imageSha256', 'imagePixelSize', 'viewIds']:
            require('captureLink:' + capture['id'] + ':' + field, capture[field] == original[field])
        path = reference / original['imageRelativePath']
        hashcheck(path, original['imageSha256'])
        with Image.open(path) as image:
            require('captureFormatAndSize:' + capture['id'], image.format == 'JPEG'
                    and image.size == (original['imagePixelSize']['width'], original['imagePixelSize']['height']))
    validation = load(reference / 'capture-validation.json')
    require('viewCoverage', set(validation['coveredViews']) == {v for c in captures.values() for v in c['viewIds']}
            == {v['id'] for v in request['views']})
    for field, report_name, spec_name in [
        ('panelMatching', 'panel-matching.json', 'panel-matching-spec.json'),
        ('supplementalCandidateMatching', 'unresolved-matching.json', 'unresolved-matching-spec.json')]:
        report = load(reference / report_name)
        hashcheck(reference / report_name, inspection[field]['sha256'])
        hashcheck(reference / spec_name, report['specSha256'])
        hashcheck(reference / 'Match-ReferencePanels.py', report['scriptSha256'])
        require('matchingInventory:' + report_name, report['inventorySha256'] == sha(external / 'inventory.json'))
        require('matchingCounts:' + report_name, len(report['matches']) == inspection[field]['matches']
                and len({r['role'] for r in report['matches']}) == inspection[field]['roles'])
        for match in report['matches']:
            require('matchingCapture:' + report_name + ':' + match['role'],
                    match['captureSha256'] == captures[match['captureId']]['imageSha256'])
            require('matchingWinner:' + report_name + ':' + match['role'],
                    match['bestCandidateIndex'] == match['candidates'][0]['index'])
            for candidate in match['candidates']:
                item = records[(candidate['library'], candidate['index'])]
                require('matchingSource:' + report_name + ':' + match['role'] + ':' + str(candidate['index']),
                        all(candidate[k] == item[k] for k in ['sourceOffset', 'width', 'height', 'offsetX', 'offsetY', 'pngSha256']))
    require('repeatByteIdentical', (reference / 'panel-matching.json').read_bytes() == repeat.read_bytes())
    assessment = load(reference / 'unresolved-resource-assessment.json')
    hashcheck(reference / assessment['matchingReport'], assessment['matchingReportSha256'])
    require('candidatesNotApproved', assessment['productionBindingAllowed'] is False
            and all(i['status'] == 'deferred' for i in assessment['items']))
    for item in inspection['rejectedRecords']:
        raw = Path(item['rawRecordPath'])
        hashcheck(raw, item['rawRecordSha256'])
        require('rawRecordLength:' + str(item['index']), raw.stat().st_size == item['rawRecordBytes'])
        with (snapshot / (item['library'] + '.MIZ')).open('rb') as source:
            source.seek(item['sourceOffset'])
            require('rawRecordMatchesSnapshot:' + str(item['index']), source.read(item['rawRecordBytes']) == raw.read_bytes())
    hashcheck(reference / 'uib-inspection.json', inspection['uibInspection']['sha256'])
    for item in uib['files']:
        hashcheck(Path(item['snapshot']), item['sha256'])
        hashcheck(live_source / item['name'], item['sha256'])
    require('uibExcluded', uib['includedInRuntimeAssets'] is False
            and all(i['scopeDecision'].startswith('excluded:') for i in uib['files']))
    return dict(schemaVersion=1, checkedAt=datetime.now(timezone.utc).isoformat(), result='pass',
                scope='saved evidence integrity only; no DPI, alpha, behavior or game acceptance claim',
                sourceRecheckSha256=sha(reference / 'live-source-recheck.json'),
                verifierSha256=sha(Path(__file__)), repeatReport=str(repeat),
                originalJpegCount=len(captures), decodedPngCount=decoded,
                matchingRecords=inspection['panelMatching']['matches'], mappedRoles=inspection['panelMatching']['roles'],
                deferredMatchingRecords=inspection['supplementalCandidateMatching']['matches'],
                preservedRejectedRecords=rejected, uibFiles=len(uib['files']), checks=checks)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--reference', type=Path, default=Path(__file__).resolve().parent)
    parser.add_argument('--repeat', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    result = verify(args.reference.resolve(), args.repeat.resolve())
    with args.output.open('x', encoding='utf-8') as output:
        output.write(json.dumps(result, ensure_ascii=False, indent=2) + '\n')
    print('Reference evidence integrity passed:', len(result['checks']), 'checks;', result['mappedRoles'], 'roles')
