"""Independently verify character repaint, slot art and deterministic real Zl builds."""
import argparse
import hashlib
import json
from pathlib import Path
import numpy as np
from PIL import Image


def require(value, message):
    if not value:
        raise ValueError(message)


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def load(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def tree(directory):
    return {str(p.relative_to(directory)): sha(p) for p in directory.rglob('*') if p.is_file()}


def pixels(path):
    with Image.open(path) as image:
        return np.array(image.convert('RGBA'))


def verify(args):
    require(not args.output.exists(), 'Verification output already exists')
    art, package = load(args.art/'composed-manifest.json'), load(args.package/'prepared-manifest.json')
    previous, recipe, layout = load(args.previous_art/'composed-manifest.json'), load(args.recipe), load(args.layout)
    require(tree(args.art) == tree(args.repeat_art), 'Composed builds differ')
    require(tree(args.package) == tree(args.repeat_package), 'Zl builds differ')
    require(art['recipeSha256'] == sha(args.recipe), 'Recipe provenance mismatch')
    require(package['sourceManifestSha256'] == sha(args.art/'composed-manifest.json'), 'Package provenance mismatch')
    require(package['sha256'] == sha(args.package/'OfficialV1.Zl'), 'Package hash mismatch')
    for manifest in (art, package):
        require(all(manifest[k] is False for k in ('runtimeReady', 'productionBindingAllowed', 'alphaVerified')), 'Acceptance flags changed')
    records, old_records = {a['role']: a for a in art['assets']}, {a['role']: a for a in previous['assets']}
    require(set(records) == set(old_records) | {'equipment-cell-frame'}, 'Unexpected frame additions/removals')
    unchanged = 0
    for role, record in records.items():
        require(record['pngSha256'] == sha(args.art/record['png']), role+': art hash mismatch')
        if role in old_records and role != 'character':
            require(record == old_records[role] and sha(args.art/record['png']) == sha(args.previous_art/record['png']), role+': unrelated frame changed')
            unchanged += 1
    record, old_record = records['character'], old_records['character']
    for key in ('logicalSize', 'textureSize', 'sourceLibrary', 'sourceIndex', 'sourcePngSha256', 'sourceOffset', 'outputOffset', 'pivot', 'offsetMode', 'alphaMode', 'blendMode'):
        require(record[key] == old_record[key], 'Character descriptor drift: '+key)
    before, after = pixels(args.previous_art/old_record['png']), pixels(args.art/record['png'])
    require(list(after.shape[1::-1]) == layout['owners']['character']['size'] == [412,562], 'Character layout dimensions changed')
    require(np.array_equal(before[:,:,3], after[:,:,3]), 'Character silhouette alpha changed')
    transform = next(a for a in recipe['assets'] if a['role'] == 'character')['transform']
    require(transform == dict(type='texture-cover',sourcePatch=[24,452,56,72],targets=[[8,90,396,461]]), 'Unexpected repaint geometry')
    patch = before[452:524,24:80]
    require(np.all(patch[:,:,3] == 255), 'Texture sample is not opaque')
    yy, xx = np.indices((461,396))
    require(np.array_equal(after[90:551,8:404], patch[yy%72,xx%56]), 'Character repaint contains non-source pixels or old wells')
    outside = np.ones(before.shape[:2],dtype=bool); outside[90:551,8:404] = False
    require(np.array_equal(before[outside],after[outside]), 'Untargeted character chrome changed')
    slot = records['equipment-cell-frame']; inventory = records['inventory']
    derivation = next(f for f in recipe['derivedFrames'] if f['role'] == 'equipment-cell-frame')
    require(derivation == dict(type='crop-frame',role='equipment-cell-frame',sourceRole='inventory',sourceArea=[15,49,36,36]), 'Unexpected slot derivation')
    require(list(slot['logicalSize']) == [36,36] and slot['sourceLibrary'] == inventory['sourceLibrary'] and slot['sourceIndex'] == inventory['sourceIndex'] and slot['sourcePngSha256'] == inventory['sourcePngSha256'], 'Slot source provenance mismatch')
    require(slot['outputOffset'] == [0,0] and slot['pivot'] == [0,0] and slot['offsetMode'] == 'baked', 'Slot placement offset changed')
    require(np.array_equal(pixels(args.art/slot['png']), pixels(args.art/inventory['png'])[49:85,15:51]), 'Slot pixels differ from native adapted inventory cell')
    cells = [n for n in layout['nodes'] if n.get('owner') == 'character' and n.get('action') == 'existing-equipment-cell']
    require(sorted(c['slotId'] for c in cells) == list(range(16)), 'Slot identities missing/duplicated')
    counts = {p['id']: 0 for p in layout['pages']}
    for cell in cells:
        x,y,w,h = cell['rect']
        require([w,h] == slot['logicalSize'] and cell['hitRect'] == cell['rect'] and cell['binding'] == 'Grid['+str(cell['slotId'])+']', 'Slot geometry/binding changed')
        require(8 <= x and 90 <= y and x+w <= 404 and y+h <= 551, 'Slot lies outside cleaned surface')
        counts[cell['page']] += 1
    require(counts == dict(character=9,extraEquipment=7,status=0,hermit=0), 'Equipment page ownership changed')
    result = dict(schemaVersion=1,kind='official-ui-character-surfaces-art-verification',releaseAccepted=False,
        deterministicArtFiles=len(tree(args.art)),deterministicPackageFiles=len(tree(args.package)),
        unchangedPriorFrames=unchanged,frames=len(art['assets']),
        character=dict(size=record['logicalSize'],repaintedPixels=396*461,unchangedChromePixels=int(outside.sum()),
            unchangedAlphaPixels=412*562,sourceLibrary=record['sourceLibrary'],sourceIndex=record['sourceIndex'],
            sourcePatch=transform['sourcePatch'],targets=transform['targets'],pngSha256=record['pngSha256']),
        equipmentFrame=dict(size=slot['logicalSize'],nativePixelsChecked=36*36,sourceLibrary=slot['sourceLibrary'],sourceIndex=slot['sourceIndex'],pngSha256=slot['pngSha256']),
        equipmentCells=len(cells),equipmentPageCounts=counts,zlSha256=package['sha256'])
    args.output.parent.mkdir(parents=True,exist_ok=True)
    args.output.write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(f"Character surfaces: {396*461} repainted pixels; {int(outside.sum())} chrome pixels unchanged; {412*562} alpha pixels unchanged; native 36x36 slot; 16 unique equipment bindings; {unchanged} prior frames unchanged; art and Zl builds byte-identical.")


if __name__ == '__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    for name in ('art','repeat-art','package','repeat-package','previous-art','recipe','layout','output'):
        parser.add_argument('--'+name,required=True,type=Path)
    verify(parser.parse_args())
