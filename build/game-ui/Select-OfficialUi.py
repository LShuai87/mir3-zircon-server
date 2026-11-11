#!/usr/bin/env python3
"""Strictly decode explicitly selected official UI records, retaining source indices.

Whole-file hashes and MIX structure are still verified. Only selected image records
are decoded, so an unsupported record outside the selection is not silently
accepted or converted. Output is an opaque REFERENCE set, not a runtime skin.
"""
import argparse
import importlib.util
import json
import os
from pathlib import Path
import re
import shutil
import struct
import tempfile

_spec = importlib.util.spec_from_file_location('strict_ui_decoder', Path(__file__).with_name('Decode-OfficialUi.py'))
decoder = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(decoder)
ID = re.compile(r'[a-z][a-z0-9-]{0,79}\Z')
HASH = re.compile(r'[0-9a-f]{64}\Z')


def ordinary(path):
    path = Path(os.path.abspath(path))
    for item in (path, *path.parents):
        if item.is_symlink() or (item.exists() and getattr(item.lstat(), 'st_file_attributes', 0) & 0x400):
            raise ValueError('Reparse point/symlink is unsupported: ' + str(item))
    return path


def read_json(path):
    if path.stat().st_size > 8 * 1024 * 1024:
        raise ValueError('JSON input exceeds size limit')
    def unique(pairs):
        result = {}
        for key, value in pairs:
            if key in result:
                raise ValueError('Duplicate JSON key: ' + key)
            result[key] = value
        return result
    return json.loads(path.read_text(encoding='utf-8-sig'), object_pairs_hook=unique)


def selected_decode(source, output, baseline_path, selection_path):
    source, output = ordinary(source), ordinary(output)
    baseline_path, selection_path = ordinary(baseline_path), ordinary(selection_path)
    source, output = decoder.validate_paths(source, output)
    if any(p == output or output in p.parents for p in (baseline_path, selection_path)):
        raise ValueError('Input files must be outside output')
    baseline_raw, selection_raw = baseline_path.read_bytes(), selection_path.read_bytes()
    selection = read_json(selection_path)
    config = decoder.load_baseline(baseline_path)
    if (selection.get('schemaVersion') != 1 or selection.get('kind') != 'official-ui-reference-selection'
            or selection.get('productionBindingAllowed') is not False):
        raise ValueError('Invalid reference selection schema')
    if selection.get('baselineSha256') != decoder.sha256(baseline_raw):
        raise ValueError('Selection baseline SHA256 mismatch')
    assets = selection.get('assets')
    if not isinstance(assets, list) or not 1 <= len(assets) <= 10000:
        raise ValueError('Invalid selection asset count')
    libraries = {lib['name']: lib for lib in config['libraries']}
    roles, chosen, by_library = set(), set(), {}
    for asset in assets:
        if not isinstance(asset, dict):
            raise ValueError('Invalid selection asset')
        role, name, index = asset.get('role'), asset.get('sourceLibrary'), asset.get('sourceIndex')
        if not isinstance(role, str) or not ID.fullmatch(role) or role in roles:
            raise ValueError('Invalid or duplicate semantic role')
        roles.add(role)
        if not isinstance(name, str) or name not in libraries or type(index) is not int or not 0 <= index < libraries[name]['slots']:
            raise ValueError('Unknown library or out-of-range index')
        if (name, index) in chosen:
            raise ValueError('Duplicate source record; use one role with aliases at binding time')
        chosen.add((name, index))
        size, offset = asset.get('logicalSize'), asset.get('sourceOffset')
        if (not isinstance(size, list) or len(size) != 2 or any(type(n) is not int or not 1 <= n <= 8192 for n in size)
                or not isinstance(offset, list) or len(offset) != 2 or any(type(n) is not int or not -32768 <= n <= 32767 for n in offset)):
            raise ValueError('Invalid expected geometry')
        if not isinstance(asset.get('pngSha256'), str) or not HASH.fullmatch(asset['pngSha256']):
            raise ValueError('Expected decoded PNG SHA256 required')
        by_library.setdefault(name, []).append(asset)
    manifest = dict(schemaVersion=1, kind='official-ui-decoded-reference', alphaVerified=False,
                    productionBindingAllowed=False, selectedOnly=True,
                    baselineSha256=decoder.sha256(baseline_raw), selectionSha256=decoder.sha256(selection_raw),
                    semanticRoles={}, libraries=[])
    output.parent.mkdir(parents=True, exist_ok=True)
    staging = Path(tempfile.mkdtemp(prefix='.' + output.name + '-', dir=output.parent))
    total_pixels = 0
    try:
        for name in sorted(by_library):
            lib = libraries[name]
            mix = decoder.read_pinned(ordinary(source / (name + '.MIX')), lib['mixSha256'])
            miz = decoder.read_pinned(ordinary(source / (name + '.MIZ')), lib['mizSha256'])
            if len(mix) < 24 or mix[:16] != decoder.MIX_SIGNATURE:
                raise ValueError(name + ': invalid MIX header')
            count = struct.unpack_from('<I', mix, 16)[0]
            if count != lib['slots'] or len(mix) != 24 + count * 4:
                raise ValueError(name + ': MIX count/length mismatch')
            offsets = struct.unpack_from('<' + str(count) + 'I', mix, 24)
            if sum(n != 0 for n in offsets) != lib['present']:
                raise ValueError(name + ': MIX present count mismatch')
            (staging / name).mkdir()
            entry = dict(lib, sourcePresent=lib['present'], present=len(by_library[name]),
                         unselectedPresent=lib['present']-len(by_library[name]), items=[None]*count)
            manifest['libraries'].append(entry)
            for asset in sorted(by_library[name], key=lambda a: a['sourceIndex']):
                index = asset['sourceIndex']
                if offsets[index] == 0:
                    raise ValueError(f'{name}[{index}]: selected slot is empty')
                try:
                    image, metadata = decoder.decode_image(miz, offsets[index])
                    with image:
                        if ([image.width, image.height] != asset['logicalSize']
                                or [metadata['offsetX'], metadata['offsetY']] != asset['sourceOffset']):
                            raise ValueError('Selected record geometry differs from reviewed input')
                        total_pixels += image.width * image.height
                        if total_pixels > decoder.MAX_TOTAL_PIXELS:
                            raise ValueError('Total decoded pixel budget exceeded')
                        relative = name + '/' + str(index) + '.png'
                        path = staging / relative
                        image.save(path, format='PNG', compress_level=9)
                        png_hash = decoder.sha256(path.read_bytes())
                        if png_hash != asset['pngSha256']:
                            raise ValueError('Decoded PNG SHA256 differs from reviewed input')
                        metadata.update(index=index, sourceOffset=offsets[index], png=relative,
                                        pngSha256=png_hash, rgbaSha256=decoder.sha256(image.tobytes()),
                                        alphaMode='opaque', alphaVerified=False)
                        entry['items'][index] = metadata
                        manifest['semanticRoles'][asset['role']] = dict(library=name, index=index, png=relative)
                except (ValueError, OSError) as error:
                    raise ValueError(f'{name}[{index}]: {error}') from error
            decoder.read_pinned(source / (name + '.MIX'), lib['mixSha256'])
            decoder.read_pinned(source / (name + '.MIZ'), lib['mizSha256'])
        if baseline_path.read_bytes() != baseline_raw or selection_path.read_bytes() != selection_raw:
            raise ValueError('Input metadata changed during conversion')
        (staging / 'selection.json').write_bytes(selection_raw)
        (staging / 'baseline.json').write_bytes(baseline_raw)
        (staging / 'manifest.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
        if output.exists():
            raise ValueError('Output appeared during conversion')
        os.rename(staging, output)
    finally:
        if staging.exists():
            shutil.rmtree(staging)
    return manifest


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for key in ('source', 'output', 'baseline', 'selection'):
        parser.add_argument('--'+key, type=Path, required=True)
    args = parser.parse_args()
    try:
        result = selected_decode(args.source, args.output, args.baseline, args.selection)
    except (ValueError, OSError, KeyError, TypeError) as error:
        parser.exit(1, 'Selected UI conversion failed: ' + str(error) + '\n')
    print(f"Selected reference decoded: {len(result['semanticRoles'])} roles; original indices retained; runtime binding disabled.")


if __name__ == '__main__':
    main()
