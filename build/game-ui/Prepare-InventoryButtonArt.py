#!/usr/bin/env python3
"""Bake pinned official inventory UI frames as small embedded sprite sheets."""
import argparse
import importlib.util
import json
import struct
from io import BytesIO
from pathlib import Path
from PIL import Image

_spec = importlib.util.spec_from_file_location("composition", Path(__file__).with_name("Compose-OfficialUi.py"))
composition = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(composition)
decoder = composition.selected.decoder


def prepare(source, check, config_path):
    root = Path(__file__).resolve().parents[2]
    config = json.loads(config_path.read_text(encoding="utf-8"))
    mix = decoder.read_pinned(source / "UI.MIX", config["mixSha256"])
    miz = decoder.read_pinned(source / "UI.MIZ", config["mizSha256"])
    count = struct.unpack_from("<I", mix, 16)[0]
    if len(mix) != 24 + count * 4:
        raise ValueError("Invalid source index length")
    offsets = struct.unpack_from("<" + str(count) + "I", mix, 24)
    width, height = config["targetSize"]
    sheet = Image.new("RGBA", (width, height * len(config["frames"])))
    edge = config["slicePadding"]
    for row, entry in enumerate(config["frames"]):
        image, _ = decoder.decode_image(miz, offsets[entry["sourceIndex"]])
        encoded = BytesIO()
        image.save(encoded, format="PNG")
        if list(image.size) != config["sourceSize"] or decoder.sha256(encoded.getvalue()) != entry["pngSha256"]:
            raise ValueError("Source button differs from pinned image")
        image = composition.mask_edges(image, [])
        # Retain the bevel/corners; resize only the inner portions for the existing footer bounds.
        sx, sy = (0, edge, image.width - edge, image.width), (0, edge, image.height - edge, image.height)
        dx, dy = (0, edge, width - edge, width), (0, edge, height - edge, height)
        for y in range(3):
            for x in range(3):
                part = image.crop((sx[x], sy[y], sx[x+1], sy[y+1]))
                part = part.resize((dx[x+1]-dx[x], dy[y+1]-dy[y]), Image.Resampling.NEAREST)
                sheet.paste(part, (dx[x], row * height + dy[y]))
    encoded = BytesIO()
    sheet.save(encoded, format="PNG", compress_level=9)
    target = root / config["output"]
    if check:
        if not target.is_file() or target.read_bytes() != encoded.getvalue():
            raise ValueError("Embedded button art is missing or stale")
    else:
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(encoded.getvalue())
    print(str(target.relative_to(root)) + ": " + str(len(config["frames"])) + " source frames, " + str(width) + "x" + str(height) + ", sha256=" + decoder.sha256(encoded.getvalue()))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", required=True, type=Path)
    parser.add_argument("--check", action="store_true")
    parser.add_argument("--config", type=Path, default=Path(__file__).with_name("inventory-button-source.json"))
    args = parser.parse_args()
    prepare(args.source, args.check, args.config)
