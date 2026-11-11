#!/usr/bin/env python3
"""Inspect pinned MIX/MIZ files, preserving failures as explicit non-buildable records.

This is a diagnostic inventory, not a decoded-reference or runtime package manifest.
The strict converter remains the only full-library conversion entry point.
"""
import argparse
import importlib.util
import json
import os
from pathlib import Path
import shutil
import struct
import tempfile

MODULE = Path(__file__).with_name('Decode-OfficialUi.py')
SPEC = importlib.util.spec_from_file_location('official_ui_strict_decoder', MODULE)
decoder = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(decoder)


def inspect_libraries(source, output, baseline):
    source, output = decoder.validate_paths(source, output)
    config = decoder.load_baseline(baseline)
    output.parent.mkdir(parents=True, exist_ok=True)
    staging = Path(tempfile.mkdtemp(prefix='.' + output.name + '-', dir=output.parent))
    manifest = dict(schemaVersion=1, kind='official-ui-diagnostic-inventory',
                    buildable=False, alphaVerified=False,
                    baselineSha256=decoder.sha256(Path(baseline).read_bytes()),
                    decoderSha256=decoder.sha256(MODULE.read_bytes()), libraries=[])
    total_pixels = 0
    try:
        for lib in config['libraries']:
            name = lib['name']
            mix = decoder.read_pinned(source / (name + '.MIX'), lib['mixSha256'])
            miz = decoder.read_pinned(source / (name + '.MIZ'), lib['mizSha256'])
            if len(mix) < 24 or mix[:16] != decoder.MIX_SIGNATURE:
                raise decoder.DecodeError(name + ': invalid MIX header')
            count = struct.unpack_from('<I', mix, 16)[0]
            if count != lib['slots'] or len(mix) != 24 + count * 4:
                raise decoder.DecodeError(name + ': MIX count/length mismatch')
            offsets = struct.unpack_from('<' + str(count) + 'I', mix, 24)
            if sum(o != 0 for o in offsets) != lib['present']:
                raise decoder.DecodeError(name + ': MIX present count mismatch')
            entry = dict(lib, items=[], decodedCount=0, rejectedCount=0)
            manifest['libraries'].append(entry)
            (staging / name).mkdir()
            for index, offset in enumerate(offsets):
                if offset == 0:
                    entry['items'].append(None)
                    continue
                record = dict(index=index, sourceOffset=offset, status='rejected')
                if 0 <= offset <= len(miz) - 16:
                    w, h, x, y, packed, tag = struct.unpack_from('<HHhhII', miz, offset)
                    record.update(width=w, height=h, offsetX=x, offsetY=y,
                                  packedBytes=packed, tag=tag)
                try:
                    image, metadata = decoder.decode_image(miz, offset)
                except decoder.DecodeError as error:
                    record['error'] = str(error)
                    entry['rejectedCount'] += 1
                    entry['items'].append(record)
                    continue
                with image:
                    total_pixels += image.width * image.height
                    if total_pixels > decoder.MAX_TOTAL_PIXELS:
                        raise decoder.DecodeError('Total decoded pixel budget exceeded')
                    relative = name + '/' + str(index) + '.png'
                    image.save(staging / relative, format='PNG', compress_level=9)
                    record.update(metadata, status='decoded', png=relative,
                                  pngSha256=decoder.sha256((staging / relative).read_bytes()),
                                  rgbaSha256=decoder.sha256(image.tobytes()),
                                  alphaMode='opaque', alphaVerified=False)
                entry['items'].append(record)
                entry['decodedCount'] += 1
            decoder.read_pinned(source / (name + '.MIX'), lib['mixSha256'])
            decoder.read_pinned(source / (name + '.MIZ'), lib['mizSha256'])
        manifest['decodedCount'] = sum(x['decodedCount'] for x in manifest['libraries'])
        manifest['rejectedCount'] = sum(x['rejectedCount'] for x in manifest['libraries'])
        (staging / 'inventory.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
        if output.exists():
            raise decoder.DecodeError('Output appeared during inspection; refusing to replace it')
        os.rename(staging, output)
    finally:
        if staging.exists():
            shutil.rmtree(staging)
    return manifest


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', required=True, type=Path)
    parser.add_argument('--output', required=True, type=Path)
    parser.add_argument('--baseline', required=True, type=Path)
    args = parser.parse_args()
    try:
        result = inspect_libraries(args.source, args.output, args.baseline)
    except (ValueError, OSError, KeyError) as error:
        parser.exit(1, 'Inspection failed: ' + str(error) + '\n')
    print(json.dumps(dict(output=str(args.output.resolve()), decoded=result['decodedCount'],
                          rejected=result['rejectedCount'], buildable=False)))


if __name__ == '__main__':
    main()
