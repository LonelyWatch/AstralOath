"""Import the original static chat sticker art and compare its IDs to the server catalogue."""

import argparse
import hashlib
import json
from pathlib import Path

from PIL import Image

from ImportArenaArt import REPO, metadata, texture_importer, write


CATALOGUE = REPO / "Server/Host/Arena/arena-chat-stickers.txt"
DESTINATION = REPO / "Assets/Game/Modules/Arena/Res/Textures/ChatStickers"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=Path("D:/sevenepic"))
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    source = (args.source / "原始资源/emoticon_chat").resolve(strict=True)
    files = {item.stem: item for item in source.glob("*.png") if item.is_file()}
    ids = CATALOGUE.read_text(encoding="utf-8").split()
    if sorted(files) != sorted(ids) or len(set(ids)) != len(ids):
        raise ValueError("The native chat art and service allowlist differ.")
    entries = []
    for key in ids:
        original = files[key].resolve(strict=True)
        if original.parent != source:
            raise ValueError(f"Art escaped source: {key}")
        with Image.open(original) as image:
            if image.format != "PNG" or not image.convert("RGBA").getchannel("A").getbbox():
                raise ValueError(f"Invalid sticker: {key}")
            width, height = image.size
        data = original.read_bytes()
        dest = DESTINATION / original.name
        write(dest, data, args.check)
        metadata(dest, args.check, texture_importer(dest, sprite=True))
        entries.append({"id": key, "source": f"emoticon_chat/{original.name}",
                        "sha256": hashlib.sha256(data).hexdigest(), "width": width, "height": height})
    manifest = DESTINATION / "source-manifest.json"
    payload = {"assetRevision": "pack-15-r4262", "stickers": entries}
    write(manifest, (json.dumps(payload, indent=2) + "\n").encode(), args.check)
    metadata(manifest, args.check, "TextScriptImporter:\n  externalObjects: {}\n")
    print(f"Verified {len(entries)} native chat stickers.")


if __name__ == "__main__":
    main()
