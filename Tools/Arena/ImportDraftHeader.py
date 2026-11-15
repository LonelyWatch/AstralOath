"""Import the small source connection quality indicator used by the draft header."""
from __future__ import annotations

import argparse
import json
from pathlib import Path

from ImportArenaArt import MODULES, REPO, SOURCE_FOLDER, digest, draft_png, metadata, read_source, write


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=Path("D:/sevenepic"))
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    root = (args.source / SOURCE_FOLDER).resolve(strict=True)
    if REPO.resolve().is_relative_to(root):
        raise ValueError("The project must stay outside the source archive.")
    folder = MODULES / "Arena/Res/Textures/Draft/Header"
    if not args.check:
        folder.mkdir(parents=True, exist_ok=True)
    metadata(folder, args.check, "folderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n")
    files = ("cm_ping_bg", "cm_ping_1", "cm_ping_2", "cm_ping_3")
    entries = [draft_png(root, args.check, f"img/{name}.png", f"Header/{name}.png") for name in files]
    layout = "wnd/pvplive_ready.csb"
    manifest = {"schemaVersion": 1, "assets": entries, "sourceLayout": layout,
                "sourceLayoutSha256": digest(read_source(root, layout))}
    path = folder / "source-manifest.json"
    write(path, (json.dumps(manifest, ensure_ascii=False, indent=2) + "\n").encode(), args.check)
    metadata(path, args.check, "TextScriptImporter:\n  externalObjects: {}\n")
    print(f"{'Verified' if args.check else 'Imported'} {len(entries)} native draft header indicators.")


if __name__ == "__main__":
    main()
