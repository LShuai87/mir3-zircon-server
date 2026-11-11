#!/usr/bin/env python3
"""Compose selected real UI art with explicit masks and instance-sized grids.

Output is staged art for integration review, not an accepted runtime package.
No global black colour-key, source-directory mutation or inferred button frames.
"""
import argparse
from collections import deque
import importlib.util
import json
from io import BytesIO
from pathlib import Path
import shutil
import tempfile
from PIL import Image, ImageDraw, ImageFilter

_spec = importlib.util.spec_from_file_location('selected_ui', Path(__file__).with_name('Select-OfficialUi.py'))
selected = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(selected)
sha = selected.decoder.sha256
read_json = selected.read_json
ordinary = selected.ordinary


def require(value, reason):
    if not value:
        raise ValueError(reason)


def rectangle(value, size, label):
    require(isinstance(value, list) and len(value) == 4 and all(type(x) is int for x in value), label + ': invalid rectangle')
    x, y, w, h = value
    require(x >= 0 and y >= 0 and w > 0 and h > 0 and x+w <= size[0] and y+h <= size[1], label + ': rectangle outside source')
    return x, y, x+w, y+h


def mask_edges(image, clear_rects):
    """Only exact-black pixels connected to the outer edge become transparent.

    Four-neighbour traversal preserves enclosed dark wells and black outlines.
    Explicit holes (e.g. minimap viewport) are separately bounded rectangles.
    """
    require(isinstance(clear_rects, list), 'clearRects must be a list')
    image = image.convert('RGB')
    width, height = image.size
    rgb = image.tobytes()
    alpha = bytearray([255]) * (width*height)
    queue = deque()
    def visit(index):
        if alpha[index] == 255 and rgb[index*3:index*3+3] == b'\0\0\0':
            alpha[index] = 0
            queue.append(index)
    for x in range(width):
        visit(x)
        visit((height-1)*width+x)
    for y in range(height):
        visit(y*width)
        visit(y*width+width-1)
    while queue:
        index = queue.popleft()
        x, y = index % width, index // width
        if x: visit(index-1)
        if x+1 < width: visit(index+1)
        if y: visit(index-width)
        if y+1 < height: visit(index+width)
    result = image.convert('RGBA')
    result.putalpha(Image.frombytes('L', image.size, bytes(alpha)))
    for area in clear_rects:
        bounds = rectangle(area, image.size, 'clearRects')
        # Black-only viewport holes prevent silently deleting visible artwork.
        patch = image.crop(bounds)
        require(patch.getbbox() is None, 'Explicit transparent hole contains non-black artwork')
        result.paste((0, 0, 0, 0), bounds)
    return result


def resized_tile(image, bounds, size):
    # Alpha uses nearest-neighbour so binary transparency is never softened.
    part = image.crop(bounds)
    rgb = part.convert('RGB').resize(size, Image.Resampling.LANCZOS)
    rgb.putalpha(part.getchannel('A').resize(size, Image.Resampling.NEAREST))
    return rgb


def match_repair_edges(original, tiled):
    """Retain panel illumination and sampled fine texture, joining the untouched RGB edge."""
    require(original.size == tiled.size and min(original.size) >= 2, 'Repair area must be at least 2x2')
    width, height = original.size
    source, texture = original.load(), tiled.load()
    # Low frequencies come from this exact panel area; high frequencies come only from
    # the declared clean patch. A narrow RGB blend keeps the original boundary exact.
    illumination = original.filter(ImageFilter.GaussianBlur(18)).load()
    texture_base = tiled.filter(ImageFilter.GaussianBlur(8)).load()
    result = tiled.copy(); pixels = result.load()
    for y in range(height):
        for x in range(width):
            weight = min(1., min(x,y,width-1-x,height-1-y) / 5.)
            repaired = [max(0,min(255,illumination[x,y][c] + texture[x,y][c] - texture_base[x,y][c])) for c in range(3)]
            pixels[x,y] = tuple(round(source[x,y][c]*(1-weight)+repaired[c]*weight) for c in range(3)) + (texture[x,y][3],)
    return result


def transform(image, spec):
    require(isinstance(spec, dict), 'Invalid transform')
    kind = spec.get('type')
    if kind == 'identity':
        require(set(spec) == {'type'}, 'Identity transform has unknown fields')
        return image.copy(), None
    if kind == 'crop':
        require(set(spec) == {'type', 'sourceArea'}, 'Unknown crop transform fields')
        return image.crop(rectangle(spec['sourceArea'], image.size, 'crop source')), None
    if kind == 'canvas':
        require(set(spec) == {'type', 'size', 'origin'}, 'Unknown canvas transform fields')
        size, origin = spec['size'], spec['origin']
        require(isinstance(size, list) and len(size) == 2 and all(type(v) is int and 1 <= v <= 4096 for v in size), 'Invalid canvas size')
        require(isinstance(origin, list) and len(origin) == 2 and all(type(v) is int and v >= 0 for v in origin), 'Invalid canvas origin')
        require(origin[0] + image.width <= size[0] and origin[1] + image.height <= size[1], 'Canvas clips source artwork')
        output = Image.new('RGBA', tuple(size))
        output.paste(image, tuple(origin))
        return output, None
    if kind == 'crop-panel-resize':
        require(set(spec) == {'type', 'sourceArea', 'size', 'sliceInsets', 'sampling'}, 'Unknown crop-panel-resize fields')
        part = image.crop(rectangle(spec['sourceArea'], image.size, 'panel crop'))
        return transform(part, dict(type='panel-resize', size=spec['size'], sliceInsets=spec['sliceInsets'], sampling=spec['sampling']))
    if kind == 'panel-resize':
        require(set(spec) == {'type', 'size', 'sliceInsets', 'sampling'}, 'Unknown panel-resize fields')
        size, insets = spec['size'], spec['sliceInsets']
        require(isinstance(size, list) and len(size) == 2 and all(type(v) is int and 1 <= v <= 4096 for v in size), 'Invalid panel size')
        require(isinstance(insets, list) and len(insets) == 4 and all(type(v) is int and v > 0 for v in insets), 'Invalid panel insets')
        require(spec['sampling'] == 'nearest', 'Panel resize requires nearest sampling')
        left, top, right, bottom = insets
        require(left + right < min(image.width, size[0]) and top + bottom < min(image.height, size[1]), 'Panel has no interior')
        sx, sy = [0, left, image.width-right, image.width], [0, top, image.height-bottom, image.height]
        tx, ty = [0, left, size[0]-right, size[0]], [0, top, size[1]-bottom, size[1]]
        output = Image.new('RGBA', tuple(size))
        # Nine disjoint regions: native corners remain exact; strips and centre
        # use explicit nearest sampling so binary alpha is never interpolated.
        for row in range(3):
            for col in range(3):
                part = image.crop((sx[col], sy[row], sx[col+1], sy[row+1]))
                extent = (tx[col+1]-tx[col], ty[row+1]-ty[row])
                if part.size != extent:
                    part = part.resize(extent, Image.Resampling.NEAREST)
                output.paste(part, (tx[col], ty[row]))
        return output, None
    if kind == 'texture-cover':
        require(set(spec) == {'type', 'sourcePatch', 'targets'}, 'Unknown texture-cover fields')
        patch_bounds = rectangle(spec['sourcePatch'], image.size, 'sourcePatch')
        patch = image.crop(patch_bounds)
        require(patch.getchannel('A').getextrema() == (255, 255), 'Texture patch must be opaque')
        targets = spec['targets']
        require(isinstance(targets, list) and 1 <= len(targets) <= 64, 'Invalid texture-cover targets')
        bounds = [rectangle(area, image.size, 'texture target') for area in targets]
        for i, area in enumerate(bounds):
            require(image.crop(area).getchannel('A').getextrema() == (255, 255), 'Texture target must be opaque')
            for previous in bounds[:i]:
                require(area[0] >= previous[2] or area[2] <= previous[0] or area[1] >= previous[3] or area[3] <= previous[1], 'Texture targets overlap')
        output = image.copy()
        # Sample the original image once; a patch may be inside a target.
        # Native pixels tile from each explicit target origin, with cropped ends.
        for x, y, right, bottom in bounds:
            for ty in range(y, bottom, patch.height):
                for tx in range(x, right, patch.width):
                    tile = patch.crop((0, 0, min(patch.width, right-tx), min(patch.height, bottom-ty)))
                    output.paste(tile, (tx, ty))
        return output, None
    if kind == 'panel-repair':
        require(set(spec) == {'type', 'sourcePatch', 'targets', 'stamps', 'crop', 'repairBlend'}, 'Unknown panel-repair fields')
        # Every repair samples the immutable input, never an earlier modified area.
        output, _ = transform(image, dict(type='texture-cover', sourcePatch=spec['sourcePatch'], targets=spec['targets']))
        require(spec['repairBlend'] == 'edge-matched-rgb', 'Unknown repair blend policy')
        for area in spec['targets']:
            bounds = rectangle(area, image.size, 'repair target')
            output.paste(match_repair_edges(image.crop(bounds), output.crop(bounds)), bounds[:2])
        stamps = spec['stamps']
        require(isinstance(stamps, list) and 1 <= len(stamps) <= 64, 'Invalid panel stamp list')
        occupied = []
        for stamp in stamps:
            require(isinstance(stamp, dict) and set(stamp) == {'source', 'target'}, 'Unknown panel stamp fields')
            source = rectangle(stamp['source'], image.size, 'panel stamp source')
            target = rectangle(stamp['target'], image.size, 'panel stamp target')
            require(image.crop(source).getchannel('A').getextrema() == (255, 255), 'Panel stamp source must be opaque')
            require(image.crop(target).getchannel('A').getextrema() == (255, 255), 'Panel stamp target must be opaque')
            for prior in occupied:
                require(target[0] >= prior[2] or target[2] <= prior[0] or target[1] >= prior[3] or target[3] <= prior[1], 'Panel stamps overlap')
            occupied.append(target)
            output.paste(resized_tile(image, source, (target[2]-target[0], target[3]-target[1])), target[:2])
        return output.crop(rectangle(spec['crop'], image.size, 'panel crop')), None
    if kind == 'quickbar-native':
        require(spec == {'type': 'quickbar-native'} and image.size == (518, 44), 'Native quickbar must preserve UI[430]')
        return image.copy(), dict(columns=10, rows=1, cellSize=[41, 40], step=[41, 40], origin=[60, 3],
                                  cells=[[60+i*41, 3, 41, 40] for i in range(10)], size=[410, 40],
                                  sourceArea=[60, 3, 410, 40], sourceCell=[60, 3, 41, 40])
    require(kind in ('inventory-grid', 'quickbar-grid'), 'Unsupported transform')
    require(set(spec) == ({'type', 'sourceArea', 'sourceCell', 'columns', 'rows', 'cellSize', 'step'} if kind == 'inventory-grid' else {'type', 'sourceArea', 'sourceCell', 'columns', 'cellSize', 'step'}), 'Unknown grid transform fields')
    area = rectangle(spec.get('sourceArea'), image.size, 'sourceArea')
    cell = rectangle(spec.get('sourceCell'), image.size, 'sourceCell')
    require(area[0] <= cell[0] and area[1] <= cell[1] and cell[2] <= area[2] and cell[3] <= area[3], 'Cell outside source grid')
    columns, rows = spec.get('columns'), spec.get('rows', 1)
    require(type(columns) is int and type(rows) is int and 1 <= columns <= 10 and 1 <= rows <= 10, 'Invalid grid count')
    require(type(spec.get('cellSize')) is int and spec['cellSize'] == 36 and type(spec.get('step')) is int and spec['step'] == 35, 'Project grid geometry must remain 36px with 35px step')
    if kind == 'inventory-grid':
        require((columns, rows) == (7, 7), 'Inventory must keep 49 cells')
    else:
        require((columns, rows) == (10, 1), 'Quickbar must keep ten links')
    x, y, right, bottom = area
    grid_w, grid_h = columns*35+1, rows*35+1
    if kind == 'quickbar-grid':
        grid_h = bottom-y
    out_w = image.width - (right-x) + grid_w
    out_h = image.height - (bottom-y) + grid_h
    output = Image.new('RGBA', (out_w, out_h))
    # Header, footer and horizontal end caps are copied at native size.
    output.paste(image.crop((0, 0, image.width, y)), (0, 0))
    if kind == 'inventory-grid':
        output.paste(image.crop((0, bottom, image.width, image.height)), (0, y+grid_h))
        require(out_w == image.width, 'Inventory header width must remain native')
        output.paste(resized_tile(image, (0, y, x, bottom), (x, grid_h)), (0, y))
        output.paste(resized_tile(image, (right, y, image.width, bottom), (image.width-right, grid_h)), (x+grid_w, y))
        tile = resized_tile(image, cell, (36, 36))
        for row in range(rows):
            for col in range(columns):
                output.paste(tile, (x+col*35, y+row*35))
    else:
        require(y == cell[1] and bottom == cell[3], 'Quickbar tile height must match its row')
        # Crop complete top/bottom strips with corresponding left and right caps.
        output = Image.new('RGBA', (out_w, image.height))
        output.paste(image.crop((0, 0, x, image.height)), (0, 0))
        output.paste(image.crop((right, 0, image.width, image.height)), (x+grid_w, 0))
        full_tile = resized_tile(image, (cell[0], 0, cell[2], image.height), (36, image.height))
        for col in range(columns):
            output.paste(full_tile, (x+col*35, 0))
    cells = [[x+col*35, y+row*35, 36, 36] for row in range(rows) for col in range(columns)]
    return output, dict(columns=columns, rows=rows, cellSize=[36, 36], step=[35, 35], origin=[x, y], cells=cells,
                        size=[grid_w, rows*35+1], sourceArea=spec['sourceArea'], sourceCell=spec['sourceCell'])


def border_frame(image, spec):
    """Copy a narrow native trim without stretching corners or painting its centre."""
    require(isinstance(spec, dict) and set(spec) == {'type', 'role', 'sourceRole', 'size', 'sliceInsets', 'horizontalSample'}, 'Invalid derived frame fields')
    require(spec['type'] == 'border-frame', 'Unsupported derived frame type')
    size, insets, sample = spec['size'], spec['sliceInsets'], spec['horizontalSample']
    require(isinstance(size, list) and len(size) == 2 and all(type(v) is int and 1 <= v <= 4096 for v in size), 'Invalid derived frame size')
    require(isinstance(insets, list) and len(insets) == 4 and all(type(v) is int and v > 0 for v in insets), 'Invalid derived frame insets')
    left, top, right, bottom = insets
    require(left + right < image.width and top + bottom < image.height and left + right < size[0] and top + bottom < size[1], 'Derived frame has no interior')
    require(isinstance(sample, list) and len(sample) == 2 and all(type(v) is int for v in sample) and left <= sample[0] and sample[1] > 0 and sample[0] + sample[1] <= image.width - right, 'Invalid derived frame horizontal sample')
    # Four corners are copied verbatim. Edge pixels use nearest-neighbour;
    # colour-keying and alpha interpolation are never applied to derived art.
    w, h = size
    output = Image.new('RGBA', (w, h))
    def put(box, at, extent=None):
        part = image.crop(box)
        if extent is not None:
            part = part.resize(extent, Image.Resampling.NEAREST)
        output.paste(part, at)
    sw, sh = image.size
    put((0, 0, left, top), (0, 0))
    put((sw-right, 0, sw, top), (w-right, 0))
    put((0, sh-bottom, left, sh), (0, h-bottom))
    put((sw-right, sh-bottom, sw, sh), (w-right, h-bottom))
    start, length = sample
    put((start, 0, start+length, top), (left, 0), (w-left-right, top))
    put((start, sh-bottom, start+length, sh), (left, h-bottom), (w-left-right, bottom))
    put((0, top, left, sh-bottom), (0, top), (left, h-top-bottom))
    put((sw-right, top, sw, sh-bottom), (w-right, top), (right, h-top-bottom))
    return output


def derive_frame(image, spec):
    require(isinstance(spec, dict), 'Invalid derived frame')
    if spec.get('type') == 'border-frame':
        return border_frame(image, spec)
    if spec.get('type') == 'segmented-frame':
        require(set(spec) == {'type', 'role', 'sourceRole', 'size', 'segments', 'sampling'}, 'Invalid segmented frame fields')
        size, count = spec['size'], spec['segments']
        require(isinstance(size, list) and len(size) == 2 and all(type(v) is int and 1 <= v <= 4096 for v in size), 'Invalid segmented frame size')
        require(type(count) is int and 1 <= count <= 64 and (image.width - 1) % count == 0 and (image.width - 1) // count >= 3 and size[0] >= count * 3 + 1, 'Invalid segment geometry')
        require(spec['sampling'] == 'nearest', 'Segmented frames require explicit nearest sampling')
        # Preserve every divider at a shared rounded target boundary. Resizing
        # the entire strip at once can discard a one-pixel divider.
        output = Image.new('RGBA', tuple(size))
        pitch = (image.width - 1) // count
        for segment in range(count):
            left = (segment * (size[0] - 1) + count // 2) // count
            right = ((segment + 1) * (size[0] - 1) + count // 2) // count
            part = image.crop((segment * pitch, 0, (segment + 1) * pitch, image.height))
            output.paste(part.resize((right - left, size[1]), Image.Resampling.NEAREST), (left, 0))
        edge = image.crop((image.width - 1, 0, image.width, image.height))
        output.paste(edge.resize((1, size[1]), Image.Resampling.NEAREST), (size[0] - 1, 0))
        return output
    require(spec.get('type') == 'crop-frame', 'Unsupported derived frame type')
    require(set(spec) == {'type', 'role', 'sourceRole', 'sourceArea'}, 'Invalid cropped frame fields')
    bounds = rectangle(spec['sourceArea'], image.size, 'cropped frame sourceArea')
    require(bounds[2]-bounds[0] <= 4096 and bounds[3]-bounds[1] <= 4096, 'Cropped frame is too large')
    return image.crop(bounds)


def tint(image, state, numerator, denominator):
    require(state in ('hover', 'pressed', 'disabled') and type(numerator) is int and type(denominator) is int and 0 < denominator <= 1000 and 0 < numerator <= 2000, 'Invalid button tint')
    alpha = image.getchannel('A')
    rgb = image.convert('RGB')
    if state == 'disabled':
        rgb = rgb.convert('L').convert('RGB')
    table = [min(255, (x*numerator+denominator//2)//denominator) for x in range(256)]
    rgb = rgb.point(table*3)
    rgb.putalpha(alpha)
    return rgb


def checker(size):
    image = Image.new('RGBA', size, (42, 66, 83, 255))
    draw = ImageDraw.Draw(image)
    for y in range(0, size[1], 16):
        for x in range(0, size[0], 16):
            if (x//16+y//16)%2:
                draw.rectangle((x, y, x+15, y+15), fill=(96, 106, 87, 255))
    return image


def order_frames(records, order):
    """Keep existing semantic indices stable when adding assets to a shared package."""
    if order is None:
        return records
    require(isinstance(order, list) and all(isinstance(role, str) for role in order), 'Invalid frame order')
    require(len(order) == len(records) and len(set(order)) == len(order) and
            set(order) == {record['role'] for record in records}, 'Frame order must cover every output exactly once')
    by_role = {record['role']: record for record in records}
    return [dict(by_role[role], outputIndex=index) for index, role in enumerate(order)]


def compose(decoded, output, recipe_path):
    decoded, output, recipe_path = ordinary(decoded), ordinary(output), ordinary(recipe_path)
    require(decoded.is_dir() and not output.exists(), 'Decoded directory must exist and output must be new')
    require(decoded not in output.parents and output not in decoded.parents and recipe_path != output and output not in recipe_path.parents, 'Input/output directories must be disjoint')
    manifest_path, selection_path = ordinary(decoded/'manifest.json'), ordinary(decoded/'selection.json')
    snapshots = {p: p.read_bytes() for p in (manifest_path, selection_path, recipe_path)}
    manifest, selection, recipe = read_json(manifest_path), read_json(selection_path), read_json(recipe_path)
    require(manifest.get('schemaVersion') == 1 and manifest.get('kind') == 'official-ui-decoded-reference' and manifest.get('alphaVerified') is False, 'Expected reference decode')
    require(recipe.get('schemaVersion') == 1 and recipe.get('kind') == 'official-ui-composition-recipe' and recipe.get('productionBindingAllowed') is False, 'Invalid composition recipe')
    require(recipe.get('selectionSha256') == sha(snapshots[selection_path]), 'Recipe selection hash mismatch')
    require(selection.get('schemaVersion') == 1 and isinstance(selection.get('assets'), list), 'Invalid selection')
    sources = {a['role']: a for a in selection['assets']}
    require(len(sources) == len(selection['assets']), 'Duplicate selected role')
    libraries = {lib['name']: lib for lib in manifest['libraries']}
    assets = recipe.get('assets')
    require(isinstance(assets, list) and 0 < len(assets) <= 1000, 'Invalid recipe asset list')
    require(all(isinstance(a, dict) for a in assets), 'Invalid asset record')
    require(len({a.get('role') for a in assets}) == len(assets) and {a.get('sourceRole') for a in assets} == set(sources), 'Recipe must cover each selected source with unique output roles')
    # Page layers may share a non-button source; stateful button sources stay unique.
    for source_role in sources:
        uses = [a for a in assets if a.get('sourceRole') == source_role]
        require(len(uses) == 1 or all(a.get('button') is False for a in uses), 'Duplicate source mapping for button: ' + source_role)
    require(recipe.get('buttonTint') == {'hover': [115,100], 'pressed': [75,100], 'disabled': [60,100]}, 'Button tint policy must be explicit')
    output.parent.mkdir(parents=True, exist_ok=True)
    staging = Path(tempfile.mkdtemp(prefix='.'+output.name+'.staging-', dir=output.parent))
    inputs, images, records, buttons, grids = {}, {}, [], {}, {}
    total_pixels = 0
    try:
        (staging/'Assets').mkdir()
        (staging/'Preview').mkdir()
        def save(role, image, source, source_role, state=None):
            require(selected.ID.fullmatch(role) and role not in images, 'Invalid or duplicate output role')
            require(set(image.getchannel('A').tobytes()) <= {0,255}, 'Fractional alpha in output')
            nonlocal total_pixels
            total_pixels += image.width * image.height
            require(total_pixels <= 67108864, 'Composed pixel budget exceeded')
            relative = 'Assets/'+role+'.png'
            image.save(staging/relative, format='PNG', compress_level=9)
            records.append(dict(role=role, outputIndex=len(records), png=relative, pngSha256=sha((staging/relative).read_bytes()),
                rgbaSha256=sha(image.tobytes()), logicalSize=list(image.size), textureSize=[(x+3)//4*4 for x in image.size],
                sourceLibrary=source['sourceLibrary'], sourceIndex=source['sourceIndex'], sourcePngSha256=source['pngSha256'],
                sourceOffset=source['sourceOffset'], outputOffset=[0,0], pivot=[0,0], offsetMode='baked', alphaMode='BinaryMask',
                blendMode='Normal', sourceRole=source_role, state=state))
            images[role] = image
        for asset in assets:
            role = asset.get('role')
            require(isinstance(role, str) and selected.ID.fullmatch(role) and asset.get('alphaPolicy') == 'edge-connected-exact-black' and type(asset.get('button')) is bool, 'Invalid semantic asset or alpha policy')
            source = sources[asset['sourceRole']]
            lib = libraries.get(source['sourceLibrary'])
            require(lib is not None and type(source['sourceIndex']) is int and 0 <= source['sourceIndex'] < len(lib['items']), 'Missing source image record')
            item = lib['items'][source['sourceIndex']]
            expected_path = source['sourceLibrary']+'/'+str(source['sourceIndex'])+'.png'
            require(item and item['png'] == expected_path and item['pngSha256'] == source['pngSha256'] and [item['width'], item['height']] == source['logicalSize'] and [item['offsetX'], item['offsetY']] == source['sourceOffset'], 'Source metadata mismatch')
            png = ordinary(decoded/expected_path)
            raw = png.read_bytes()
            require(sha(raw) == source['pngSha256'], 'Source PNG hash mismatch')
            inputs[png] = raw
            with Image.open(BytesIO(raw)) as original:
                require(original.mode in ('RGB', 'RGBA') and original.convert('RGBA').getchannel('A').getextrema() == (255, 255) and list(original.size) == source['logicalSize'] and original.width*original.height <= 16777216, 'Invalid source dimensions/mode')
                image = mask_edges(original, asset.get('clearRects', []))
            image, grid = transform(image, asset.get('transform'))
            if grid: grids[role] = grid
            save(role, image, source, asset['sourceRole'])
            preview = checker(image.size)
            preview.alpha_composite(image)
            preview.convert('RGB').save(staging/'Preview'/(role+'.png'), compress_level=9)
            if asset['button']:
                states = {'normal': role}
                for state, ratio in recipe['buttonTint'].items():
                    state_role = role+'-'+state
                    native = asset.get('nativeStates', {})
                    if native:
                        require(set(native) == {'hover', 'pressed', 'disabled'}, 'Incomplete native button states')
                        state_source = sources[native[state]]
                        state_png = ordinary(decoded / (state_source['sourceLibrary']+'/'+str(state_source['sourceIndex'])+'.png'))
                        raw_state = state_png.read_bytes()
                        require(sha(raw_state) == state_source['pngSha256'], 'Native state source hash differs')
                        inputs[state_png] = raw_state
                        with Image.open(BytesIO(raw_state)) as original_state:
                            state_image, _ = transform(mask_edges(original_state, []), asset['transform'])
                        require(state_image.size == image.size, 'Native state size differs')
                        save(state_role, state_image, state_source, asset['sourceRole'], state)
                    else:
                        save(state_role, tint(image, state, *ratio), source, asset['sourceRole'], state)
                    states[state] = state_role
                buttons[role] = states
        derived = recipe.get('derivedFrames', [])
        require(isinstance(derived, list) and len(derived) <= 32, 'Invalid derived frame list')
        primary = {a['role']: a for a in records if a['state'] is None}
        for frame in derived:
            require(isinstance(frame, dict) and isinstance(frame.get('role'), str) and selected.ID.fullmatch(frame['role']) and frame['role'] not in images, 'Invalid or duplicate derived role')
            require(isinstance(frame.get('sourceRole'), str) and frame['sourceRole'] in primary and frame['sourceRole'] not in buttons, 'Derived frame must use an original non-button asset')
            source_record = primary[frame['sourceRole']]
            image = derive_frame(images[frame['sourceRole']], frame)
            save(frame['role'], image, sources[source_record['sourceRole']], source_record['sourceRole'])
            preview = checker(image.size)
            preview.alpha_composite(image)
            preview.convert('RGB').save(staging/'Preview'/(frame['role']+'.png'), compress_level=9)
        for composition in recipe.get('compositions', []):
            require(composition.get('id') == 'hud-orb', 'Unknown composition')
            layers = composition['layers']
            require([l['role'] for l in layers] == ['hud-orb-shell','hud-hp-fill','hud-mp-fill'], 'Invalid orb layers')
            offsets = {a['role']: a['sourceOffset'] for a in selection['assets']}
            shell_offset = offsets['hud-orb-shell']
            for layer in layers:
                require(layer['offset'] == [offsets[layer['role']][i]-shell_offset[i] for i in (0,1)], 'Orb placement must share original anchor')
            shell = images['hud-orb-shell']
            preview = Image.new('RGB', (shell.width*3, shell.height))
            for col, ratio in enumerate((0, 0.5, 1)):
                frame = checker(shell.size)
                for layer in layers:
                    part = images[layer['role']]
                    at = layer['offset'][:]
                    if layer.get('fill'):
                        require(layer['fill'] == 'vertical-bottom', 'Unknown fill direction')
                        visible = int(part.height*ratio+0.5)
                        if not visible: continue
                        top = part.height-visible
                        part = part.crop((0,top,part.width,part.height))
                        at[1] += top
                    frame.alpha_composite(part, tuple(at))
                preview.paste(frame.convert('RGB'), (col*shell.width, 0))
            preview.save(staging/'Preview/hud-orb-states.png', compress_level=9)
        records = order_frames(records, recipe.get('frameOrder'))
        for path, raw in {**snapshots, **inputs}.items():
            require(ordinary(path).read_bytes() == raw, 'Input changed during composition')
        result = dict(schemaVersion=1, kind='official-ui-composed-art', runtimeReady=False, productionBindingAllowed=False,
                      alphaVerified=False, sourceManifestSha256=sha(snapshots[manifest_path]), recipeSha256=sha(snapshots[recipe_path]),
                      assets=records, buttons=buttons, grids=grids, derivedFrames=derived, compositions=recipe.get('compositions', []))
        (staging/'composed-manifest.json').write_text(json.dumps(result, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
        require(not output.exists(), 'Output appeared during composition')
        staging.rename(output)
        return result
    finally:
        if staging.exists(): shutil.rmtree(staging)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--decoded', required=True, type=Path)
    parser.add_argument('--output', required=True, type=Path)
    parser.add_argument('--recipe', required=True, type=Path)
    args = parser.parse_args()
    result = compose(args.decoded, args.output, args.recipe)
    print(f"Composed {len(result['assets'])} real-art frames, {len(result['buttons'])} explicit button state sets; runtime acceptance pending.")


if __name__ == '__main__':
    main()
