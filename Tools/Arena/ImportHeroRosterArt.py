"""Import the hero detail sprites used by the original equipment view."""
import argparse
import io
import json
from pathlib import Path

from PIL import Image

from ImportArenaArt import (REPO, SOURCE_FOLDER, UNCOMPRESSED_UI, digest, metadata,
                            read_source, texture_importer, write)


DEST = REPO / "Assets/Game/Modules/Home/Res/Textures/Inventory/Roster"
SPRITES = (
    "base_unit_blue", "_box_gray", "_btn_blue_r", "icon_menu_hero", "icon_menu_profile",
    "icon_menu_story", "cm_item_slot_0", "cm_item_slot_arti_0", "cm_icon_parweapon",
    "cm_icon_parhelm", "cm_icon_pararmor", "cm_icon_parneck", "cm_icon_parring",
    "cm_icon_parboots", "cm_icon_pararti", "icon_menu_equip_auto", "icon_menu_unequip_auto",
    "icon_menu_equip_undo", "icon_quick_transfer", "icon_menu_zoom", "icon_menu_arrow2",
    "cm_icon_etcbp", "_plus", "cm_icon_profire", "cm_icon_proice", "cm_icon_prowind",
)
ZODIAC = ("archer", "bull", "crab", "fish", "goat", "lion", "maiden", "ram", "scales", "scorpion", "twins", "waterbearer")
LAYOUTS = ("hero_detail_base", "hero_detail_list", "unit_detail", "unit_bar", "unit_list_unfold")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=Path("D:/sevenepic"))
    parser.add_argument("--check", action="store_true")
    options = parser.parse_args()
    root = (options.source / SOURCE_FOLDER).resolve(strict=True)
    assets = []
    sprites = SPRITES + tuple(f"cm_icon_zodiac_{sign}" for sign in ZODIAC)
    UNCOMPRESSED_UI.update(sprites)
    for stem in sprites:
        relative = f"img/{stem}.png"
        data = read_source(root, relative)
        path = DEST / f"{stem}.png"
        with Image.open(io.BytesIO(data)) as image:
            if image.format != "PNG":
                raise ValueError("Expected source PNG: " + relative)
            width, height = image.size
        write(path, data, options.check)
        metadata(path, options.check, texture_importer(path, sprite=True))
        checksum = digest(data)
        assets.append({"source": relative, "sourceSha256": checksum, "outputSha256": checksum,
                       "asset": path.relative_to(REPO).as_posix(), "width": width, "height": height})
    references = []
    for stem in LAYOUTS:
        relative = f"wnd/{stem}.csb"
        references.append({"source": relative, "sha256": digest(read_source(root, relative))})
    manifest = {"referenceLayouts": references, "assets": assets}
    write(DEST / "source-manifest.json",
          (json.dumps(manifest, ensure_ascii=False, indent=2) + "\n").encode(), options.check)
    print(f"{'Verified' if options.check else 'Imported'} {len(assets)} original hero detail sprites.")


if __name__ == "__main__":
    main()
