"""Append reviewed native character companion artwork, preserving existing package frames.

The input art directory must be the prior package's composed art. Source libraries are
read-only. A new output directory is required; existing art is never rewritten.
"""
import argparse, hashlib, importlib.util, json, shutil, struct
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('compose', ROOT/'build/game-ui/Compose-OfficialUi.py')
compose = importlib.util.module_from_spec(spec); spec.loader.exec_module(compose)
sha = lambda b: hashlib.sha256(b).hexdigest()
def write(p, d): p.write_bytes((json.dumps(d, ensure_ascii=False, indent=2)+'\n').encode('utf-8'))

def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--base-art', type=Path, required=True)
    ap.add_argument('--source', type=Path, required=True)
    ap.add_argument('--output', type=Path, required=True)
    args = ap.parse_args()
    if args.output.exists(): raise ValueError('Output must be new')
    base = json.loads((args.base_art/'composed-manifest.json').read_text(encoding='utf-8-sig'))
    recipe_path = ROOT/'build/game-ui/composition-recipe.json'
    selection_path = ROOT/'build/game-ui/reference-selection.json'
    recipe = json.loads(recipe_path.read_text(encoding='utf-8-sig'))
    selection = json.loads(selection_path.read_text(encoding='utf-8-sig'))
    roles = ['character-companion', 'character-companion-button']
    if any(a['role'] in roles for a in base['assets']): raise ValueError('Base already contains companions')
    mix = (args.source/'UI.MIX').read_bytes(); miz = (args.source/'UI.MIZ').read_bytes()
    sources = []
    for role, index in zip(roles, [130, 3304]):
        offset = struct.unpack_from('<I', mix, 24+index*4)[0]
        image, meta = compose.selected.decoder.decode_image(miz, offset)
        from io import BytesIO
        buffer = BytesIO(); image.save(buffer, format='PNG', compress_level=9)
        _, _, x, y = struct.unpack_from('<HHhh', miz, offset)
        sources.append(dict(role=role, sourceLibrary='UI', sourceIndex=index,
            logicalSize=list(image.size), sourceOffset=[x,y], pngSha256=sha(buffer.getvalue()),
            identification='read-only-native-library-review', captureId='character-companion', alphaVerified=False))
    selection['assets'] = [a for a in selection['assets'] if a['role'] not in roles] + sources
    write(selection_path, selection)
    recipe['selectionSha256'] = sha(selection_path.read_bytes())
    additions = [dict(role=roles[0], sourceRole=roles[0], alphaPolicy='edge-connected-exact-black',
        clearRects=[], transform=dict(type='identity'), button=False),
        dict(role=roles[1], sourceRole=roles[1], alphaPolicy='edge-connected-exact-black', clearRects=[],
        transform=dict(type='panel-resize', size=[56,22], sliceInsets=[4,4,4,4], sampling='nearest'), button=True)]
    recipe['assets'] = [a for a in recipe['assets'] if a['role'] not in roles] + additions
    write(recipe_path, recipe)
    # Keep the effective-scope provenance inputs current without altering their
    # independently reviewed warehouse, quickbar or character transforms.
    shop_path = ROOT/'build/game-ui/npc-shop-selection.json'
    shop = json.loads(shop_path.read_text(encoding='utf-8-sig'))
    shop['assets'] = [a for a in shop['assets'] if a['role'] not in roles] + sources
    write(shop_path, shop)
    combined_path = ROOT/'build/game-ui/warehouse-combined-composition-recipe.json'
    combined = json.loads(combined_path.read_text(encoding='utf-8-sig'))
    combined['assets'] = [a for a in combined['assets'] if a['role'] not in roles] + additions
    combined['selectionSha256'] = sha(shop_path.read_bytes())
    write(combined_path, combined)
    shutil.copytree(args.base_art, args.output)
    for source, asset in zip(sources, additions):
        offset = struct.unpack_from('<I', mix, 24+source['sourceIndex']*4)[0]
        image, _ = compose.selected.decoder.decode_image(miz, offset)
        image, _ = compose.transform(compose.mask_edges(image, []), asset['transform'])
        states = {'normal': asset['role']}
        images = [(asset['role'], image, None)]
        if asset['button']:
            for state, ratio in recipe['buttonTint'].items():
                role = asset['role']+'-'+state
                images.append((role, compose.tint(image, state, *ratio), state)); states[state]=role
            base['buttons'][asset['role']] = states
        for role, im, state in images:
            relative = 'Assets/'+role+'.png'; im.save(args.output/relative, format='PNG', compress_level=9)
            base['assets'].append(dict(role=role, outputIndex=len(base['assets']), png=relative,
                pngSha256=sha((args.output/relative).read_bytes()), rgbaSha256=sha(im.tobytes()),
                logicalSize=list(im.size), textureSize=[(x+3)//4*4 for x in im.size],
                sourceLibrary='UI', sourceIndex=source['sourceIndex'], sourcePngSha256=source['pngSha256'],
                sourceOffset=source['sourceOffset'], outputOffset=[0,0], pivot=[0,0], offsetMode='baked',
                alphaMode='BinaryMask', blendMode='Normal', sourceRole=source['role'], state=state))
    evidence = dict(kind='native-character-companion-source', source=str(args.source),
        mixSha256=sha(mix), mizSha256=sha(miz), sources=sources,
        baseManifestSha256=sha((args.base_art/'composed-manifest.json').read_bytes()),
        preservedFrames=len(base['assets'])-5, outputFrames=len(base['assets']))
    write(args.output/'companion-source.json', evidence)
    base['sourceManifestSha256']=sha((args.output/'companion-source.json').read_bytes())
    base['recipeSha256']=sha(recipe_path.read_bytes())
    write(args.output/'composed-manifest.json', base)
    assert sha((args.source/'UI.MIX').read_bytes())==sha(mix) and sha((args.source/'UI.MIZ').read_bytes())==sha(miz)
    print('Preserved %d frames; appended native UI[130] + UI[3304] states.' % evidence['preservedFrames'])
if __name__=='__main__': main()
