"""Import the original Epic Seven hero bag art and log its source hashes."""
import argparse
import io
import json
import re
from pathlib import Path

from PIL import Image

from ImportArenaArt import REPO, SOURCE_FOLDER, digest, metadata, read_source, texture_importer, write


DEST = REPO / "Assets/Game/Modules/Home/Res/Textures/Inventory/Bag"
SPRITES = (
    "_tap_select_blue", "_tap_select_blue2", "_line_equip_wearing", "_bar_2",
    "_btn_small_black", "_box_equip_none", "_sorting2", "cm_icon_all",
    "cm_item_slot", "cm_item_slot_arti", "cm_item_slot_private", "cm_icon_parprivate",
    "icon_menu_private", "icon_menu_stuff", "icon_menu_reward_box", "icon_menu_equipstorage",
    "cm_icon_arti_public", "hero_lv_max", "_box_equip_0", "arti_gauge_bar",
)
LAYOUTS = ("inventory_renewal", "inventory_wearing", "sorting_renewal",
           "artifact_bar", "artifact_detail_sub", "inventory_artifact_popup")
# Cap insets from the original ImageView / Button widgets, in Unity left/bottom/right/top order.
BORDERS = {
    "_tap_select_blue": (9, 10, 29, 8),
    "_tap_select_blue2": (8, 10, 8, 8),
    "_line_equip_wearing": (60, 31, 34, 17),
    "_bar_2": (32, 4, 32, 5),
    "_btn_small_black": (14, 16, 14, 12),
    "_box_equip_none": (40, 23, 40, 21),
    "_box_equip_0": (100, 100, 100, 100),
    "cm_item_slot": (30, 30, 30, 30),
}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=Path("D:/sevenepic"))
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    root = (args.source / SOURCE_FOLDER).resolve(strict=True)
    assets = []
    for stem in SPRITES:
        relative = f"img/{stem}.png"
        data = read_source(root, relative)
        path = DEST / f"{stem}.png"
        with Image.open(io.BytesIO(data)) as image:
            if image.format != "PNG" or not image.convert("RGBA").getchannel("A").getbbox():
                raise ValueError(f"Invalid source sprite: {relative}")
            width, height = image.size
        importer = texture_importer(path, sprite=True)
        importer = re.sub(r"(?m)^(\s+textureCompression:) \d+$", r"\1 0", importer)
        importer = re.sub(r"(?m)^  userData:.*$", "  userData: AstralOath.ImportOverride", importer)
        if stem in BORDERS:
            left, bottom, right, top = BORDERS[stem]
            importer = re.sub(r"(?m)^  spriteBorder: .*",
                              f"  spriteBorder: {{x: {left}, y: {bottom}, z: {right}, w: {top}}}", importer)
        write(path, data, args.check)
        metadata(path, args.check, importer)
        if args.check:
            actual = path.with_suffix(".png.meta").read_text(encoding="utf-8-sig")
            if set(re.findall(r"(?m)^\s+textureCompression: (\d+)$", actual)) != {"0"}:
                raise ValueError(f"Unity compression would alter interface artwork: {path}")
        checksum = digest(data)
        assets.append({"asset": path.relative_to(REPO).as_posix(), "source": relative,
                       "sourceSha256": checksum, "outputSha256": checksum, "width": width, "height": height})
    manifest = {
        "referenceLayouts": [
            {"source": relative, "sha256": digest(read_source(root, relative))}
            for relative in (f"wnd/{stem}.csb" for stem in LAYOUTS)
        ],
        "assets": assets,
    }
    write(DEST / "source-manifest.json",
          (json.dumps(manifest, ensure_ascii=False, indent=2) + "\n").encode(), args.check)
    metadata(DEST / "source-manifest.json", args.check,
             "TextScriptImporter:\n  externalObjects: {}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n")
    print(f"{'Verified' if args.check else 'Imported'} {len(assets)} original hero bag sprites.")


if __name__ == "__main__":
    main()
