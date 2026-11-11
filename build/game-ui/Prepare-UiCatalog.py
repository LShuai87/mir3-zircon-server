#!/usr/bin/env python3
"""Validate a prepared Zl/catalog bundle. Synthetic preparation only, never deployment.

The package must already have been written by Mir3Library.Save. This tool does
not infer alpha, assign final roles, crop official images or close input gates.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import struct
import sys
import tempfile

RESOLUTIONS = ((1024, 768), (1366, 768), (1280, 800), (1440, 900), (1600, 900), (1920, 1080))
ID = re.compile(r"[a-z][a-z0-9]*(?:\.[a-z][a-z0-9]*){1,7}\Z")
HASH = re.compile(r"[0-9a-f]{64}\Z")
SOURCE = re.compile(r"[A-Za-z][A-Za-z0-9_.]{0,127}\Z")
MAX_PACKAGE = 512 * 1024 * 1024


def require(condition, message):
    if not condition:
        raise ValueError(message)


def sha(data):
    return hashlib.sha256(data).hexdigest()


def keys(value, expected, context):
    require(type(value) is dict and set(value) == set(expected.split()), context + ': unexpected or missing keys')


def integer(value, low, high, context):
    require(type(value) is int and low <= value <= high, context + ': integer outside range')
    return value


def vector(value, length, low, high, context):
    require(type(value) is list and len(value) == length, context + ': invalid vector')
    return tuple(integer(x, low, high, context) for x in value)


def identifiers(values, context):
    require(type(values) is list and 0 < len(values) <= 10000, context + ': invalid identifier list')
    require(all(type(x) is str and ID.fullmatch(x) for x in values), context + ': invalid identifier')
    require(len(set(values)) == len(values), context + ': duplicate identifier')
    return set(values)


def ordinary_path(path):
    # Check before resolve: resolving first would hide directory junctions.
    path = Path(os.path.abspath(path))
    for item in (path, *path.parents):
        try:
            info = item.lstat()
        except FileNotFoundError:
            continue
        require(not item.is_symlink() and not (getattr(info, 'st_file_attributes', 0) & 0x400),
                'Reparse point/symlink is unsupported: ' + str(item))
    return path


def read_json(path):
    def pairs(items):
        result = {}
        for key, value in items:
            require(key not in result, 'Duplicate JSON key: ' + key)
            result[key] = value
        return result
    def invalid_constant(value):
        raise ValueError('Non-finite JSON number: ' + value)
    require(path.stat().st_size <= 8 * 1024 * 1024, 'JSON exceeds size limit')
    raw = path.read_bytes()
    return json.loads(raw.decode('utf-8-sig'), object_pairs_hook=pairs, parse_constant=invalid_constant), raw


def read_zl(data):
    require(8 <= len(data) <= MAX_PACKAGE, 'Invalid Zl byte length')
    header_size, count = struct.unpack_from('<ii', data)
    require(0 < count <= 100000, 'Invalid Zl slot count')
    header_end = 4 + header_size
    require(8 + count <= header_end <= len(data), 'Invalid Zl header size')
    cursor = 8
    images = {}
    for index in range(count):
        require(cursor < header_end, 'Truncated Zl presence table')
        present = data[cursor]
        cursor += 1
        require(present in (0, 1), 'Noncanonical Zl presence byte')
        if not present:
            continue
        require(cursor + 25 <= header_end, 'Truncated Zl image header')
        fields = struct.unpack_from('<ihhhhBhhhhhh', data, cursor)
        cursor += 25
        position, width, height, x, y = fields[:5]
        require(0 < width <= 8192 and 0 < height <= 8192 and width * height <= 16777216,
                'Invalid Zl image size')
        require(all(value == 0 for value in fields[5:]), 'UI package cannot contain shadow/overlay metadata')
        size = ((width + 3) // 4) * ((height + 3) // 4) * 8
        require(header_end <= position <= len(data) - size, 'Zl payload outside file')
        images[index] = dict(width=width, height=height, offset=(x, y), position=position,
                             size=size, data=data[position:position + size])
    require(cursor == header_end, 'Zl header has trailing bytes')
    next_position = header_end
    for image in images.values():
        require(image['position'] == next_position, 'Zl payload gap/overlap/order mismatch')
        next_position += image['size']
    require(next_position == len(data), 'Zl has trailing/unclaimed bytes')
    return count, images


def check_alpha(image, mode):
    """Check DXT1 transparency, including right/bottom padding, at every texel."""
    width, height = image['width'], image['height']
    blocks_w = (width + 3) // 4
    for offset in range(0, len(image['data']), 8):
        c0, c1, indices = struct.unpack_from('<HHI', image['data'], offset)
        block = offset // 8
        bx, by = block % blocks_w * 4, block // blocks_w * 4
        for pixel in range(16):
            transparent = c0 <= c1 and ((indices >> (pixel * 2)) & 3) == 3
            if bx + pixel % 4 >= width or by + pixel // 4 >= height:
                require(transparent, 'DXT1 padding must be transparent')
            elif mode == 'Opaque':
                require(not transparent, 'Opaque asset contains transparent logical texel')


def validate(contract, manifest, package):
    keys(contract, 'schemaVersion kind productionBindingAllowed roles buttons layoutIds bindingIds', 'contract')
    keys(manifest, 'schemaVersion kind productionBindingAllowed package assets buttons layouts bindings', 'manifest')
    for label, value in (('contract', contract), ('manifest', manifest)):
        integer(value['schemaVersion'], 1, 1, label + '.schemaVersion')
        require(value['kind'] == 'synthetic-ui-catalog-preparation', label + ': synthetic preparation only')
        require(value['productionBindingAllowed'] is False, label + ': production binding is forbidden')
    role_ids = identifiers(contract['roles'], 'contract.roles')
    button_ids = identifiers(contract['buttons'], 'contract.buttons')
    layout_ids = identifiers(contract['layoutIds'], 'contract.layoutIds')
    binding_ids = identifiers(contract['bindingIds'], 'contract.bindingIds')
    keys(manifest['package'], 'fileName sha256', 'package')
    require(manifest['package']['fileName'] == 'Fixture.Zl', 'Only Fixture.Zl may be prepared')
    require(type(manifest['package']['sha256']) is str and HASH.fullmatch(manifest['package']['sha256']), 'Invalid package SHA256')
    require(sha(package) == manifest['package']['sha256'], 'Package SHA256 mismatch')
    count, images = read_zl(package)
    require(type(manifest['assets']) is list and len(manifest['assets']) == len(role_ids), 'Missing/extra assets')
    assets, indices = {}, set()
    for asset in manifest['assets']:
        keys(asset, 'role outputIndex logicalSize textureSize sourceOffset pivot offsetMode alphaMode blendMode dataSha256', 'asset')
        role = asset['role']
        require(type(role) is str and role in role_ids and role not in assets, 'Unknown/duplicate role')
        index = integer(asset['outputIndex'], 0, count - 1, role + '.outputIndex')
        require(index not in indices and index in images, 'Conflicting/empty output index')
        indices.add(index)
        logical = vector(asset['logicalSize'], 2, 1, 8192, role + '.logicalSize')
        texture = vector(asset['textureSize'], 2, 4, 8192, role + '.textureSize')
        require(texture == tuple((x + 3) // 4 * 4 for x in logical), 'Invalid DXT1 padded texture size')
        source_offset = vector(asset['sourceOffset'], 2, -32768, 32767, role + '.sourceOffset')
        vector(asset['pivot'], 2, -32768, 32767, role + '.pivot')
        require(asset['offsetMode'] in ('baked', 'source'), 'Unknown offset mode')
        require(asset['alphaMode'] in ('Opaque', 'BinaryMask'), 'Unsupported alpha mode')
        require(asset['blendMode'] == 'Normal', 'Unsupported blend mode')
        image = images[index]
        require(logical == (image['width'], image['height']), 'Zl logical dimensions mismatch')
        require(image['offset'] == (source_offset if asset['offsetMode'] == 'source' else (0, 0)), 'Zl offset mismatch')
        require(type(asset['dataSha256']) is str and HASH.fullmatch(asset['dataSha256']) and sha(image['data']) == asset['dataSha256'],
                'Image DXT1 SHA256 mismatch')
        check_alpha(image, asset['alphaMode'])
        assets[role] = asset
    require(indices == set(images), 'Zl contains unbound images')
    require(type(manifest['buttons']) is dict and set(manifest['buttons']) == button_ids, 'Missing/unknown buttons')
    for button_id, states in manifest['buttons'].items():
        keys(states, 'normal hover pressed disabled', button_id)
        require(all(type(role) is str and role in assets for role in states.values()), 'Unknown button state role')
        geometry = ('logicalSize', 'textureSize', 'sourceOffset', 'pivot', 'offsetMode', 'alphaMode', 'blendMode')
        normal = assets[states['normal']]
        require(all(all(assets[role][key] == normal[key] for key in geometry) for role in states.values()),
                'Button states have inconsistent geometry/alpha/blend')
    require(type(manifest['layouts']) is list and len(manifest['layouts']) == len(RESOLUTIONS), 'Six layouts required')
    resolutions = set()
    for layout in manifest['layouts']:
        keys(layout, 'width height rects', 'layout')
        pair = (integer(layout['width'], 1, 1920, 'layout.width'), integer(layout['height'], 1, 1080, 'layout.height'))
        require(pair in RESOLUTIONS and pair not in resolutions, 'Unknown/duplicate resolution')
        resolutions.add(pair)
        require(type(layout['rects']) is dict and set(layout['rects']) == layout_ids, 'Missing/unknown layout IDs')
        for rect_id, rect in layout['rects'].items():
            x, y, w, h = vector(rect, 4, 0, 1920, rect_id)
            require(w > 0 and h > 0 and x + w <= pair[0] and y + h <= pair[1], 'Invalid/out-of-bounds layout rectangle')
    require(type(manifest['bindings']) is dict and set(manifest['bindings']) == binding_ids, 'Missing/unknown bindings')
    for binding_id, binding in manifest['bindings'].items():
        keys(binding, 'page source', binding_id)
        require(binding['page'] in ('character', 'extraEquipment', 'status', 'hermit'), 'Unknown binding page')
        require(type(binding['source']) is str and SOURCE.fullmatch(binding['source']), 'Invalid binding source')
    return assets


def csharp(manifest, contract_hash, manifest_hash):
    quote = lambda value: json.dumps(value, ensure_ascii=True)
    lines = [
        '// Generated by Prepare-UiCatalog.py. SYNTHETIC PREPARATION ONLY.',
        'using System.Collections.Generic;',
        'using System.Collections.ObjectModel;',
        'namespace Client.Models.GameUi.Preparatory',
        '{',
        '    public static class PreparedUiCatalog',
        '    {',
        '        public const bool ProductionBindingAllowed = false;',
        '        public const string PackageSha256 = ' + quote(manifest['package']['sha256']) + ';',
        '        public const string ContractSha256 = ' + quote(contract_hash) + ';',
        '        public const string ManifestSha256 = ' + quote(manifest_hash) + ';',
        '        public sealed class Asset',
        '        {',
        '            public readonly int Index, Width, Height, TextureWidth, TextureHeight, OffsetX, OffsetY, PivotX, PivotY;',
        '            public readonly string OffsetMode, AlphaMode, BlendMode, DataSha256;',
        '            public Asset(int i, int w, int h, int tw, int th, int ox, int oy, int px, int py, string om, string am, string bm, string hash)',
        '            { Index=i; Width=w; Height=h; TextureWidth=tw; TextureHeight=th; OffsetX=ox; OffsetY=oy; PivotX=px; PivotY=py; OffsetMode=om; AlphaMode=am; BlendMode=bm; DataSha256=hash; }',
        '        }',
        '        public sealed class Button',
        '        {',
        '            public readonly string Normal, Hover, Pressed, Disabled;',
        '            public Button(string n, string h, string p, string d) { Normal=n; Hover=h; Pressed=p; Disabled=d; }',
        '        }',
        '        public sealed class Rect',
        '        {',
        '            public readonly int X, Y, Width, Height;',
        '            public Rect(int x, int y, int w, int h) { X=x; Y=y; Width=w; Height=h; }',
        '        }',
        '        public sealed class Binding',
        '        {',
        '            public readonly string Page, Source;',
        '            public Binding(string p, string s) { Page=p; Source=s; }',
        '        }',
        '        public static readonly ReadOnlyDictionary<string, Asset> Assets = new ReadOnlyDictionary<string, Asset>(new Dictionary<string, Asset>',
        '        {'
    ]
    for asset in sorted(manifest['assets'], key=lambda a: a['role']):
        nums = [asset['outputIndex'], *asset['logicalSize'], *asset['textureSize'], *asset['sourceOffset'], *asset['pivot']]
        args = [str(x) for x in nums] + [quote(asset[key]) for key in ('offsetMode', 'alphaMode', 'blendMode', 'dataSha256')]
        lines.append('            { ' + quote(asset['role']) + ', new Asset(' + ', '.join(args) + ') },')
    lines += ['        });', '        public static readonly ReadOnlyDictionary<string, Button> Buttons = new ReadOnlyDictionary<string, Button>(new Dictionary<string, Button>', '        {']
    for key, states in sorted(manifest['buttons'].items()):
        lines.append('            { ' + quote(key) + ', new Button(' + ', '.join(quote(states[k]) for k in ('normal', 'hover', 'pressed', 'disabled')) + ') },')
    lines += ['        });', '        public static readonly ReadOnlyDictionary<string, ReadOnlyDictionary<string, Rect>> Layouts = new ReadOnlyDictionary<string, ReadOnlyDictionary<string, Rect>>(new Dictionary<string, ReadOnlyDictionary<string, Rect>>', '        {']
    for layout in sorted(manifest['layouts'], key=lambda v: (v['width'], v['height'])):
        key = str(layout['width']) + 'x' + str(layout['height'])
        lines.append('            { ' + quote(key) + ', new ReadOnlyDictionary<string, Rect>(new Dictionary<string, Rect> {')
        for rect_id, rect in sorted(layout['rects'].items()):
            lines.append('                { ' + quote(rect_id) + ', new Rect(' + ', '.join(str(n) for n in rect) + ') },')
        lines.append('            }) },')
    lines += ['        });', '        public static readonly ReadOnlyDictionary<string, Binding> Bindings = new ReadOnlyDictionary<string, Binding>(new Dictionary<string, Binding>', '        {']
    for key, binding in sorted(manifest['bindings'].items()):
        lines.append('            { ' + quote(key) + ', new Binding(' + quote(binding['page']) + ', ' + quote(binding['source']) + ') },')
    lines += ['        });', '    }', '}', '']
    return '\n'.join(lines).encode('utf-8')


def prepare(contract_path, manifest_path, package_path, output_path):
    inputs = [ordinary_path(p) for p in (contract_path, manifest_path, package_path)]
    output = ordinary_path(output_path)
    require(not output.exists(), 'Output must be a new directory')
    for path in inputs:
        require(path.is_file() and not path.is_relative_to(output), 'Inputs must be files outside output')
    contract, contract_raw = read_json(inputs[0])
    manifest, manifest_raw = read_json(inputs[1])
    require(inputs[2].stat().st_size <= MAX_PACKAGE, 'Zl exceeds byte limit')
    package = inputs[2].read_bytes()
    assets = validate(contract, manifest, package)
    generated = csharp(manifest, sha(contract_raw), sha(manifest_raw))
    report = dict(schemaVersion=1, kind='synthetic-ui-catalog-preparation', productionBindingAllowed=False,
                  releaseAccepted=False, packageSha256=sha(package), contractSha256=sha(contract_raw),
                  manifestSha256=sha(manifest_raw), catalogSha256=sha(generated),
                  generatorSha256=sha(Path(__file__).read_bytes()), assetCount=len(assets),
                  buttonCount=len(manifest['buttons']), layoutCount=len(manifest['layouts']),
                  bindingCount=len(manifest['bindings']))
    output.parent.mkdir(parents=True, exist_ok=True)
    ordinary_path(output.parent)
    staging = Path(tempfile.mkdtemp(prefix='.' + output.name + '.staging-', dir=output.parent))
    try:
        (staging / 'Fixture.Zl').write_bytes(package)
        (staging / 'PreparedUiCatalog.cs').write_bytes(generated)
        (staging / 'preparation.json').write_text(json.dumps(report, sort_keys=True, indent=2) + '\n', encoding='utf-8')
        require(not output.exists(), 'Output appeared during preparation')
        os.rename(staging, output)
    finally:
        if staging.exists():
            shutil.rmtree(staging)
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--contract', type=Path, required=True)
    parser.add_argument('--manifest', type=Path, required=True)
    parser.add_argument('--package', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    try:
        result = prepare(args.contract, args.manifest, args.package, args.output)
    except (ValueError, OSError, UnicodeError, RecursionError) as error:
        print('Catalog preparation rejected: ' + str(error), file=sys.stderr)
        return 1
    print('Synthetic catalog prepared: {assetCount} assets, {buttonCount} buttons, {layoutCount} layouts; production disabled.'.format(**result))
    return 0


if __name__ == '__main__':
    sys.exit(main())
