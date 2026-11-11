#!/usr/bin/env python3
"""Verify additive shop art, binary alpha, reproducibility and Zl index migration."""
import argparse
from collections import deque
import hashlib
import json
from pathlib import Path
import struct
import numpy as np
from PIL import Image

SOURCE_SHA = 'f574ddf094e5f1a0b4d78ccd31da1f9e9b3402ec6be8f9fb9b025358239c85d1'
OLD_PACKAGE_SHA = 'b469289d105e2404361033c81869b610399d7610edee5143dd7e62d1803ccfa7'
OLD_COMPOSED_SHA = '4b424d964b2b93412d51500e0eb7886b7daf74c841486821a836b6d6bd2ca182'

def sha(raw):
    return hashlib.sha256(raw).hexdigest()

def load(file):
    return json.loads(file.read_text(encoding='utf-8-sig'))

def require(value, message):
    if not value:
        raise ValueError(message)

def rgba(file):
    with Image.open(file) as image:
        return np.array(image.convert('RGBA'))

def alpha_reference(source):
    # Four-connected flood fill from exact-black edge pixels. Interior black stays opaque.
    h, w = source.shape[:2]
    black = np.all(source[:, :, :3] == 0, axis=2)
    outside = np.zeros((h, w), dtype=bool)
    queue = deque()
    for y, x in [(y, x) for y in (0, h - 1) for x in range(w)] + [(y, x) for x in (0, w - 1) for y in range(h)]:
        if black[y, x] and not outside[y, x]:
            outside[y, x] = True
            queue.append((y, x))
    while queue:
        y, x = queue.popleft()
        for yy, xx in ((y-1, x), (y+1, x), (y, x-1), (y, x+1)):
            if 0 <= yy < h and 0 <= xx < w and black[yy, xx] and not outside[yy, xx]:
                outside[yy, xx] = True
                queue.append((yy, xx))
    expected = source.copy()
    expected[:, :, 3] = np.where(outside, 0, 255)
    # RGB under transparent texels is deliberately canonical zero.
    expected[outside] = 0
    return expected

def verify_shop_entry(entry):
    require(entry['sourceLibrary'] == 'UI' and entry['sourceIndex'] == 511, 'Shop source identity changed')
    require(entry['logicalSize'] == [344, 438] and entry['sourceOffset'] == [0, 0], 'Shop source geometry changed')
    require(entry.get('sourcePngSha256', entry.get('pngSha256')) == SOURCE_SHA, 'Shop source hash changed')

def by_role(assets):
    mapping = {a['role']: a for a in assets}
    require(len(mapping) == len(assets), 'Duplicate role')
    return mapping

def package_frames(directory, manifest):
    raw = (directory / 'OfficialV1.Zl').read_bytes()
    prepared = load(directory / 'prepared-manifest.json')
    require(sha(raw) == prepared['sha256'], 'Zl digest differs')
    require(prepared['assets'] == manifest['assets'], 'Package/catalog frame map differs')
    header, count = struct.unpack_from('<ii', raw)
    require(count == len(manifest['assets']) and header == 4 + 26 * count, 'Zl table size differs')
    require([a['outputIndex'] for a in manifest['assets']] == list(range(count)), 'Catalog indices are not contiguous')
    frames, cursor = {}, header + 4
    for index, entry in enumerate(manifest['assets']):
        at = 8 + 26 * index
        require(raw[at] == 1, 'Missing Zl frame')
        start, w, h, ox, oy, shadow, sw, sh, sx, sy, ow, oh = struct.unpack_from('<ihhhhBhhhhhh', raw, at + 1)
        size = ((w+3)//4)*((h+3)//4)*8
        require([w, h] == entry['logicalSize'] and (ox,oy,shadow,sw,sh,sx,sy,ow,oh) == (0,)*9, 'Zl frame geometry differs')
        require(start == cursor and start + size <= len(raw), 'Overlapping or truncated Zl payload')
        frames[entry['role']] = raw[start:start+size]
        cursor += size
    require(cursor == len(raw), 'Zl trailing bytes')
    return raw, frames

def same_bytes(actual, expected, reason):
    require(actual == expected, reason)

def verify(args):
    root, work, old = args.root, args.work, args.previous
    output = args.output
    require(not output.exists(), 'Verification output must be new')
    selection_path = root / 'build/game-ui/npc-shop-selection.json'
    recipe_path = root / 'build/game-ui/npc-shop-composition-recipe.json'
    selection, recipe = load(selection_path), load(recipe_path)
    # Compare immutable previous output, never the active selection that advances on binding.
    prior_path = old / 'art/composed-manifest.json'
    require(sha(prior_path.read_bytes()) == OLD_COMPOSED_SHA, 'Previous composed baseline differs')
    prior = load(prior_path)
    previous_sources = {}
    for asset in prior['assets']:
        identity = {k: asset[k] for k in ('sourceLibrary', 'sourceIndex', 'sourceOffset', 'sourcePngSha256')}
        role = asset['sourceRole']
        require(role not in previous_sources or previous_sources[role] == identity, 'Conflicting previous source: '+role)
        previous_sources[role] = identity
    selected, previous_selected = by_role(selection['assets']), previous_sources
    require(len(selected) == 30 and set(selected) == set(previous_selected) | {'npc-shop'}, 'Selection must add only shop')
    require(selection['productionBindingAllowed'] is False and recipe['productionBindingAllowed'] is False, 'Candidate became production eligible')
    require(recipe['selectionSha256'] == sha(selection_path.read_bytes()), 'Recipe selection hash differs')
    for role, asset in previous_selected.items():
        for key in ('sourceLibrary', 'sourceIndex', 'sourceOffset'):
            require(selected[role][key] == asset[key], 'Previous source changed: '+role+'/'+key)
        require(selected[role]['pngSha256'] == asset['sourcePngSha256'], 'Previous source digest changed: '+role)
        source_file = work / 'decoded' / selected[role]['sourceLibrary'] / (str(selected[role]['sourceIndex'])+'.png')
        require(sha(source_file.read_bytes()) == selected[role]['pngSha256'], 'Selected source digest differs: '+role)
        require(list(rgba(source_file).shape[1::-1]) == selected[role]['logicalSize'], 'Selected source size differs: '+role)
    require(all(not a.get('captureId', '').startswith('06') for a in selected.values()), 'Ignored capture06 referenced')
    verify_shop_entry(selected['npc-shop'])
    shop_recipe = next(a for a in recipe['assets'] if a['role'] == 'npc-shop')
    require(shop_recipe == dict(role='npc-shop', sourceRole='npc-shop', alphaPolicy='edge-connected-exact-black', clearRects=[], transform={'type':'identity'}, button=False), 'Shop transform or alpha policy changed')
    source = work / 'decoded/UI/511.png'
    require(sha(source.read_bytes()) == SOURCE_SHA, 'Decoded shop source differs')
    source_pixels = rgba(source)
    require(source_pixels.shape == (438,344,4), 'Decoded source size differs')
    expected = alpha_reference(source_pixels)
    manifest = load(work / 'art/composed-manifest.json')
    assets, old_assets = by_role(manifest['assets']), by_role(prior['assets'])
    require(len(assets) == 93 and len(old_assets) == 92 and set(assets) == set(old_assets) | {'npc-shop'}, 'Composed role set changed')
    for key in ('runtimeReady', 'productionBindingAllowed'):
        require(manifest[key] is False, 'Unaccepted manifest gate opened: '+key)
    require(manifest['recipeSha256'] == sha(recipe_path.read_bytes()), 'Composed recipe hash differs')
    require(manifest['sourceManifestSha256'] == sha((work/'decoded/manifest.json').read_bytes()), 'Composed source hash differs')
    for key in ('buttons','grids','compositions'):
        require(manifest[key] == prior[key], 'Existing binding semantics changed: '+key)
    require(len(manifest['buttons']) == 19, 'Button state set count differs')
    require(len(manifest['grids']['inventory']['cells']) == 49 and len(manifest['grids']['quickbar-base']['cells']) == 10, 'Cell count changed')
    shop = assets['npc-shop']
    verify_shop_entry(shop)
    actual = rgba(work/'art'/shop['png'])
    require(np.array_equal(actual, expected), 'Shop pixel/alpha transform differs from independent reference')
    png_count = 0
    migrations = []
    for role, entry in assets.items():
        file = work/'art'/entry['png']
        require(sha(file.read_bytes()) == entry['pngSha256'] and sha(rgba(file).tobytes()) == entry['rgbaSha256'], 'Asset digest differs: '+role)
        same_bytes(file.read_bytes(), (work/'art-repeat'/entry['png']).read_bytes(), 'Repeat PNG differs: '+role)
        png_count += 1
        if role in old_assets:
            same_bytes(file.read_bytes(), (old/'art'/old_assets[role]['png']).read_bytes(), 'Existing PNG changed: '+role)
            if old_assets[role]['outputIndex'] != entry['outputIndex']:
                migrations.append(dict(role=role, previousIndex=old_assets[role]['outputIndex'], candidateIndex=entry['outputIndex']))
    same_bytes((work/'art/composed-manifest.json').read_bytes(), (work/'art-repeat/composed-manifest.json').read_bytes(), 'Repeat manifest differs')
    decoded_files = sorted(p for p in (work/'decoded').rglob('*') if p.is_file())
    for file in decoded_files:
        same_bytes(file.read_bytes(), (work/'decoded-repeat'/file.relative_to(work/'decoded')).read_bytes(), 'Repeated selected decode differs: '+file.name)
    new_raw, new_frames = package_frames(work/'zl', manifest)
    old_raw, old_frames = package_frames(old/'zl', prior)
    require(sha(old_raw) == OLD_PACKAGE_SHA, 'Previous package differs from fixed baseline')
    same_bytes(new_raw, (work/'zl-repeat/OfficialV1.Zl').read_bytes(), 'Repeat Zl differs')
    same_bytes((work/'zl/prepared-manifest.json').read_bytes(), (work/'zl-repeat/prepared-manifest.json').read_bytes(), 'Repeat prepared manifest differs')
    for role, raw in old_frames.items():
        same_bytes(raw, new_frames[role], 'Existing compressed payload changed: '+role)
    require(migrations, 'Expected index migration was not recorded')
    bc1 = load(work/'bc1/verification.json')
    require(bc1['frames'] == 93 and bc1['releaseAccepted'] is False and bc1['zlSha256'] == sha(new_raw), 'Independent BC1 evidence mismatch')
    require((work/'render-verified/npc-shop.png').is_file(), 'GPU shop render missing')
    # Meaningful failure controls: wrong source, lossy preservation, alpha leak, old catalog paired with new package.
    negatives = []
    def rejects(label, action):
        try:
            action()
        except ValueError:
            negatives.append(label)
        else:
            raise AssertionError('Negative control was accepted: '+label)
    rejects('wrong source index', lambda: verify_shop_entry(dict(shop, sourceIndex=512)))
    rejects('wrong source digest', lambda: verify_shop_entry(dict(shop, sourcePngSha256='0'*64)))
    rejects('existing compressed payload corruption', lambda: same_bytes(new_frames['character']+b'\0', old_frames['character'], 'Payload mismatch'))
    transparent = np.argwhere(expected[:,:,3] == 0)
    require(len(transparent) > 0, 'Shop fixture has no transparency')
    broken = actual.copy()
    y,x = transparent[0]
    broken[y,x,3] = 255
    rejects('opaque edge leak', lambda: require(np.array_equal(broken, expected), 'Alpha mismatch'))
    rejects('stale 92-frame catalog against 93-frame package', lambda: package_frames(work/'zl', prior))
    interior_black = np.all(source_pixels[:,:,:3] == 0, axis=2) & (expected[:,:,3] == 255)
    if np.any(interior_black):
        broken = actual.copy()
        broken[interior_black,3] = 0
        rejects('interior black erased', lambda: require(np.array_equal(broken, expected), 'Interior black mismatch'))
    result = dict(schemaVersion=1, kind='npc-shop-additive-asset-verification', status='pass', releaseAccepted=False,
                  runtimeReady=False, productionBindingAllowed=False, selectedSources=30, frames=93, buttonStateSets=19,
                  existingPngsByteIdentical=92, existingBc1PayloadsByteIdentical=92, repeatedPngsByteIdentical=png_count,
                  repeatedDecodedFilesByteIdentical=len(decoded_files), repeatedPackageByteIdentical=True,
                  sourceSha256=SOURCE_SHA, previousPackageSha256=sha(old_raw), candidatePackageSha256=sha(new_raw),
                  sourceLogicalSize=[344,438], textureSize=shop['textureSize'],
                  shopTransparentPixels=int((expected[:,:,3] == 0).sum()), shopOpaqueBlackInteriorPixels=int(interior_black.sum()),
                  indexMigrations=migrations, newShopIndex=shop['outputIndex'], oldCatalogCompatible=False,
                  negativeControls=negatives, negativeControlsPassed=len(negatives),
                  previousComposedSha256=OLD_COMPOSED_SHA,
                  note='93-frame candidate preserves the fixed previous 92-frame output. Current runtime catalog and window binding require separate verification.')
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(f"NPC shop: {len(assets)} frames; 92 existing PNGs and BC1 payloads unchanged; {len(migrations)} index migrations; {len(negatives)} negative controls passed.")
    return result

if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('root','work','previous','output'):
        parser.add_argument('--'+name, type=Path, required=True)
    verify(parser.parse_args())
