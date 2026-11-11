"""Reproduce embedded minimap footer sprites from pinned native UI sources."""
import argparse, importlib.util, io, json, struct
from pathlib import Path
from PIL import Image
ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('composition', Path(__file__).with_name('Compose-OfficialUi.py'))
composition = importlib.util.module_from_spec(spec)
spec.loader.exec_module(composition)
decoder = composition.selected.decoder

def prepare(source, check):
    config = json.loads(Path(__file__).with_name('minimap-button-source.json').read_text(encoding='utf-8'))
    mix = decoder.read_pinned(source / 'UI.MIX', config['mixSha256'])
    miz = decoder.read_pinned(source / 'UI.MIZ', config['mizSha256'])
    width, height = config['sourceSize']
    sheet = Image.new('RGBA', (width, height * len(config['frames'])))
    for row, frame in enumerate(config['frames']):
        offset = struct.unpack_from('<I', mix, 24 + frame['sourceIndex'] * 4)[0]
        image, _ = decoder.decode_image(miz, offset)
        encoded = io.BytesIO()
        image.save(encoded, format='PNG')
        if image.size != (width, height) or decoder.sha256(encoded.getvalue()) != frame['pngSha256']:
            raise ValueError('Source sprite differs from the reviewed image')
        sheet.paste(composition.mask_edges(image, []), (0, row * height))
    encoded = io.BytesIO()
    sheet.save(encoded, format='PNG', compress_level=9)
    target = ROOT / config['output']
    if check:
        if not target.is_file() or target.read_bytes() != encoded.getvalue():
            raise ValueError('Embedded minimap art is missing or stale')
    else:
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(encoded.getvalue())
    print(f'{config["output"]}: {len(config["frames"])} pinned native frames, sha256={decoder.sha256(encoded.getvalue())}')

if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', required=True, type=Path)
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    prepare(args.source, args.check)
