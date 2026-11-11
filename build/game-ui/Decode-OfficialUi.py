#!/usr/bin/env python3
"""Decode the pinned official MIX/MIZ UI libraries into an atomic, indexed PNG set.

Requires Python 3.10+ and Pillow. Source files are always opened read-only.
The output directory must be new, disjoint from the source, and is published
only after every library and image has passed validation.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import struct
import tempfile
import zlib

from PIL import Image

MIX_SIGNATURE = bytes.fromhex('0f284329204d4952332c204d4952332e')
MAX_SLOTS = 100_000
MAX_DIMENSION = 8192
MAX_IMAGE_PIXELS = 16_777_216
MAX_TOTAL_PIXELS = 268_435_456
MAX_FILE_BYTES = 536_870_912


class DecodeError(ValueError):
    pass


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def validate_paths(source, output):
    source, output = Path(source).resolve(strict=True), Path(output).resolve()
    if not source.is_dir():
        raise DecodeError('Source must be a directory')
    if source == output or source in output.parents or output in source.parents:
        raise DecodeError('Source and output directories must be disjoint')
    if output.exists():
        raise DecodeError('Output already exists; choose a new directory')
    return source, output


def load_baseline(path):
    config = json.loads(Path(path).read_text(encoding='utf-8-sig'))
    libraries = config.get('libraries', [])
    if config.get('schemaVersion') != 1 or not libraries or len(libraries) > 32:
        raise DecodeError('Invalid baseline schema or library count')
    names = set()
    for lib in libraries:
        name = lib.get('name', '')
        if not re.fullmatch(r'[A-Za-z][A-Za-z0-9_-]{0,63}', name) or name.casefold() in names:
            raise DecodeError('Unsafe or duplicate library name: ' + name)
        names.add(name.casefold())
        for key in ('mixSha256', 'mizSha256'):
            if not re.fullmatch(r'[0-9a-f]{64}', lib.get(key, '')):
                raise DecodeError(name + ': missing source hash ' + key)
        slots, present = lib.get('slots'), lib.get('present')
        if type(slots) is not int or type(present) is not int or not 0 <= present <= slots <= MAX_SLOTS:
            raise DecodeError(name + ': invalid expected counts')
    return config


def read_pinned(path, expected_hash):
    if not path.is_file() or path.stat().st_size > MAX_FILE_BYTES:
        raise DecodeError(str(path) + ': missing or oversized input')
    data = path.read_bytes()
    if sha256(data) != expected_hash:
        raise DecodeError(str(path) + ': source SHA256 mismatch')
    return data


def decode_image(data, offset):
    if offset < 0 or offset > len(data) - 16:
        raise DecodeError('Image header outside MIZ')
    width, height, x, y, packed_length, tag = struct.unpack_from('<HHhhII', data, offset)
    if not (0 < width <= MAX_DIMENSION and 0 < height <= MAX_DIMENSION) or width * height > MAX_IMAGE_PIXELS:
        raise DecodeError('Invalid or excessive image dimensions')
    end = offset + 16 + packed_length
    if not packed_length or end > len(data):
        raise DecodeError('Compressed image outside MIZ')
    stride = (width * 2 + 3) // 4 * 4
    expected = stride * height
    inflater = zlib.decompressobj()
    try:
        raw = inflater.decompress(data[offset + 16:end], expected + 1)
    except zlib.error as error:
        raise DecodeError('Invalid zlib data: ' + str(error)) from error
    if len(raw) != expected or not inflater.eof or inflater.unconsumed_tail or inflater.unused_data:
        raise DecodeError('Wrong decompressed length or trailing compressed data')
    # Matches the verified source decoder: 565 BGR, aligned rows, bottom-up.
    image = Image.frombytes('RGB', (width, height), raw, 'raw', 'BGR;16', stride, -1).convert('RGBA')
    return image, {'width': width, 'height': height, 'offsetX': x, 'offsetY': y,
                   'packedBytes': packed_length, 'tag': tag}


def decode_libraries(source, output, baseline):
    source, output = validate_paths(source, output)
    config = load_baseline(baseline)
    output.parent.mkdir(parents=True, exist_ok=True)
    staging = Path(tempfile.mkdtemp(prefix='.' + output.name + '-', dir=output.parent))
    total_pixels = 0
    manifest = {'schemaVersion': 1, 'kind': 'official-ui-decoded-reference',
                'alphaVerified': False, 'libraries': []}
    try:
        for lib in config['libraries']:
            name = lib['name']
            mix = read_pinned(source / (name + '.MIX'), lib['mixSha256'])
            miz = read_pinned(source / (name + '.MIZ'), lib['mizSha256'])
            if len(mix) < 24 or mix[:16] != MIX_SIGNATURE:
                raise DecodeError(name + ': invalid MIX header')
            count = struct.unpack_from('<I', mix, 16)[0]
            if count != lib['slots'] or len(mix) != 24 + count * 4:
                raise DecodeError(name + ': MIX count/length mismatch')
            offsets = struct.unpack_from('<' + str(count) + 'I', mix, 24)
            if sum(o != 0 for o in offsets) != lib['present']:
                raise DecodeError(name + ': MIX present count mismatch')
            (staging / name).mkdir()
            entry = dict(lib, items=[])
            manifest['libraries'].append(entry)
            for index, offset in enumerate(offsets):
                if offset == 0:
                    entry['items'].append(None)
                    continue
                try:
                    image, metadata = decode_image(miz, offset)
                    with image:
                        total_pixels += image.width * image.height
                        if total_pixels > MAX_TOTAL_PIXELS:
                            raise DecodeError('Total decoded pixel budget exceeded')
                        relative = name + '/' + str(index) + '.png'
                        path = staging / relative
                        image.save(path, format='PNG', compress_level=9)
                        metadata.update(index=index, png=relative, pngSha256=sha256(path.read_bytes()),
                                        rgbaSha256=sha256(image.tobytes()), alphaMode='opaque', alphaVerified=False)
                    entry['items'].append(metadata)
                except (ValueError, OSError) as error:
                    raise DecodeError(f'{name}[{index}]: {error}') from error
            # Detect source changes during conversion as well as at initial read.
            read_pinned(source / (name + '.MIX'), lib['mixSha256'])
            read_pinned(source / (name + '.MIZ'), lib['mizSha256'])
        (staging / 'manifest.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
        # Windows rename refuses an existing destination; explicit check also protects POSIX empty directories.
        if output.exists():
            raise DecodeError('Output appeared during conversion; refusing to replace it')
        os.rename(staging, output)
    finally:
        if staging.exists():
            shutil.rmtree(staging)
    return manifest


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', required=True, type=Path, help='Official Data directory (read-only)')
    parser.add_argument('--output', required=True, type=Path, help='New output directory')
    parser.add_argument('--baseline', type=Path, default=Path(__file__).with_name('source-libraries.json'))
    args = parser.parse_args()
    try:
        manifest = decode_libraries(args.source, args.output, args.baseline)
    except (ValueError, OSError, KeyError) as error:
        parser.exit(1, 'Decode failed: ' + str(error) + '\n')
    print(json.dumps({'output': str(args.output.resolve()), 'libraries': len(manifest['libraries']),
                      'slots': sum(x['slots'] for x in manifest['libraries']),
                      'present': sum(x['present'] for x in manifest['libraries'])}))


if __name__ == '__main__':
    main()
