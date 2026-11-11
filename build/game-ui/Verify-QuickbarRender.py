#!/usr/bin/env python3
"""Compare rendered shortcut end caps/last-slot trim with an independent BC1 decode."""
import argparse
import importlib.util
import json
import struct
from pathlib import Path
from PIL import Image

_spec = importlib.util.spec_from_file_location("catalog_primitives", Path(__file__).with_name("Prepare-UiCatalog.py"))
_primitives = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(_primitives)


def decode(frame):
    width, height = frame["width"], frame["height"]
    image = Image.new("RGBA", (width, height))
    blocks = (width + 3) // 4
    def rgb565(value):
        r, g, b = (value >> 11) & 31, (value >> 5) & 63, value & 31
        return ((r << 3) | (r >> 2), (g << 2) | (g >> 4), (b << 3) | (b >> 2), 255)
    for offset in range(0, len(frame["data"]), 8):
        a, b, bits = struct.unpack_from("<HHI", frame["data"], offset)
        colours = [rgb565(a), rgb565(b)]
        if a > b:
            colours += [tuple((2*colours[0][i]+colours[1][i])//3 for i in range(3))+(255,),
                        tuple((colours[0][i]+2*colours[1][i])//3 for i in range(3))+(255,)]
        else:
            colours += [tuple((colours[0][i]+colours[1][i])//2 for i in range(3))+(255,), (0,0,0,0)]
        block = offset // 8
        for i in range(16):
            x, y = block % blocks * 4 + i % 4, block // blocks * 4 + i // 4
            if x < width and y < height:
                image.putpixel((x,y), colours[(bits >> (i*2)) & 3])
    return image


def verify(package, evidence):
    _, frames = _primitives.read_zl(package.read_bytes())
    frame = frames[3]
    assert (frame["width"], frame["height"]) == (518,44), "Quickbar frame was rescaled"
    reference = decode(frame)
    regions = {"left-ornament": (0,0,60,44), "right-ornament": (470,0,518,44),
               "last-slot-bottom": (429,37,470,43), "last-slot-right": (467,3,470,43)}
    results = []
    paths = [evidence / "official-hud.png", evidence / "official-reentry-hud.png"]
    paths += sorted(evidence.glob("official-reset-*.png"))
    for path in paths:
        with Image.open(path) as source:
            screenshot = source.convert("RGB")
        origin = ((screenshot.width-518)//2, screenshot.height-182)
        for name, (left,top,right,bottom) in regions.items():
            count, maximum = 0, 0
            for y in range(top,bottom):
                for x in range(left,right):
                    expected = reference.getpixel((x,y))
                    if expected[3] != 255:
                        continue
                    actual = screenshot.getpixel((origin[0]+x,origin[1]+y))
                    difference = max(abs(actual[i]-expected[i]) for i in range(3))
                    assert difference <= 8, f"{path.name}: {name} clipped/covered at {x},{y}: {actual} vs {expected}"
                    count += 1
                    maximum = max(maximum,difference)
            assert count > 50, "Insufficient opaque trim pixels"
            results.append(dict(image=path.name, region=name, comparedPixels=count, maxRgbDifference=maximum))
    assert len(paths) == 8, "Expected initial/reentry plus six resolutions"
    return dict(verification="actual D3D screenshot vs independent BC1 frame decode", frameSize=[518,44],
                resolutions=6, screenshots=len(paths), results=results, productionAccepted=False)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--package", type=Path, required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    result = verify(args.package, args.evidence)
    args.output.write_text(json.dumps(result,indent=2)+"\n",encoding="utf-8")
    print(f"Quickbar render: {len(result['results'])} trim comparisons passed across {result['screenshots']} screenshots.")
