"""Verify real-art surface additions without changing prior captures or packages."""
import argparse
import hashlib
import json
from pathlib import Path
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


def verify(args):
    require(not args.output.exists(), 'Verification output already exists')
    art, package = load(args.art/'composed-manifest.json'), load(args.package/'prepared-manifest.json')
    previous = load(args.previous_art/'composed-manifest.json')
    recipe, layout = load(args.recipe), load(args.layout)
    require(tree(args.art) == tree(args.repeat_art), 'Composed builds differ')
    require(tree(args.package) == tree(args.repeat_package), 'Zl builds differ')
    require(art['recipeSha256'] == sha(args.recipe) and package['sourceManifestSha256'] == sha(args.art/'composed-manifest.json'), 'Build provenance mismatch')
    require(package['sha256'] == sha(args.package/'OfficialV1.Zl'), 'Package hash mismatch')
    require(art['assets'][:len(previous['assets'])] == previous['assets'], 'Existing frame descriptors changed')
    for asset in previous['assets']:
        require(sha(args.art/asset['png']) == sha(args.previous_art/asset['png']), 'Existing art changed: '+asset['role'])
    roles = {'chat-history-frame': 'chat', 'chat-input-frame': 'chatInput', 'hud-command-frame': 'commands'}
    require({f['role'] for f in recipe['derivedFrames']} == set(roles), 'Unexpected derived surfaces')
    assets = {a['role']: a for a in art['assets']}
    checked, surfaces = 0, []
    for frame in recipe['derivedFrames']:
        role, owner = frame['role'], roles[frame['role']]
        record, source = assets[role], assets[frame['sourceRole']]
        require(record['logicalSize'] == layout['owners'][owner]['size'] == frame['size'], role+': logical layout mismatch')
        require(record['sourcePngSha256'] == source['sourcePngSha256'] and record['sourceIndex'] == source['sourceIndex'] and record['sourceOffset'] == source['sourceOffset'], role+': lost source provenance')
        require(record['outputOffset'] == [0,0] and record['pivot'] == [0,0] and record['offsetMode'] == 'baked', role+': nonzero output placement')
        with Image.open(args.art/record['png']) as output, Image.open(args.art/source['png']) as original:
            image, trim = output.convert('RGBA'), original.convert('RGBA')
            w,h = image.size
            require(list(image.size) == frame['size'] and sha(args.art/record['png']) == record['pngSha256'], role+': PNG mismatch')
            alpha = image.getchannel('A')
            require(set(alpha.tobytes()) <= {0,255}, role+': fractional alpha')
            require(frame['sliceInsets'] == [1,1,1,1], role+': unexpected trim size')
            require(not any(alpha.crop((1,1,w-1,h-1)).tobytes()), role+': interior covers live controls')
            for target, original_point in [((0,0),(0,0)),((w-1,0),(trim.width-1,0)),((0,h-1),(0,trim.height-1)),((w-1,h-1),(trim.width-1,trim.height-1))]:
                require(image.getpixel(target) == trim.getpixel(original_point), role+': corner changed')
            centre = (w-2)*(h-2)
            checked += centre
            surfaces.append(dict(role=role,owner=owner,size=[w,h],transparentInteriorPixelsChecked=centre,pngSha256=record['pngSha256']))
    result = dict(schemaVersion=1,kind='official-ui-hud-surfaces-verification',releaseAccepted=False,
        deterministicArtFiles=len(tree(args.art)),deterministicPackageFiles=len(tree(args.package)),
        unchangedPriorFrames=len(previous['assets']),frames=len(art['assets']),
        transparentInteriorPixelsChecked=checked,surfaces=surfaces,zlSha256=package['sha256'])
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(f"HUD surfaces: {len(surfaces)} logical frames; {checked} transparent interior pixels; {len(previous['assets'])} prior frames unchanged; both art and Zl builds byte-identical.")


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('art','repeat-art','package','repeat-package','previous-art','recipe','layout','output'):
        parser.add_argument('--'+name,required=True,type=Path)
    verify(parser.parse_args())
