#!/usr/bin/env python3
"""Archive scoped runtime references and annotate originals; never controls the game."""
import argparse
import hashlib
import json
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[6]
REF = ROOT / 'docs/design/assets/official-ingame-ui/acceptance/reference'
HERE = Path(__file__).resolve().parent
IGNORED = '06-chat-chinese-input'


def load(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def require(condition, message):
    if not condition:
        raise ValueError(message)


def save(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')


def region(label, rect, kind='reference', note=''):
    return dict(label=label, rect=rect, kind=kind, note=note,
                precision='manual reference area; not a runtime hit rectangle')


def extras(capture_id):
    character = [region('Character preview', [30, 168, 172, 200], 'dynamic'),
        region('Equipment and tabs', [16, 165, 223, 290], 'action'),
        region('Main attributes', [280, 172, 121, 235], 'dynamic'),
        region('Element and combat attributes', [31, 475, 369, 120], 'dynamic'),
        region('Character close', [398, 88, 20, 20], 'action')]
    inventory = [region('Bag item grid (reference 6x8)', [754, 155, 246, 328], 'action'),
        region('Bag totals and actions', [751, 486, 249, 99], 'dynamic'),
        region('Bag close', [990, 117, 20, 20], 'action')]
    if capture_id == '01-overview':
        return [region('Chat history: keep project rendering', [224, 657, 574, 106], 'dynamic'),
            region('Chat input: keep HWND / IME / draft', [224, 763, 574, 17], 'action'),
            region('Minimap viewport: clear explicit hole', [870, 47, 150, 152], 'transparent'),
            region('Game world behind HUD cutouts', [1, 592, 225, 75], 'transparent',
                   'Only surrounding cutouts show through; enclosed black panels stay opaque.'),
            region('World / quest overlays: outside this skin', [854, 230, 171, 244], 'excluded')]
    if capture_id == '02-character':
        return character
    if capture_id == '03-character-inventory':
        return character + inventory
    if capture_id == '04-equipment-tooltip':
        return character + inventory + [region('Item tooltip: dynamic project text', [754, 200, 170, 158], 'dynamic',
                'Overlays bag cells; screenshot does not establish a separate bitmap or fixed height.')]
    if capture_id == '05-npc-dialog-shop-inventory':
        return [region('NPC interaction card: preserve current style', [1, 26, 418, 195], 'excluded'),
            region('Independent NPC shop product list', [23, 272, 280, 215], 'action'),
            region('NPC service actions: preserve original handlers', [22, 616, 276, 24], 'action'),
            region('Bag item grid (reference 6x8)', [441, 157, 246, 328], 'action')]
    raise ValueError('Unexpected active reference: ' + capture_id)


def annotate(capture_path, capture_id, regions, output):
    with Image.open(capture_path) as src:
        image = src.convert('RGB')
    board = Image.new('RGB', (image.width + 440, max(image.height, 80 + len(regions)*30)), '#171e29')
    board.paste(image, (0, 0))
    draw = ImageDraw.Draw(board)
    font = ImageFont.load_default(size=13)
    palette = dict(reference='#51d4ff', action='#ffc65c', dynamic='#c99aff',
                   transparent='#50e0a1', excluded='#aaaaaa')
    draw.text((image.width+14, 12), capture_id, font=font, fill='white')
    draw.text((image.width+14, 34), 'ANNOTATED RUNTIME ORIGINAL / screenshot pixels', font=font, fill='#b7c9de')
    draw.text((image.width+14, 52), 'Rectangles locate references, not input hitboxes.', font=font, fill='#b7c9de')
    for i, r in enumerate(regions, 1):
        x,y,w,h = r['rect']
        require(x >= 0 and y >= 0 and w > 0 and h > 0 and x+w <= image.width and y+h <= image.height,
                'Reference region outside original: ' + r['label'])
        color = palette[r['kind']]
        draw.rectangle((x,y,x+w-1,y+h-1), outline=color, width=2)
        text = str(i)
        draw.rectangle((x+2,y+2,x+9+len(text)*8,y+19), fill='#101823')
        draw.text((x+4,y+3), text, font=font, fill=color)
        draw.text((image.width+14, 80+(i-1)*30), str(i) + '. ' + r['label'], font=font, fill=color)
    board.save(output, compress_level=9)


def alpha_board(images, output):
    roles = ['character', 'inventory', 'hud-orb-shell', 'hud-hp-fill', 'hud-mp-fill',
             'minimap-frame', 'quickbar-base', 'hud-command-frame', 'chat-history-frame']
    board = Image.new('RGB', (1020, 960), '#171e29')
    d = ImageDraw.Draw(board)
    font = ImageFont.load_default(size=13)
    d.text((12,10), 'CANDIDATE ALPHA PREVIEWS / not official runtime screenshots', font=font, fill='white')
    d.text((12,29), 'Checkerboard = alpha zero. Thumbnails only; source and output dimensions differ where adapted.', font=font, fill='#b7c9de')
    for i,role in enumerate(roles):
        x,y = (i%3)*340, 60+(i//3)*300
        image = images[role].copy()
        original_size = image.size
        image.thumbnail((320, 246), Image.Resampling.NEAREST)
        patch = Image.new('RGBA', image.size)
        q = ImageDraw.Draw(patch)
        for py in range(0,image.height,8):
            for px in range(0,image.width,8):
                c = '#8390a0' if (px//8+py//8)%2 else '#c4cedb'
                q.rectangle((px,py,px+7,py+7), fill=c)
        patch.alpha_composite(image)
        board.paste(patch.convert('RGB'), (x+10,y+34))
        d.text((x+10,y), role, font=font, fill='white')
        d.text((x+10,y+16), str(original_size[0])+'x'+str(original_size[1])+' output', font=font, fill='#b7c9de')
    board.save(output, compress_level=9)


def build(inspection, art, output):
    require(not output.exists(), 'Output must not exist; historical evidence is immutable')
    # Validate all sources before creating an output directory.
    spec_path = HERE / 'effective-matching-spec.json'
    matching_path = HERE / 'panel-matching.json'
    spec, matching = load(spec_path), load(matching_path)
    require(matching['specSha256'] == sha(spec_path), 'Matching spec hash mismatch')
    require(matching['scriptSha256'] == sha(REF/'Match-ReferencePanels.py'), 'Matching script hash mismatch')
    require(matching['inventorySha256'] == sha(inspection/'inventory.json'), 'Inspection hash mismatch')
    require(len(matching['matches']) == len(spec['matches']), 'Matching request count mismatch')
    captures = {c['id']:c for c in load(REF/'captures/capture-session.json')}
    inv = {lib['name']:lib for lib in load(inspection/'inventory.json')['libraries']}
    manifest_path = ROOT/'build/game-ui/asset-manifest.json'
    recipe_path = ROOT/'build/game-ui/composition-recipe.json'
    manifest, recipe = load(manifest_path), load(recipe_path)
    require(manifest['runtimeReady'] is False and manifest['productionBindingAllowed'] is False,
            'This reference report cannot authorize runtime activation')
    require(manifest['inputs']['compositionRecipeSha256'] == sha(recipe_path), 'Recipe hash mismatch')
    original_paths, regions, observations = {}, {}, []
    for request,m in zip(spec['matches'], matching['matches']):
        require(m['captureId'] != IGNORED, 'Ignored screenshot cannot be a reference')
        require((m['captureId'],m['role']) == (request['captureId'],request['role']), 'Matching order mismatch')
        c = captures[m['captureId']]
        original = REF/c['imageRelativePath']
        require(m['captureSha256'] == c['imageSha256'] == sha(original), 'Original capture hash mismatch')
        original_paths[m['captureId']] = original
        for candidate in m['candidates']:
            item = inv[candidate['library']]['items'][candidate['index']]
            require(candidate['pngSha256'] == item['pngSha256'] == sha(inspection/item['png']), 'Diagnostic PNG mismatch')
        best = m['candidates'][0]
        at = best['bestPlacements'][0]
        role = m['role']
        ambiguous = role == 'hud-command-shell'
        observation = dict(role=role, captureId=m['captureId'], sourceLibrary=best['library'],
            sourceIndex=best['index'], sourcePngSha256=best['pngSha256'],
            sourceSize=[best['width'],best['height']], sourceOffset=[best['offsetX'],best['offsetY']],
            screenshotBitmapRect=[at['x'],at['y'],best['width'],best['height']],
            meanAbsoluteChannelError=at['meanAbsoluteChannelError'],
            confidenceBoundary='local JPEG comparison; not client coordinates, alpha fitting or hit rectangles',
            identification='explicit source adaptation; 140/1270 visually ambiguous' if ambiguous else 'local visual correspondence')
        observations.append(observation)
        regions.setdefault(m['captureId'], []).append(region(role+' / '+best['library']+'['+str(best['index'])+']',
                observation['screenshotBitmapRect']))
    require(set(original_paths) == set(captures)-{IGNORED}, 'All five active runtime originals must be covered')
    availability = load(REF/'availability.json')
    require(availability['officialExecutable']['fileVersion'] == '1.15.0.7269' and
            availability['launcherExecutable']['fileVersion'] == '1.9.3.137', 'Runtime metadata version mismatch')
    active_hashes = {o['sourcePngSha256'] for o in observations}
    assets, images = [], {}
    for a in manifest['assets']:
        path = art/a['png']
        require(path.is_file() and sha(path) == a['pngSha256'], 'Candidate PNG hash mismatch: '+a['role'])
        with Image.open(path) as original:
            im = original.convert('RGBA')
        require(list(im.size) == a['logicalSize'], 'Logical PNG dimensions mismatch')
        alpha = im.getchannel('A').histogram()
        source = inv[a['sourceLibrary']]['items'][a['sourceIndex']]
        require(source['pngSha256'] == a['sourcePngSha256'] == sha(inspection/source['png']), 'Asset source mismatch')
        matched = a['sourcePngSha256'] in active_hashes
        require(matched or a['sourceRole']=='bottom-segmented-fill', 'Unexplained source without active reference: '+a['role'])
        assets.append(dict(role=a['role'],sourceRole=a['sourceRole'], outputIndex=a['outputIndex'],
            sourceLibrary=a['sourceLibrary'],sourceIndex=a['sourceIndex'],sourceSize=[source['width'],source['height']],
            sourcePngSha256=a['sourcePngSha256'],outputSize=a['logicalSize'],outputPngSha256=a['pngSha256'],
            screenshotEvidence='active runtime source correspondence' if matched else 'paired source art adaptation, not runtime identification',
            outputAlphaMode=a['alphaMode'],outputBlendMode=a['blendMode'],
            alphaPixels=dict(transparent=alpha[0],opaque=alpha[255],intermediate=sum(alpha[1:255])),
            opacityBoundary='candidate output mask; does not prove official runtime alpha semantics'))
        images[a['role']] = im
    require(len(assets)==manifest['package']['slotCount']==92, 'Expected pinned 92-frame candidate')
    output.mkdir(parents=True)
    view_records = []
    for capture_id,path in original_paths.items():
        view_regions = regions[capture_id] + extras(capture_id)
        image_path = output/(capture_id+'-annotated.png')
        annotate(path,capture_id,view_regions,image_path)
        view_records.append(dict(captureId=capture_id, originalPath=path.relative_to(ROOT).as_posix(),
            originalSha256=sha(path),originalPixelSize=captures[capture_id]['imagePixelSize'],
            annotatedImage=image_path.name,annotatedSha256=sha(image_path),regions=view_regions))
    alpha_board(images,output/'candidate-alpha-preview.png')
    result = dict(schemaVersion=1,kind='scoped-runtime-reference-map',checkedAtLocalDate='2026-11-08',
        configuredResolution=[1024,768],windowMode='windowed',
        metadata=dict(runtimeVersion='1.15.0.7269',launcherVersion='1.9.3.137',
            source='reference/availability.json and runtime-source-libraries.json',
            dpiPercent=None,measuredClientRect=None,
            screenshotPixels='1026x795 runtime originals; 1280x959 settings attachment; neither equals measured client bounds'),
        ignoredCaptureIds=[IGNORED], settingsCapture='reference/captures/07-resolution-settings-user.png',
        observations=observations,views=view_records,assets=assets,
        alphaPreview=dict(path='candidate-alpha-preview.png',sha256=sha(output/'candidate-alpha-preview.png')),
        layers=[dict(id='hud-orb',order=['world','orb-shell','HP bottom crop','MP bottom crop','dynamic labels'],
                    sourceOffsetRule='fill source offset minus shell offset, baked exactly once; not fitted from JPEG'),
                dict(id='chat',order=['container frame','existing project history/tab/filter controls','existing input HWND'],
                    scope='reposition and frame only; keep content, selection, floating and IME'),
                dict(id='minimap',order=['map Panel/Image and markers','frame with explicit viewport hole','title/coordinate/button controls']),
                dict(id='character',order=['adapted frame','existing preview/equipment/attributes','tooltip overlay']),
                dict(id='inventory',order=['adapted frame','49 existing item cells','tooltip overlay']),
                dict(id='npc',order=['original interaction card unchanged','independent service + inventory'],
                    scope='interaction card and secondary confirmation/quantity popups excluded')],
        adaptations=[dict(id='inventory',reference='UI[120], 276x494, 6x8 cells',project='276x412, 7x7, 49 cells; 36x36 globally'),
                     dict(id='character',reference='UI[2765], 412x562, official labels/slots',project='4 existing project tabs, all 16 equipment IDs and project attributes; no official slot numbering'),
                     dict(id='quickbar',reference='UI[430], 518x44',project='459x44, 10 existing links'),
                     dict(id='command-shell',reference='UI[140] and UI[1270] remain visually ambiguous',project='UI[140] deliberately selected; 225x175 pasted at (0,27) into 225x202 canvas'),
                     dict(id='experience',reference='UI[422] border is visible; UI[423] is paired source art',project='574x8 segmented track/fill bound to existing project experience'),
                     dict(id='button-states',reference='normal appearance only; no live hover/pressed/disabled transitions captured',project='explicit deterministic tint states; preserved existing handlers'),
                     dict(id='tooltip',reference='runtime text overlay in capture04, variable content',project='procedural project frame/layout retaining all original item text')],
        evidenceBoundary='Reference availability, shape, layering and mask traceability only. G0-2/3/4/5 and real server/IME/release acceptance remain separate.',
        inputs=[dict(path=path.relative_to(ROOT).as_posix(),sha256=sha(path)) for path in
                [spec_path,matching_path,manifest_path,recipe_path,REF/'resolution-confirmation.json',
                 REF/'availability.json',REF/'runtime-source-libraries.json',REF/'source-comparison.json']],
        generatorSha256=sha(Path(__file__)),releaseAccepted=False,runtimeReady=False)
    save(output/'reference-map.json',result)
    for im in images.values():
        im.close()
    return result


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--inspection', type=Path, required=True)
    parser.add_argument('--art', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    result = build(args.inspection,args.art,args.output)
    print(json.dumps(dict(result='pass',activeCaptures=len(result['views']),
        localComparisons=len(result['observations']),candidateAssets=len(result['assets']),
        ignoredScreenshotUsed=False,runtimeReady=False),ensure_ascii=False))
