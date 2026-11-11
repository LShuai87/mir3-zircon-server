#!/usr/bin/env python3
"""Compile real OfficialV1 resource descriptors into C#. Never activates a skin.

JSON is consumed at build time only. The current contract remains disabled until
scene bindings and acceptance are complete; it must not be deployed as a release.
"""
import argparse
import importlib.util
import json
import os
from pathlib import Path
import re
import sys

HELPER_PATH = Path(__file__).with_name('Prepare-UiCatalog.py')
spec = importlib.util.spec_from_file_location('official_catalog_primitives', HELPER_PATH)
primitives = importlib.util.module_from_spec(spec)
spec.loader.exec_module(primitives)
require, keys, integer, vector = primitives.require, primitives.keys, primitives.integer, primitives.vector
sha, read_json, read_zl, ordinary_path = primitives.sha, primitives.read_json, primitives.read_zl, primitives.ordinary_path
ROLE = re.compile(r'[a-z][a-z0-9]*(?:-[a-z0-9]+)*\Z')
MAX_PACKAGE = 64 * 1024 * 1024
STATES = ('normal', 'hover', 'pressed', 'disabled')
ASSET_KEYS = ('role png pngSha256 rgbaSha256 sourceLibrary sourcePngSha256 offsetMode alphaMode '
              'blendMode sourceRole state outputIndex sourceIndex logicalSize textureSize sourceOffset outputOffset pivot')


def digest(value, context):
    require(type(value) is str and primitives.HASH.fullmatch(value), context + ': invalid SHA256')


def roles(values, context):
    require(type(values) is list and 0 < len(values) <= 4096, context + ': invalid role list')
    require(all(type(x) is str and ROLE.fullmatch(x) for x in values), context + ': invalid role')
    require(len(set(values)) == len(values), context + ': duplicate role')
    return set(values)


def validate(manifest, prepared, package, recipe, selection, libraries, input_hashes):
    keys(manifest, 'schemaVersion kind style status runtimeReady productionBindingAllowed package inputs sourceLibraries requiredRoles assets buttons scope activationBoundary', 'manifest')
    require(type(manifest['schemaVersion']) is int and manifest['schemaVersion'] == 1 and
            manifest['kind'] == 'official-ui-asset-catalog' and manifest['style'] == 'OfficialV1', 'Unsupported catalog contract')
    require(manifest['status'] == 'pending-scene-integration' and manifest['runtimeReady'] is False and
            manifest['productionBindingAllowed'] is False, 'Scene activation/release is not supported by this compiler')
    require(prepared['kind'] == 'official-ui-prepared-zl' and all(prepared[k] is False for k in
            ('runtimeReady', 'productionBindingAllowed', 'alphaVerified')), 'Expected an unaccepted prepared package')
    require(manifest['inputs'] == input_hashes, 'Build input hashes do not match catalog')
    require(prepared['recipeSha256'] == input_hashes['compositionRecipeSha256'] and
            recipe['selectionSha256'] == input_hashes['selectionSha256'], 'Recipe/selection provenance mismatch')
    for label, item in (('recipe', recipe), ('selection', selection)):
        require(item['productionBindingAllowed'] is False, label + ': unexpected activation')
    keys(manifest['package'], 'fileName sha256 byteLength slotCount', 'package')
    info = manifest['package']
    require(info['fileName'] == 'OfficialV1.Zl', 'Unexpected package filename')
    integer(info['byteLength'], 8, MAX_PACKAGE, 'package.byteLength')
    integer(info['slotCount'], 1, 65536, 'package.slotCount')
    digest(info['sha256'], 'package.sha256')
    require(8 <= len(package) <= MAX_PACKAGE and len(package) == info['byteLength'] and
            sha(package) == info['sha256'] == prepared['sha256'], 'Package hash/length mismatch')
    count, images = read_zl(package)
    require(count == info['slotCount'], 'Package slot count mismatch')
    required = roles(manifest['requiredRoles'], 'requiredRoles')
    require(type(manifest['assets']) is list and len(manifest['assets']) == len(required) == len(prepared['assets']), 'Asset count mismatch')
    require(type(prepared['assets']) is list and len({a['role'] for a in prepared['assets']}) == len(prepared['assets']), 'Duplicate prepared roles')
    originals = {a['role']: a for a in prepared['assets']}
    assets, indices = {}, set()
    for asset in manifest['assets']:
        keys(asset, ASSET_KEYS + ' dataSha256', 'asset')
        role = asset['role']
        require(type(role) is str and ROLE.fullmatch(role) and role not in assets and role in required, 'Unknown or duplicate asset role')
        require({k: v for k, v in asset.items() if k != 'dataSha256'} == originals.get(role), role + ': prepared descriptor mismatch')
        i = integer(asset['outputIndex'], 0, count - 1, role + '.outputIndex')
        require(i not in indices and i in images, role + ': missing or duplicate frame index')
        size = vector(asset['logicalSize'], 2, 1, 4096, role + '.logicalSize')
        texture = vector(asset['textureSize'], 2, 4, 4096, role + '.textureSize')
        offset = vector(asset['outputOffset'], 2, -32768, 32767, role + '.outputOffset')
        vector(asset['sourceOffset'], 2, -32768, 32767, role + '.sourceOffset')
        require(vector(asset['pivot'], 2, -32768, 32767, role + '.pivot') == (0, 0) and offset == (0, 0), role + ': offsets must be baked exactly once')
        require(texture == tuple((v + 3) // 4 * 4 for v in size), role + ': invalid padded texture size')
        require(asset['offsetMode'] == 'baked' and asset['blendMode'] == 'Normal' and asset['alphaMode'] == 'BinaryMask', role + ': unsupported rendering policy')
        for field in ('pngSha256', 'rgbaSha256', 'sourcePngSha256', 'dataSha256'):
            digest(asset[field], role + '.' + field)
        frame = images[i]
        require(size == (frame['width'], frame['height']) and offset == frame['offset'] and
                sha(frame['data']) == asset['dataSha256'], role + ': Zl geometry/payload mismatch')
        primitives.check_alpha(frame, asset['alphaMode'])
        indices.add(i)
        assets[role] = asset
    require(set(assets) == required == set(originals) and indices == set(images), 'Semantic roles do not exactly cover the package')
    require(type(manifest['buttons']) is dict and manifest['buttons'] == prepared['buttons'], 'Button groups differ from prepared contract')
    grouped = set()
    for button, states in manifest['buttons'].items():
        require(type(button) is str and ROLE.fullmatch(button), 'Invalid button role')
        keys(states, ' '.join(STATES), button)
        require(states['normal'] == button and len(set(states.values())) == 4 and all(r in assets for r in states.values()), button + ': missing/duplicate button state')
        require(len({tuple(assets[r]['logicalSize']) for r in states.values()}) == 1, button + ': state dimensions differ')
        for state, r in states.items():
            require(assets[r]['state'] == (None if state == 'normal' else state) and assets[r]['sourceRole'] == button and r not in grouped, button + ': ambiguous state assignment')
            grouped.add(r)
    require(grouped == {r for r, a in assets.items() if a['state'] is not None} | set(manifest['buttons']), 'Ungrouped button state')
    used = {a['sourceLibrary'] for a in assets.values()}
    require(type(libraries['libraries']) is list and len({v['name'] for v in libraries['libraries']}) == len(libraries['libraries']), 'Duplicate source library')
    expected_libraries = [v for v in libraries['libraries'] if v['name'] in used]
    require(len(expected_libraries) == len(used) and manifest['sourceLibraries'] == expected_libraries, 'Source library baseline mismatch')
    for asset in assets.values():
        baseline = next(v for v in expected_libraries if v['name'] == asset['sourceLibrary'])
        integer(asset['sourceIndex'], 0, baseline['slots'] - 1, asset['role'] + '.sourceIndex')
    require(manifest['scope'] == dict(npcCards='Legacy', secondaryPopups='Legacy', displaySettings='unchanged', loginAndCharacterSelection='unchanged'), 'Scope changed')
    require(type(manifest['activationBoundary']) is str and bool(manifest['activationBoundary']), 'Activation boundary is required')
    return assets


def csharp(manifest, manifest_hash):
    quote = lambda s: json.dumps(s, ensure_ascii=True)
    lines = [
        '// <auto-generated> Build-time OfficialV1 resource contract. Do not edit by hand. </auto-generated>',
        'using System.Collections.Generic;', 'using System.Collections.ObjectModel;',
        'using System.Drawing;', 'using Client.Controls;', '', 'namespace Client.Envir', '{',
        '    // No runtime JSON. This catalog alone does not enable the skin or bind gameplay actions.',
        '    public static class GameUiOfficialV1Catalog', '    {',
        '        public const bool RuntimeReady = false;',
        r'        public const string PackageRelativePath = @"Data\GameUI\OfficialV1.Zl";',
        '        public const string ManifestSha256 = ' + quote(manifest_hash) + ';',
        '        public const string PackageSha256 = ' + quote(manifest['package']['sha256']) + ';',
        '        public static GameUiPackageManifest PackageManifest { get; } = new GameUiPackageManifest(',
        '            PackageSha256, new GameUiPackageFrame[]', '            {'
    ]
    by_role = {a['role']: a for a in manifest['assets']}
    for a in sorted(manifest['assets'], key=lambda a: a['outputIndex']):
        w, h = a['logicalSize']
        lines.append('                new GameUiPackageFrame(' + quote(a['role']) + ', ' + str(a['outputIndex']) +
                     ', new Size(' + str(w) + ', ' + str(h) + '), Point.Empty, ' + quote(a['dataSha256']) + '),')
    lines += ['            }, new string[]', '            {']
    for role in sorted(manifest['requiredRoles']):
        lines.append('                ' + quote(role) + ',')
    lines += ['            });', '',
              '        // Art state groups only; existing controls retain their original click handlers.',
              '        public static IReadOnlyDictionary<string, ButtonImageStates> Buttons { get; } =',
              '            new ReadOnlyDictionary<string, ButtonImageStates>(new Dictionary<string, ButtonImageStates>(System.StringComparer.Ordinal)', '            {']
    for role, states in sorted(manifest['buttons'].items()):
        indices = ', '.join(str(by_role[states[s]]['outputIndex']) for s in STATES)
        lines.append('                { ' + quote(role) + ', new ButtonImageStates(' + indices + ') },')
    lines += ['            });', '    }', '}', '']
    return '\n'.join(lines).encode('utf-8')


def compile_catalog(manifest_path, prepared_path, package_path, recipe_path, selection_path, libraries_path, output_path, check=False):
    paths = [ordinary_path(p) for p in (manifest_path, prepared_path, package_path, recipe_path, selection_path, libraries_path)]
    output = ordinary_path(output_path)
    require(output not in paths and len(set(paths)) == len(paths), 'Output/input paths must be distinct')
    require(not output.exists() or check, 'Output already exists; use --check to verify without changing it')
    manifest, manifest_raw = read_json(paths[0])
    prepared, prepared_raw = read_json(paths[1])
    require(paths[2].stat().st_size <= MAX_PACKAGE, 'Package exceeds byte limit')
    package = paths[2].read_bytes()
    recipe, recipe_raw = read_json(paths[3])
    selection, selection_raw = read_json(paths[4])
    libraries, libraries_raw = read_json(paths[5])
    inputs = dict(preparedManifestSha256=sha(prepared_raw), compositionRecipeSha256=sha(recipe_raw),
                  selectionSha256=sha(selection_raw), sourceLibrariesSha256=sha(libraries_raw))
    validate(manifest, prepared, package, recipe, selection, libraries, inputs)
    generated = csharp(manifest, sha(manifest_raw))
    # Reject input changes before publishing the generated source.
    expected = [sha(manifest_raw), sha(prepared_raw), sha(package), sha(recipe_raw), sha(selection_raw), sha(libraries_raw)]
    require(all(sha(p.read_bytes()) == h for p, h in zip(paths, expected)), 'Input changed during compilation')
    if check:
        require(output.is_file() and output.read_bytes() == generated, 'Compiled catalog is missing or stale')
    else:
        output.parent.mkdir(parents=True, exist_ok=True)
        ordinary_path(output.parent)
        with output.open('xb') as stream:
            stream.write(generated)
    return dict(assetCount=len(manifest['assets']), buttonCount=len(manifest['buttons']),
                packageSha256=sha(package), catalogSha256=sha(generated), runtimeReady=False)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('manifest', 'prepared', 'package', 'recipe', 'selection', 'source-libraries', 'output'):
        parser.add_argument('--' + name, type=Path, required=True)
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    try:
        result = compile_catalog(args.manifest, args.prepared, args.package, args.recipe, args.selection,
                                 args.source_libraries, args.output, args.check)
    except (ValueError, OSError, KeyError, TypeError, UnicodeError, RecursionError) as error:
        print('OfficialV1 catalog rejected: ' + str(error), file=sys.stderr)
        return 1
    print('OfficialV1 catalog verified: {assetCount} frames, {buttonCount} button groups; runtime activation disabled.'.format(**result))
    return 0


if __name__ == '__main__':
    sys.exit(main())
