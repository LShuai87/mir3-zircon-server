#!/usr/bin/env python3
"""Verify native UI[2765] preservation against the preceding staged artwork/package."""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
from PIL import Image, ImageChops, ImageDraw

ROOT = Path(__file__).resolve().parents[2]

def require(value, reason):
    if not value:
        raise ValueError(reason)

def load_module(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module

def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def read(path):
    return json.loads(path.read_text(encoding='utf-8'))

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for option in ('source', 'art', 'package', 'previous-art', 'previous-package', 'output'):
        parser.add_argument('--' + option, required=True, type=Path)
    args = parser.parse_args()
    recipe = read(ROOT / 'build/game-ui/composition-recipe.json')
    layout = read(ROOT / 'build/game-ui/layout-official-v1.json')
    art = read(args.art / 'composed-manifest.json')
    previous = read(args.previous_art / 'composed-manifest.json')
    records = {a['role']: a for a in art['assets']}
    old_records = {a['role']: a for a in previous['assets']}
    role = 'character-main-background'
    require(len(records) == len(art['assets']) == 94 and set(records) == set(old_records), 'Unexpected artwork role set')
    require(art['recipeSha256'] == sha(ROOT / 'build/game-ui/composition-recipe.json'), 'Recipe hash drift')
    require(not art['runtimeReady'] and not art['productionBindingAllowed'], 'Preview acceptance flags changed')
    unchanged = []
    for name, record in records.items():
        require(sha(args.art / record['png']) == record['pngSha256'], name + ': PNG hash mismatch')
        if name != role:
            require(record == old_records[name], name + ': unrelated descriptor changed')
            require((args.art / record['png']).read_bytes() == (args.previous_art / record['png']).read_bytes(), name + ': unrelated pixels changed')
            unchanged.append(name)
    record = records[role]
    require(record['sourceIndex'] == 2765 and record['sourceLibrary'] == 'UI', 'Wrong character source')
    require(record['sourcePngSha256'] == sha(args.source), 'Source provenance mismatch')
    source = Image.open(args.source).convert('RGBA')
    require(source.size == (412, 562), 'Source dimensions changed')
    compose = load_module('native_character_compose', ROOT / 'build/game-ui/Compose-OfficialUi.py')
    native = compose.mask_edges(source, []).crop((0, 64, 412, 562))
    actual = Image.open(args.art / record['png']).convert('RGBA')
    require(actual.size == native.size == (412, 498), 'Background dimensions changed')
    transform = next(a for a in recipe['assets'] if a['role'] == role)['transform']
    patches = []
    require(transform == {'type':'crop','sourceArea':[0,64,412,498]}, 'Native background must use a lossless crop')
    outside = Image.new('L', actual.size, 255)
    require(actual.tobytes() == native.tobytes(), 'Native artwork changed inside the character page')
    require(actual.getchannel('A').tobytes() == native.getchannel('A').tobytes(), 'Native alpha changed')
    cells = [n for n in layout['nodes'] if n['id'].startswith('character.equipment.')]
    require(sorted(n['slotId'] for n in cells) == list(range(16)) and all(n['page']=='character' for n in cells), 'Equipment must remain together on homepage')
    require([p['id'] for p in layout['pages']] == ['character','status','hermit'], 'Unexpected extra page')
    tabs = [n for n in layout['nodes'] if n['id'].startswith('character.tab.')]
    require(len(tabs) == 3 and {n['targetPage'] for n in tabs} == {'character','status','hermit'}, 'Unexpected tabs')
    zl = load_module('native_character_zl', ROOT / 'build/game-ui/Prepare-UiCatalog.py')
    count, frames = zl.read_zl(args.package.read_bytes())
    old_count, old_frames = zl.read_zl(args.previous_package.read_bytes())
    require(count == old_count == 94 and set(frames) == set(old_frames), 'Package frame set changed')
    for name in unchanged:
        index = records[name]['outputIndex']
        require({k:v for k,v in frames[index].items() if k!='position'} == {k:v for k,v in old_frames[index].items() if k!='position'}, name + ': unrelated packaged frame changed')
    require(frames[record['outputIndex']]['data'] != old_frames[record['outputIndex']]['data'], 'Native background was not packaged')
    result = dict(schemaVersion=1, result='pass', kind='native-character-2765-verification', sourceSha256=sha(args.source),
        packageSha256=sha(args.package), previousPackageSha256=sha(args.previous_package),
        sourceSize=list(source.size), nativePixelsPreserved=outside.histogram()[255], localRepairs=patches,
        untouchedArtworkFrames=len(unchanged), untouchedPackageFrames=len(unchanged), equipmentCellsOnHomepage=len(cells),
        pages=[p['id'] for p in layout['pages']], productionBindingAllowed=False,
        scope='PNG/native-pixel and packaged-frame preservation; synthetic runtime/interaction evidence is recorded separately.')
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(result, indent=2))

if __name__ == '__main__':
    main()
