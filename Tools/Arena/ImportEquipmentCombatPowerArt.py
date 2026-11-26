"""Import the missing original equipment CP arrow and verify the existing crossed-swords sprite."""
import argparse
import json
from pathlib import Path

from PIL import Image

from ImportArenaArt import REPO, SOURCE_FOLDER, digest, metadata, read_source, texture_importer, write

DEST = REPO / "Assets/Game/Modules/Home/Res/Textures/Inventory/Equipment/Management"
REUSED = REPO / "Assets/Game/Modules/Home/Res/Textures/Inventory/Roster/cm_icon_etcbp.png"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=Path("D:/sevenepic"))
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    source = (args.source / SOURCE_FOLDER).resolve(strict=True)
    records = []
    for stem, path, size, reused in (
        ("cm_icon_etcarrow", DEST / "cm_icon_etcarrow.png", (18, 12), False),
        ("cm_icon_etcbp", REUSED, (27, 26), True),
    ):
        relative = "img/" + stem + ".png"
        data = read_source(source, relative)
        if reused:
            if not path.is_file() or path.read_bytes() != data:
                raise ValueError("The existing CP icon differs from its original source: " + str(path))
        else:
            write(path, data, args.check)
            importer = texture_importer(path, True).replace("textureCompression: 1", "textureCompression: 0")
            importer = importer.replace("  userData:", "  userData: AstralOath.ImportOverride")
            metadata(path, args.check, importer)
        with Image.open(path) as image:
            if image.size != size:
                raise ValueError("Unexpected original CP sprite dimensions: " + relative)
        record = {"source": relative, "asset": path.relative_to(REPO).as_posix(),
                  "sourceSha256": digest(data), "outputSha256": digest(path.read_bytes()),
                  "size": list(size), "reused": reused, "slicing": "native sprite; no scale-nine borders"}
        if stem == "cm_icon_etcarrow":
            record.update({"sourceNode": "Layer/n_show_eff/arrow_item", "sourcePosition": [639, 227],
                           "sourceScale": [1.25, 1.25], "sourceRotationSkew": [-90, -90],
                           "unityRotationZ": 90, "sourceRgb": [255, 120, 0]})
        else:
            record.update({"sourceNodes": ["Layer/n_show_eff/n_stat1/power", "Layer/n_show_eff/n_stat2/power"],
                           "sourceRgb": [51, 152, 238]})
        records.append(record)
    layout = "wnd/equip_change_popup.csb"
    manifest = {"sourceRelease": "pack-15-r4262", "layout": layout,
                "layoutSha256": digest(read_source(source, layout)), "assets": records}
    target = DEST / "combat-power-source.json"
    write(target, (json.dumps(manifest, ensure_ascii=False, indent=2) + "\n").encode(), args.check)
    metadata(target, args.check, "TextScriptImporter:\n  externalObjects: {}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n")
    print("Verified CP arrow and reused icon." if args.check else "Imported CP arrow; verified reused icon.")


if __name__ == "__main__":
    main()
