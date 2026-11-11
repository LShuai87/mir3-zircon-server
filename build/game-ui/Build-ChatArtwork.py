"""Pack unmodified decoded official UI artwork for the native chat controls."""
import argparse
import hashlib
import json
from pathlib import Path
from PIL import Image

INDICES = [i for start in (210, 220, 200, 240, 230, 70, 74, 50) for i in range(start, start + 4)] + [115]

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("inspection", type=Path, help="Strict decoder output containing UI/*.png")
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[2]
    output = root / "Client/Resources/GameUi/chat-artwork.png"
    atlas = Image.new("RGBA", (134, 24 * len(INDICES)))
    sources = []
    for row, index in enumerate(INDICES):
        source = args.inspection / "UI" / f"{index}.png"
        image = Image.open(source).convert("RGBA")
        expected = (31, 24) if row < 20 else (18, 18) if row < 28 else (18, 12) if row < 32 else (134, 14)
        if image.size != expected:
            raise ValueError(f"UI[{index}]: expected {expected}, got {image.size}")
        atlas.paste(image, (0, row * 24))
        sources.append(dict(library="UI", index=index, sha256=hashlib.sha256(source.read_bytes()).hexdigest(),
                            rectangle=[0, row * 24, *image.size]))
    atlas.save(output)
    manifest = dict(description="Original four-state channel/scroll controls and UI[115] nine-slice border. Black interior is rendered by the client; no full chat background asset was found in the decoded inventory.",
                    atlasSha256=hashlib.sha256(output.read_bytes()).hexdigest(), sources=sources)
    output.with_suffix(".json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(output)

if __name__ == "__main__":
    main()
