"""Import only the original UI art needed by the hero roster and bag."""
import argparse
import io
import json
from pathlib import Path

from PIL import Image

from ImportArenaArt import REPO, SOURCE_FOLDER, digest, metadata, read_source, texture_importer, write

DEST = REPO / "Assets/Game/Modules/Home/Res/Textures/Inventory"
SPRITES = (
    "base_inventory", "base_unit_hero", "_tap_select_blue", "_grow_inventory_top",
    "icon_menu_equip", "icon_menu_equipment", "icon_menu_pararti",
    "renew_icon_quick_hero", "renew_icon_quick_hero_p", "quick_esc", "quick_esc_p",
)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=Path("D:/sevenepic"))
    parser.add_argument("--check", action="store_true")
    opt = parser.parse_args()
    root = (opt.source / SOURCE_FOLDER).resolve(strict=True)
    assets = []
    for stem in SPRITES:
        relative = f"img/{stem}.png"
        data = read_source(root, relative)
        path = DEST / f"{stem}.png"
        with Image.open(io.BytesIO(data)) as im:
            if im.format != "PNG":
                raise ValueError("Expected source PNG: " + relative)
            width, height = im.size
        write(path, data, opt.check)
        metadata(path, opt.check, texture_importer(path, sprite=True))
        checksum = digest(data)
        assets.append({"asset": path.relative_to(REPO).as_posix(), "source": relative,
                       "sourceSha256": checksum, "outputSha256": checksum, "width": width, "height": height})
    layouts = []
    for name in ("inventory_renewal", "hero_detail_base", "hero_detail_list", "quick_menu_renew"):
        relative = f"wnd/{name}.csb"
        layouts.append({"source": relative, "sha256": digest(read_source(root, relative))})
    manifest = {"referenceLayouts": layouts, "assets": assets}
    write(DEST / "source-manifest.json",
          (json.dumps(manifest, ensure_ascii=False, indent=2) + "\n").encode(), opt.check)
    print(f"{'Verified' if opt.check else 'Imported'} {len(assets)} original hero inventory sprites.")


if __name__ == "__main__":
    main()
