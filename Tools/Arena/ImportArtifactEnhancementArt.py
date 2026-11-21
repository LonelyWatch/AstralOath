"""Import the rune dial and materials displayed in unit_item_upgrade."""
import argparse
import json
import re
import sys
from pathlib import Path

import numpy as np
from PIL import Image

from ImportArenaArt import REPO, SOURCE_FOLDER, decode_sct, digest, metadata, png_bytes, read_source, write
from ImportSummoningArt import TEXT_IMPORTER, import_sprite, texture_importer

sys.path.insert(0, str(REPO / "Tools/BattleScene"))
from ImportRtaStage import atlas_regions, FOLDER_IMPORTER

DEST = REPO / "Assets/Game/Modules/Home/Res/Textures/Inventory/ArtifactEnhancement"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=Path("D:/sevenepic"))
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    source = (args.source / SOURCE_FOLDER).resolve(strict=True)
    if not args.check:
        DEST.mkdir(parents=True, exist_ok=True)
    metadata(DEST, args.check, FOLDER_IMPORTER)
    assets = []
    for relative in ["item/token_gold.png", "item/icon_bottle_01.png", "img/game_eff_unit_expup.png"] + [
        f"item_arti/{filename}" for i in (1, 2, 3)
        for filename in (f"icon_stone_art{i}.png", f"stone_art{i}_l.jpg")
    ]:
        assets.append(import_sprite(source, relative, DEST / Path(relative).name, args.check))
    sources = {}
    for atlas_name, names in {
        "itemupgrade_mainbg": ("upgrade_bg", "panta_bg", "star_01", "star_02", "star_03"),
        "itemupgrade_main": ("stone", "stone_b", "panta_in", "stone_glow", "flash"),
    }.items():
        base = "effect/" + atlas_name
        atlas, page = (read_source(source, base + ext) for ext in (".atlas", ".sct"))
        for ext in (".sct", ".atlas", ".scsp"):
            sources[base + ext] = digest(read_source(source, base + ext))
        image, _ = decode_sct(page)
        regions = atlas_regions(atlas, image)
        for name in names:
            rgba = regions[name].copy()
            alpha = rgba[:, :, 3:4]
            rgba[:, :, :3] = np.divide(rgba[:, :, :3], alpha, out=np.zeros_like(rgba[:, :, :3]), where=alpha > 0)
            output = png_bytes(Image.fromarray(np.rint(255 * rgba.clip(0, 1)).astype("uint8")))
            path = DEST / (name + ".png")
            write(path, output, args.check)
            importer = texture_importer(path, True).replace("textureCompression: 1", "textureCompression: 0")
            importer = re.sub(r"(?m)^  userData:.*$", "  userData: AstralOath.ImportOverride", importer)
            metadata(path, args.check, importer)
            meta = path.with_name(path.name + ".meta")
            content = meta.read_text(encoding="utf-8-sig")
            if "  userData: AstralOath.ImportOverride" not in content:
                if args.check:
                    raise ValueError(f"Rune atlas overrides missing in {meta}")
                meta.write_text(re.sub(r"(?m)^  userData:.*$", "  userData: AstralOath.ImportOverride", content), encoding="utf-8")
            assets.append({"asset": path.relative_to(REPO).as_posix(), "region": name, "source": base, "outputSha256": digest(output)})
    sources["wnd/unit_item_upgrade.csb"] = digest(read_source(source, "wnd/unit_item_upgrade.csb"))
    manifest = DEST / "source-manifest.json"
    write(manifest, (json.dumps({"sources": sources, "assets": assets}, indent=2) + "\n").encode(), args.check)
    metadata(manifest, args.check, TEXT_IMPORTER)
    print(f"{'Verified' if args.check else 'Imported'} {len(assets)} enhancement sprites.")


if __name__ == "__main__":
    main()
