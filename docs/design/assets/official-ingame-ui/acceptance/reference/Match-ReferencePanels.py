"""Reproduce local border matching; this is visual evidence, never a layout or alpha specification."""
import argparse
import hashlib
import json
from pathlib import Path
from PIL import Image


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def match(inspection, reference, spec_path):
    spec = json.loads(spec_path.read_text(encoding='utf-8'))
    inventory_path = inspection / 'inventory.json'
    if sha(inventory_path) != spec['inventorySha256']:
        raise ValueError('Inspection inventory hash mismatch')
    inventory = json.loads(inventory_path.read_text(encoding='utf-8'))
    captures = {x['id']: x for x in json.loads((reference / 'captures/capture-session.json').read_text(encoding='utf-8'))}
    libraries = {x['name']: x for x in inventory['libraries']}
    results = []
    for request in spec['matches']:
        capture = captures[request['captureId']]
        capture_path = reference / capture['imageRelativePath']
        if sha(capture_path) != capture['imageSha256']:
            raise ValueError('Capture hash mismatch: ' + capture['id'])
        candidates = []
        with Image.open(capture_path) as original:
            target = original.convert('RGB')
        target_pixels = target.load()
        for index in request['indices']:
            record = libraries[request['library']]['items'][index]
            if not record or record['status'] != 'decoded':
                raise ValueError('Requested source record was not decoded')
            source_path = inspection / record['png']
            if sha(source_path) != record['pngSha256']:
                raise ValueError('Source PNG hash mismatch')
            with Image.open(source_path) as original:
                source = original.convert('RGB')
            w, h = source.size
            edge, step = spec['borderPixels'], spec['sampleStep']
            points = []
            for y in range(0, h, step):
                for x in range(0, w, step):
                    if request.get('sampling', 'border') == 'border' and not (x < edge or x >= w-edge or y < spec['topPixels'] or y >= h-edge):
                        continue
                    if request.get('sampling', 'border') == 'border' and x > w-spec['excludeCloseWidth'] and y < spec['excludeCloseHeight']:
                        continue
                    if any(rx <= x < rx+rw and ry <= y < ry+rh for rx, ry, rw, rh in request.get('excludeRects', [])):
                        continue
                    color = source.getpixel((x, y))
                    if max(color) > request.get('minSourceMaxChannel', spec['minSourceMaxChannel']):
                        points.append((x, y, color))
            if not points:
                raise ValueError('No matching samples')
            scores = []
            for oy in range(*request['yRange']):
                for ox in range(*request['xRange']):
                    if ox < 0 or oy < 0 or ox+w > target.width or oy+h > target.height:
                        continue
                    error = sum(sum(abs(c[i]-target_pixels[x+ox, y+oy][i]) for i in range(3))
                                for x, y, c in points)/(3*len(points))
                    scores.append((error, ox, oy))
            if not scores:
                raise ValueError('Search contains no valid placement')
            scores.sort()
            candidates.append(dict(library=request['library'], index=index,
                sourceOffset=record['sourceOffset'], width=w, height=h,
                offsetX=record['offsetX'], offsetY=record['offsetY'],
                pngSha256=record['pngSha256'], samples=len(points), sampling=request.get('sampling', 'border'),
                bestPlacements=[dict(meanAbsoluteChannelError=round(e, 6), x=x, y=y)
                                for e, x, y in scores[:3]]))
            source.close()
        target.close()
        candidates.sort(key=lambda c: c['bestPlacements'][0]['meanAbsoluteChannelError'])
        results.append(dict(role=request['role'], captureId=capture['id'],
            captureSha256=capture['imageSha256'], candidates=candidates,
            bestCandidateIndex=candidates[0]['index']))
    return dict(schemaVersion=1, kind='official-ui-local-border-matching',
        coordinateSpace='original screenshot pixels including title bar; ranges are half-open',
        metric='mean absolute RGB channel error (0..255); no scaling or alpha fitting',
        method='visually selected candidates and local placement search on sampled source borders or artwork',
        limitations=[
            'Local search supports visual identification; it does not prove global uniqueness.',
            'JPEG compression and live text/controls affect the error; no exact pixel identity claimed.',
            'Artwork samples omit dark pixels and specified occlusions; this is not a transparency rule.',
            'Source PNGs are opaque diagnostic renders; transparency and blend semantics remain unverified.',
            'Placements locate source bitmaps; they are not client coordinates, final layouts or hitboxes.',
            'Candidates have different dimensions/sample counts; errors are descriptive, not calibrated confidence.',
            'Current snapshot is pinned; exact historical source hashes at each capture are not proven.'
        ], specSha256=sha(spec_path), inventorySha256=sha(inventory_path),
        scriptSha256=sha(Path(__file__)), matches=results)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--inspection', type=Path, required=True)
    parser.add_argument('--reference', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--spec', type=Path, help='Optional matching specification; defaults to panel-matching-spec.json in reference')
    args = parser.parse_args()
    result = match(args.inspection, args.reference, args.spec or args.reference / 'panel-matching-spec.json')
    with args.output.open('x', encoding='utf-8') as output:
        output.write(json.dumps(result, ensure_ascii=False, indent=2)+'\n')
    print(json.dumps([dict(role=r['role'], capture=r['captureId'], index=r['bestCandidateIndex'],
        best=r['candidates'][0]['bestPlacements'][0]) for r in result['matches']]))
