"""Import the pinned original gacha interface and artifact illustrations."""
import argparse
import csv
import io
import json
import re
from pathlib import Path

from PIL import Image
from ImportArenaArt import REPO, SOURCE_FOLDER, digest, metadata, read_source, texture_importer, write

NATIVE_UI = (
    "_btn_gacha_gray", "_btn_gacha_gray_p", "_btn_gacha_gold", "_btn_gacha_gold_p",
    "gacha_title_bg", "gacha_title_arti_bg", "gacha_get_bg1", "gacha_get_bg2",
    "gacha_10bg_fire", "gacha_10bg_ice", "gacha_10bg_wind", "gacha_10bg_light", "gacha_10bg_dark",
    "icon_menu_bookmark", "icon_menu_bookmarkmoon", "gacha_renew_select_bg",
    "_btn_circle_black_76", "_btn_circle_black_76_p", "_btn_small_black", "_btn_small_black_p",
    "icon_menu_info", "icon_quick_tmp_inven", "renew_icon_quick_setting", "icon_menu_change",
    "banner_cliping", "banner_cliping_outline", "gacha_banner_title", "cm_btn_helpd",
    "cm_hud_top_btn_plus", "cm_icon_etcarrow2", "_grow_white", "renew_popup_bg_2_alpha",
    "_itembg_stuff_2", "_btn_gold_r_2", "_btn_gold_r_p_2", "_btn_blue_r_3", "_btn_blue_r_p_3",
    "gacha_10frame_fire", "gacha_10frame_ice", "gacha_10frame_wind", "gacha_10frame_light",
    "gacha_10frame_dark", "gacha_new", "cm_icon_etcshare", "cm_ballonsm", "z_tile_bar",
    "gacha_wish_deco_3", "gacha_wish_deco_4", "gacha_wish_deco_5", "gacha_wish_deco_6",
    "gacha_wish_deco_7", "gacha_wish_deco_8", "arti_gauge_bg", "cm_icon_tmp_inven", "gacha_wish_slot_arti", "gacha_wish_slot_cir", "gacha_wish_box_1", "gacha_wish_box_2",
)
LAYOUTS = ("gacha_renew", "gacha_renew_covenant", "gacha_renew_pickup", "gacha_renew_info_popup", "gacha_renew_bar_banner", "gacha_special_summon_p", "top_bar", "top_bar_token", "gacha_unit_bar",
           "gacha_wish_choose", "gacha_result_unit", "gacha_result_artifact", "gacha_summary",
           "gacha_inven", "gacha_skip_btn", "inventory_artifact_popup", "artifact_detail")
HOME = REPO / "Assets/Game/Modules/Home"
SUMMON = REPO / "Assets/Game/Modules/Summon"
TEXT_IMPORTER = "TextScriptImporter:\n  externalObjects: {}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n"

def import_sprite(root, relative, dest, check):
    data = read_source(root, relative)
    with Image.open(io.BytesIO(data)) as image:
        width, height = image.size
    write(dest, data, check)
    importer = texture_importer(dest, sprite=True)
    importer = re.sub(r"(?m)^(\s+textureCompression:) \d+$", r"\1 0", importer)
    importer = re.sub(r"(?m)^  userData:.*$", "  userData: AstralOath.ImportOverride", importer)
    metadata(dest, check, importer)
    checksum = digest(data)
    return {"asset": dest.relative_to(REPO).as_posix(), "source": relative, "sourceSha256": checksum,
            "outputSha256": checksum, "width": width, "height": height}

def manifest(dest, root, layouts, assets, check):
    recorded = {"assets": assets, "referenceLayouts": [
        {"source": f"wnd/{name}.csb", "sha256": digest(read_source(root, f"wnd/{name}.csb"))} for name in layouts
    ]}
    write(dest, (json.dumps(recorded, ensure_ascii=False, indent=2) + "\n").encode(), check)
    metadata(dest, check, TEXT_IMPORTER)

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=Path("D:/sevenepic"))
    parser.add_argument("--check", action="store_true")
    opts = parser.parse_args()
    root = (opts.source / SOURCE_FOLDER).resolve(strict=True)
    rows = csv.DictReader((REPO / "Config/Tables/artifact_templates.csv").open(encoding="utf-8"))
    portraits = [import_sprite(root, f"item_arti/{row['icon']}_l.jpg",
                 HOME / "Res/Textures/Artifacts" / (row["id"] + ".jpg"), opts.check) for row in rows]
    rows_file = REPO / "Config/Tables/artifact_templates.csv"
    with rows_file.open(encoding="utf-8") as stream:
        rows = list(csv.DictReader(stream))
    portraits += [import_sprite(root, f"item_arti/{row['icon']}_fu.png",
                  HOME / "Res/Textures/Artifacts/Full" / (row["id"] + ".png"), opts.check) for row in rows]
    combat = HOME / "Res/Textures/Artifacts/Combat"
    portraits += [import_sprite(root, f"item_arti/icon_{row['icon']}.png",
                  combat / (row["id"] + ".png"), opts.check) for row in rows]
    metadata(combat, opts.check, "folderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n")
    manifest(HOME / "Res/Textures/Artifacts/source-manifest.json", root, LAYOUTS[-2:], portraits, opts.check)
    artwork = [import_sprite(root, f"img/{name}.png", SUMMON / "Res/Textures" / (name + ".png"), opts.check)
               for name in NATIVE_UI]
    for name in ("gacha_wish_bg_2", "gacha_wish_bg_3", "banner_gacha_wish", "gacha_c1024_01_bg", "gacha_c1024_01_banner"):
        artwork.append(import_sprite(root, f"banner/{name}.png", SUMMON / "Res/Textures" / (name + ".png"), opts.check))
    for name in ("renew_popup_bg_alpha", "_btn_blue", "_btn_gold", "_box_w5", "gacha_title_flag", "gacha_wish_btn", "gacha_wish_btn_p", "gacha_wish_mask", "gacha_wish_bar"):
        artwork.append(import_sprite(root, f"img/{name}.png", SUMMON / "Res/Textures" / (name + ".png"), opts.check))
    for name in ("arti_frame_deco_1", "arti_frame_small"):
        artwork.append(import_sprite(root, f"item_arti/{name}.png",
                                     SUMMON / "Res/Textures" / (name + ".png"), opts.check))
    for name in ("token_ticket_rare", "token_rarecoin"):
        artwork.append(import_sprite(root, f"item/{name}.png", SUMMON / "Res/Textures" / (name + ".png"), opts.check))
    manifest(SUMMON / "Res/Textures/source-manifest.json", root, LAYOUTS[:-2], artwork, opts.check)
    print(f"{'Verified' if opts.check else 'Imported'} {len(artwork)} native UI images and {len(portraits)} artifact cards.")

if __name__ == "__main__":
    main()
