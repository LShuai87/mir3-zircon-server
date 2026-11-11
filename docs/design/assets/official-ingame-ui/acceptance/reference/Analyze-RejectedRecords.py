"""Render explicitly unverified two-plane hypotheses; never emit a build manifest."""
import argparse
from collections import Counter
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import struct
import zlib
from PIL import Image, ImageDraw


def analyze(reference, output):
    if output.exists():
        raise ValueError('Choose a new output directory')
    evidence = json.loads((reference / 'runtime-inspection.json').read_text(encoding='utf-8-sig'))
    inputs = []
    for item in evidence['rejectedRecords']:
        raw = Path(item['rawRecordPath']).read_bytes()
        if hashlib.sha256(raw).hexdigest() != item['rawRecordSha256']:
            raise ValueError('Raw record hash mismatch')
        if len(raw) < 16:
            raise ValueError('Truncated record header')
        w, h, x, y, packed, tag = struct.unpack_from('<HHhhII', raw)
        # Deliberately scoped to the five pinned records, not a format decoder.
        if (w, h, x, y, tag) != (24, 44, 0, 0, 4) or len(raw) != packed + 16:
            raise ValueError('Record differs from the studied 24x44 tag=4 case')
        stride = (w * 2 + 3) // 4 * 4
        rgb_bytes = stride * h
        mask_bytes = ((w + 1) // 2) * h
        inflater = zlib.decompressobj()
        data = inflater.decompress(raw[16:], rgb_bytes + mask_bytes + 1)
        if (len(data) != rgb_bytes + mask_bytes or not inflater.eof
                or inflater.unused_data or inflater.unconsumed_tail):
            raise ValueError('Unexpected decompressed payload')
        inputs.append((item, w, h, stride, data[:rgb_bytes], data[rgb_bytes:]))
    output.mkdir(parents=True)
    (output / 'source-evidence.json').write_bytes((reference / 'runtime-inspection.json').read_bytes())
    board = Image.new('RGB', (960, 620), '#383838')
    draw = ImageDraw.Draw(board)
    draw.text((8, 8), 'EXPERIMENT ONLY - two-plane RGB565 / 4-bit mask hypothesis; NOT verified decoding', fill='white')
    records = []
    for column, (item, w, h, stride, rgb, mask) in enumerate(inputs):
        with Image.frombytes('RGB', (w, h), rgb, 'raw', 'BGR;16', stride, -1) as source:
            image = source.convert('RGBA')
        variants = []
        for row, name in enumerate(['rgb-only', 'mask-high-first', 'mask-low-first']):
            with image.copy() as candidate:
                if row:
                    values = [v for byte in mask for v in
                              ((byte >> 4, byte & 15) if row == 1 else (byte & 15, byte >> 4))]
                    with Image.frombytes('L', (w, h), bytes(v * 17 for v in values)) as raw_alpha:
                        with raw_alpha.transpose(Image.Transpose.FLIP_TOP_BOTTOM) as alpha:
                            candidate.putalpha(alpha)
                name = str(item['index']) + '-' + name
                path = output / (name + '.png')
                candidate.save(path)
                variants.append(dict(name=name, path=str(path), sha256=hashlib.sha256(path.read_bytes()).hexdigest()))
                with Image.new('RGBA', (w, h), '#225577') as background:
                    background.alpha_composite(candidate)
                    with background.convert('RGB').resize((w * 4, h * 4), Image.Resampling.NEAREST) as tile:
                        px, py = 12 + column * 188, 50 + row * 185
                        board.paste(tile, (px, py))
                        draw.text((px, py - 16), name, fill='white')
        image.close()
        histogram = Counter(n for byte in mask for n in (byte >> 4, byte & 15))
        records.append(dict(index=item['index'], rawRecordSha256=item['rawRecordSha256'],
                            proposedRgbPlaneBytes=len(rgb), proposedMaskPlaneBytes=len(mask),
                            proposedMaskNibbleHistogram=dict(sorted(histogram.items())), variants=variants))
    board.save(output / 'hypotheses.png')
    board.close()
    result = dict(schemaVersion=1, kind='unverified-record-format-hypotheses',
                  checkedAt=datetime.now(timezone.utc).isoformat(), productionBindingAllowed=False,
                  strictDecoderModified=False, buildable=False, records=records,
                  sourceEvidenceSnapshot=str(output / 'source-evidence.json'),
                  sourceEvidenceSha256=hashlib.sha256((output / 'source-evidence.json').read_bytes()).hexdigest(),
                  toolSha256=hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
                  limitations=['Splitting 2112 + 528 bytes is a hypothesis, not a published format contract.',
                               'Both nibble orders are rendered; no candidate is selected as verified.',
                               'The proposed mask contains intermediate alpha values; do not silently quantize to DXT1.',
                               'Arrow appearance suggests a minimap control, but runtime association and states remain unverified.'])
    (output / 'analysis.json').write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    return result


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--reference', type=Path, default=Path(__file__).resolve().parent)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    result = analyze(args.reference, args.output)
    print('Unverified hypotheses saved for', len(result['records']), 'records; production binding prohibited')
