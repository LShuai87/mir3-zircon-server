#!/usr/bin/env python3
"""Audit effective UI asset use; an intact candidate package is not a closed gate."""
import argparse
from collections import Counter
import hashlib
import json
from pathlib import Path
import re
import struct

ROOT = Path(__file__).resolve().parents[2]
SURFACES = {'hud', 'character', 'inventory', 'belt', 'minimap', 'chat', 'npc-shop', 'item-tooltip', 'warehouse'}
STATES = {'normal', 'hover', 'pressed', 'disabled'}


def require(condition, reason):
    if not condition:
        raise ValueError(reason)


def load(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def sha(raw):
    return hashlib.sha256(raw).hexdigest()


def unique(items, key, label):
    result = {item[key]: item for item in items}
    require(len(result) == len(items), 'Duplicate ' + label)
    return result


def installed_uses(text):
    # Conservative static inventory of the current entrypoint, not a C# execution proof.
    # Reject binding expressions this bounded scanner cannot resolve.
    text = re.sub(r'//[^\n]*|/\*.*?\*/', '', text, flags=re.S)
    calls = re.findall(r'\b(?:Frame|Skin)\(([^,\n\)]*)', text)
    require(all(c == 'string id' or re.fullmatch(r'"[^"\n]+"', c) for c in calls),
            'Unrecognized frame expression; review the scanner')
    indexed_frames = re.findall(r'GameUiOfficialV1Catalog\.PackageManifest\.Frames\[([^\]\n]+)\]', text)
    require(indexed_frames == ['id', 'id'], 'Unrecognized direct catalog frame access')
    roles = set(re.findall(r'\b(?:Frame|Skin)\("([^"\n]+)"', text))
    buttons = set(re.findall(r'GameUiOfficialV1Catalog\.Buttons\["([^"\n]+)"\]', text))
    mappings = re.findall(r'var buttons = new Dictionary<string, string>\s*\{(.*?)\};', text, re.S)
    require(len(mappings) == 1, 'Cannot resolve HUD button dictionary')
    entries = re.findall(r'\["(\w+Button)"\]\s*=\s*"([^"\n]+)"', mappings[0])
    require(len(entries) == 8 and len({key for key, _ in entries}) == 8, 'HUD artwork bindings changed; review the scanner')
    buttons.update(value for _, value in entries)
    accesses = re.findall(r'GameUiOfficialV1Catalog\.Buttons\[([^\]\n]+)\]', text)
    require(all(a.startswith('"') and a.endswith('"') or a == 'binding.Value' for a in accesses),
            'Unrecognized catalog button access')
    require('foreach (var binding in buttons)' in text and 'Buttons[binding.Value]' in text,
            'HUD dictionary is no longer bound by the reviewed loop')
    return roles, buttons


def validate_contract(usage, manifest, selection, recipe, read_source):
    require(usage['schemaVersion'] == 1 and usage['kind'] == 'effective-scope-asset-usage-contract', 'Unknown usage contract')
    require(usage['referenceResolution'] == [1024, 768] and usage['windowMode'] is True, 'Confirmed resolution changed')
    require(all(usage[k] is False for k in ('runtimeReady', 'productionBindingAllowed', 'layoutFrozen', 'gateClosureAllowed')),
            'Candidate audit cannot authorize activation or gate closure')
    require(usage['scope'] == manifest['scope'] == {'npcCards': 'Legacy', 'secondaryPopups': 'Legacy',
            'displaySettings': 'unchanged', 'loginAndCharacterSelection': 'unchanged'}, 'Scope changed')
    surfaces = unique(usage['surfaces'], 'id', 'surface')
    require(set(surfaces) == SURFACES, 'Effective scope is missing a surface')
    assets = unique(manifest['assets'], 'role', 'asset role')
    require(set(manifest['requiredRoles']) == set(assets) and len(manifest['requiredRoles']) == len(assets),
            'Candidate role list differs from package inventory')
    require([a['outputIndex'] for a in manifest['assets']] == list(range(len(assets))), 'Noncontiguous frame indices')
    selected = unique(selection['assets'], 'role', 'selected source')
    base = unique(recipe['assets'], 'role', 'composition role')
    derived = unique(recipe['derivedFrames'], 'role', 'derived role')
    rules = usage['rules']
    require((rules['alphaMode'], rules['blendMode'], rules['offsetMode'], rules['outputOffset'], rules['pivot'],
             rules['textureAlignment'], rules['rgbCompression'], rules['gpuRgbTolerance']) ==
            ('BinaryMask', 'Normal', 'baked', [0, 0], [0, 0], 4, 'BC1-lossy', 8), 'Unreviewed render rules')
    require(rules['buttonTint'] == recipe['buttonTint'] ==
            {'hover': [115, 100], 'pressed': [75, 100], 'disabled': [60, 100]}, 'Unreviewed tint rules')
    require(selection['productionBindingAllowed'] is False and recipe['productionBindingAllowed'] is False,
            'Art input became production eligible')
    for role, a in assets.items():
        source = selected[a['sourceRole']]
        require((a['sourceLibrary'], a['sourceIndex'], a['sourcePngSha256'], a['sourceOffset']) ==
                (source['sourceLibrary'], source['sourceIndex'], source['pngSha256'], source['sourceOffset']),
                'Source identity differs: ' + role)
        require(all(a[k] == rules[k] for k in ('alphaMode', 'blendMode', 'offsetMode', 'outputOffset', 'pivot')),
                'Frame render metadata differs: ' + role)
        size = a['logicalSize']
        require(len(size) == 2 and all(type(v) is int and 0 < v <= 16384 for v in size), 'Invalid frame size: ' + role)
        require(a['textureSize'] == [(v + 3) // 4 * 4 for v in size], 'BC1 padding differs: ' + role)
        if role in derived:
            require(a['sourceRole'] == derived[role]['sourceRole'], 'Derived source differs: ' + role)
        else:
            base_role = role if role in base else a['sourceRole']
            require(base_role in base, 'Missing composition recipe: ' + role)
            rule = base[base_role]
            require(a['sourceRole'] == rule['sourceRole'], 'Composition source differs: ' + role)
            require(rule['alphaPolicy'] == 'edge-connected-exact-black', 'Unreviewed alpha extraction: ' + role)
            if a['state'] is not None:
                require(rule['button'] is True and a['state'] in STATES, 'Invalid button composition state: ' + role)
    groups = manifest['buttons']
    for group, states in groups.items():
        require(set(states) == STATES, 'Incomplete button states: ' + group)
        require(len(set(states.values())) == 4 and set(states.values()) <= assets.keys(), 'Invalid button frame mapping: ' + group)
        for state, role in states.items():
            require(assets[role]['sourceRole'] == group and assets[role]['state'] == (None if state == 'normal' else state),
                    'Button frame identity differs: ' + group + '/' + state)
        require(len({tuple(assets[role]['logicalSize']) for role in states.values()}) == 1, 'Button state dimensions differ: ' + group)
    declared_roles = [v for s in surfaces.values() for v in s['roles']]
    declared_buttons = [v for s in surfaces.values() for v in s['buttonGroups']]
    require(all(v == 1 for v in Counter(declared_roles).values()) and all(v == 1 for v in Counter(declared_buttons).values()),
            'Ambiguous surface ownership')
    actual_roles, actual_buttons = installed_uses(read_source(usage['entrypoint']))
    require(set(declared_roles) == actual_roles, 'Scene frame uses differ from declared effective usage')
    require(set(declared_buttons) == actual_buttons, 'Scene button uses differ from declared effective usage')
    require(actual_roles <= assets.keys() and actual_buttons <= groups.keys(), 'Installed art is missing')
    uninstalled = usage['uninstalledButtonGroups']
    require(set(uninstalled) == groups.keys() - actual_buttons and all(uninstalled.values()), 'Uninstalled buttons not accounted for')
    consumed = actual_roles | {role for group in actual_buttons for role in groups[group].values()}
    composition_only = set(usage['compositionOnlyRoles'])
    reserved = {role for group in uninstalled for role in groups[group].values()}
    staged = usage['stagedFrameRoles']
    require(isinstance(staged, dict) and all(role in assets and assets[role]['state'] is None and
            role not in {frame for states in groups.values() for frame in states.values()} and
            isinstance(ref, dict) and set(ref) == {'surface', 'reason'} and ref['surface'] in surfaces and ref['reason']
            for role, ref in staged.items()), 'Invalid staged frame classification')
    classifications = [consumed, composition_only, reserved, set(staged)]
    require(all(not left & right for i, left in enumerate(classifications) for right in classifications[i+1:]) and
            set().union(*classifications) == assets.keys(), 'Unclassified or multiply classified frames')
    for surface in surfaces.values():
        for ref in surface['reuse']:
            require(ref['reason'] and ref['anchor'] in read_source(ref['path']), 'Reused-control evidence is stale: ' + surface['id'])
        require(surface['roles'] or surface['reuse'], 'Surface has no art/control decision')
    gaps = usage['outstanding']
    require(len({g['id'] for g in gaps}) == len(gaps) and
            all(g['surface'] in surfaces and g['reason'] and g['taskIds'] for g in gaps), 'Invalid outstanding role')
    require(any(g['surface'] == 'warehouse' and g['id'] == 'warehouse-background' for g in gaps),
            'Warehouse integration is incomplete; its open requirement must remain explicit')
    require(all(any(g['id'] == role and g['surface'] == ref['surface'] for g in gaps)
                for role, ref in staged.items()), 'Staged frame has no outstanding integration requirement')
    require(len(usage['remainingAcceptance']) >= 2, 'Final visual/control acceptance is missing')
    return dict(integrityChecksPassed=True, requirementCoverageComplete=False, referenceResolution=[1024, 768],
                surfaceCount=len(surfaces), packageFrames=len(assets), sceneReferencedFrames=len(consumed),
                directFrameRoles=sorted(actual_roles), installedButtonGroups=sorted(actual_buttons),
                compositionOnlyRoles=sorted(composition_only), uninstalledButtonGroups=sorted(uninstalled),
                uninstalledButtonFrames=len(reserved), stagedFrameRoles=sorted(staged), stagedFrames=len(staged), outstanding=gaps,
                remainingAcceptance=usage['remainingAcceptance'], gateClosureAllowed=False,
                runtimeReady=False, productionBindingAllowed=False)


def verify_package(raw, manifest):
    require(len(raw) >= 8 and len(raw) == manifest['package']['byteLength'] and
            sha(raw) == manifest['package']['sha256'], 'Package digest or length differs')
    header, count = struct.unpack_from('<ii', raw)
    require(count == len(manifest['assets']) == manifest['package']['slotCount'] and header == 4 + count * 26,
            'Invalid Zl table')
    cursor = header + 4
    require(cursor <= len(raw), 'Truncated Zl table')
    for index, a in enumerate(manifest['assets']):
        at = 8 + index * 26
        require(raw[at] == 1, 'Missing Zl frame')
        start, w, h, *metadata = struct.unpack_from('<ihhhhBhhhhhh', raw, at + 1)
        length = ((w + 3) // 4) * ((h + 3) // 4) * 8
        require(a['outputIndex'] == index and [w, h] == a['logicalSize'] and metadata == [0] * 9,
                'Zl frame geometry differs: ' + a['role'])
        require(start == cursor and start + length <= len(raw), 'Invalid Zl payload bounds')
        require(sha(raw[start:start + length]) == a['dataSha256'], 'Zl payload hash differs: ' + a['role'])
        cursor += length
    require(cursor == len(raw), 'Unaccounted Zl bytes')
    return count


def verify_artifacts(directory, manifest, selection):
    from PIL import Image
    for source in selection['assets']:
        file = directory / 'decoded' / source['sourceLibrary'] / (str(source['sourceIndex']) + '.png')
        require(sha(file.read_bytes()) == source['pngSha256'], 'Decoded source hash differs: ' + source['role'])
    opaque_black = transparent = 0
    for a in manifest['assets']:
        file = directory / 'art' / a['png']
        require(sha(file.read_bytes()) == a['pngSha256'], 'PNG digest differs: ' + a['role'])
        with Image.open(file) as image:
            rgba = image.convert('RGBA')
            pixels = rgba.tobytes()
            require(list(rgba.size) == a['logicalSize'] and sha(pixels) == a['rgbaSha256'], 'PNG pixels differ: ' + a['role'])
        require(set(pixels[3::4]) <= {0, 255}, 'Nonbinary alpha: ' + a['role'])
        for i in range(0, len(pixels), 4):
            if pixels[i + 3] == 0:
                require(pixels[i:i + 3] == b'\0\0\0', 'Noncanonical transparent RGB: ' + a['role'])
                transparent += 1
            elif pixels[i:i + 3] == b'\0\0\0':
                opaque_black += 1
    frames = verify_package((directory / 'zl' / 'OfficialV1.Zl').read_bytes(), manifest)
    return dict(selectedSourcePngs=len(selection['assets']), outputPngs=len(manifest['assets']),
                binaryAlphaFrames=len(manifest['assets']), opaqueBlackPixels=opaque_black,
                transparentPixels=transparent, verifiedBc1Payloads=frames, gpuComparisonExecuted=False)


def audit(root, artifacts=None):
    usage = load(root / 'build/game-ui/asset-usage-official-v1.json')
    for entry in usage['inputs']:
        require(sha((root / entry['path']).read_bytes()) == entry['sha256'], 'Input hash differs: ' + entry['path'])
    manifest = load(root / 'build/game-ui/asset-manifest.json')
    selection = load(root / 'build/game-ui/npc-shop-selection.json')
    recipe = load(root / 'build/game-ui/warehouse-combined-composition-recipe.json')
    require(recipe['selectionSha256'] == sha((root / 'build/game-ui/npc-shop-selection.json').read_bytes()), 'Recipe selection hash differs')
    report = validate_contract(usage, manifest, selection, recipe, lambda p: (root / p).read_text(encoding='utf-8-sig'))
    report['artifactVerification'] = verify_artifacts(artifacts, manifest, selection) if artifacts else None
    report['staticAuditOnly'] = True
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=Path, default=ROOT)
    parser.add_argument('--artifacts', type=Path, help='Candidate output directory containing art/, decoded/ and zl/')
    parser.add_argument('--output', type=Path)
    parser.add_argument('--require-complete', action='store_true', help='Fail while effective role or final visual acceptance is open')
    args = parser.parse_args()
    report = audit(args.root.resolve(), args.artifacts)
    if args.output:
        require(not args.output.exists(), 'Audit output must be new')
        args.output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print('Asset usage integrity passed: {surfaceCount} surfaces; {sceneReferencedFrames}/{packageFrames} frames referenced; '
          '{uninstalledButtonFrames} uninstalled button frames; {stagedFrames} packaged unbound frames; effective-role and visual gates remain open.'.format(**report))
    require(not args.require_complete or report['requirementCoverageComplete'], 'Effective-scope acceptance is incomplete; inspect outstanding and remainingAcceptance')


if __name__ == '__main__':
    main()
