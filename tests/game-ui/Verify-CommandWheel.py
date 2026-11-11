#!/usr/bin/env python3
"""Independently verify the native command-shell adaptation and deterministic builds."""
import argparse
import hashlib
import json
from pathlib import Path
from PIL import Image, ImageDraw


def require(value, message):
    if not value:
        raise ValueError(message)


def sha(file):
    return hashlib.sha256(file.read_bytes()).hexdigest()


def load(file):
    return json.loads(file.read_text(encoding='utf-8-sig'))


def tree(directory):
    return {p.relative_to(directory).as_posix(): sha(p) for p in directory.rglob('*') if p.is_file()}


def independent_mask(image):
    rgb = image.convert('RGB')
    flags = Image.new('L', rgb.size)
    width, height = rgb.size
    rgb_pixels = rgb.load()
    flags.putdata([1 if rgb_pixels[x, y] == (0, 0, 0) else 0 for y in range(height) for x in range(width)])
    edge = [(x, y) for x in range(width) for y in (0, height-1)]
    edge += [(x, y) for y in range(height) for x in (0, width-1)]
    for point in edge:
        if flags.getpixel(point) == 1:
            ImageDraw.floodfill(flags, point, 2)
    rgba = rgb.convert('RGBA')
    rgba.putalpha(flags.point(lambda value: 0 if value == 2 else 255))
    return rgba


def verify(args):
    require(not args.output.exists(), 'Use a fresh verification output')
    art = load(args.art/'composed-manifest.json')
    previous = load(args.previous_art/'composed-manifest.json')
    package = load(args.package/'prepared-manifest.json')
    previous_package = load(args.previous_package/'prepared-manifest.json')
    require(previous_package['sourceManifestSha256'] == sha(args.previous_art/'composed-manifest.json'), 'Previous art does not belong to previous package')
    require(previous_package['sha256'] == sha(args.previous_package/'OfficialV1.Zl'), 'Previous package digest mismatch')
    decoded = load(args.decoded/'manifest.json')
    require(tree(args.art) == tree(args.repeat_art), 'Repeat composed builds differ')
    require(tree(args.package) == tree(args.repeat_package), 'Repeat Zl builds differ')
    require(package['sha256'] == sha(args.package/'OfficialV1.Zl'), 'Zl digest mismatch')
    require(package['sourceManifestSha256'] == sha(args.art/'composed-manifest.json'), 'Art/package provenance mismatch')
    require(package['runtimeReady'] is False and package['productionBindingAllowed'] is False, 'Acceptance flags changed')
    current = {a['role']: a for a in art['assets']}
    old = {a['role']: a for a in previous['assets']}
    require(set(current) == set(old) and len(current) == 92, 'Frame roles changed')
    changed = [role for role in current if sha(args.art/current[role]['png']) != sha(args.previous_art/old[role]['png'])]
    require(changed == ['hud-command-frame'], 'Unrelated artwork changed: '+repr(changed))
    require(art['buttons'] == previous['buttons'] and len(art['buttons']) == 19, 'Button state roles changed')
    require(art['grids'] == previous['grids'] and art['compositions'] == previous['compositions'], 'Grid/fill contracts changed')
    source = decoded['semanticRoles']['hud-command-shell']
    frame = current['hud-command-frame']
    require(source['library'] == 'UI' and source['index'] == 140, 'Unexpected adapted source')
    source_path = args.decoded/source['png']
    require(sha(source_path) == 'a22ce21539d63f2b050fe7c026abc0f2789376efa0fd56d2dfafb3e7fc9fbfd0', 'Native PNG changed')
    require(frame['sourceIndex'] == 140 and frame['sourceOffset'] == [-112, -87] and frame['outputOffset'] == [0, 0], 'Source/baked offsets changed')
    with Image.open(source_path) as image:
        require(image.size == (225, 175), 'Native shell dimensions changed')
        expected = independent_mask(image)
    with Image.open(args.art/frame['png']) as image:
        candidate = image.convert('RGBA')
    require(candidate.size == (225, 202), 'Command owner dimensions changed')
    require(candidate.crop((0, 0, 225, 175)).tobytes() == expected.tobytes(), 'Native shell pixels scaled, moved or changed')
    require(candidate.crop((0, 175, 225, 202)).tobytes() == bytes(225*27*4), 'Canvas padding is not transparent black')
    native_pixels = expected.load()
    opaque_black = sum(native_pixels[x, y] == (0, 0, 0, 255) for y in range(175) for x in range(225))
    result = dict(schemaVersion=1, kind='official-ui-command-wheel-verification', releaseAccepted=False,
                  unchangedArtFrames=91, changedArtRoles=changed, buttonStateSets=19,
                  sourceLibrary='UI', sourceIndex=140, sourceSize=[225, 175], outputSize=[225, 202], origin=[0, 0],
                  exactNativeRgbaPixels=225*175, transparentCanvasPixels=225*27,
                  opaqueBlackInteriorPixelsPreserved=opaque_black, sourcePngSha256=sha(source_path),
                  artManifestSha256=sha(args.art/'composed-manifest.json'), packageSha256=sha(args.package/'OfficialV1.Zl'),
                  deterministicArt=True, deterministicPackage=True,
                  sourceIdentification='explicit native-art adaptation; UI[140] versus UI[1270] is not uniquely resolved by screenshots')
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2)+'\n', encoding='utf-8')
    print('Command wheel: 91 other art frames unchanged; native 225x175 RGBA exact; 6075 transparent canvas pixels; repeat art/Zl byte-identical.')
    return result


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('art', 'previous-art', 'previous-package', 'repeat-art', 'package', 'repeat-package', 'decoded', 'output'):
        parser.add_argument('--'+name, type=Path, required=True)
    verify(parser.parse_args())
