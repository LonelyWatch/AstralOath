"""Import the original picked marker for the draft roster cards."""
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
    folder = MODULES / "Arena/Res/Textures/Draft/Roster"
    if not args.check:
        folder.mkdir(parents=True, exist_ok=True)
    metadata(folder, args.check, "folderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n")
    marker = draft_png(root, args.check, "img/cm_icon_check_b.png", "Roster/cm_icon_check_b.png")
    layout = "wnd/unit_bar.csb"
    manifest = {"schemaVersion": 1, "assets": [marker], "sourceLayout": layout,
                "sourceLayoutSha256": digest(read_source(root, layout))}
    path = folder / "source-manifest.json"
    write(path, (json.dumps(manifest, ensure_ascii=False, indent=2) + "\n").encode(), args.check)
    metadata(path, args.check, "TextScriptImporter:\n  externalObjects: {}\n")
    print("Verified native roster marker." if args.check else "Imported native roster marker.")


if __name__ == "__main__":
    main()
