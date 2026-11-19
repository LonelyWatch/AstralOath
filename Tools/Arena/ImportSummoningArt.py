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
    "gacha_wish_deco_1", "gacha_wish_deco_2", "btn_minus", "glow_white_circle_2", "_checked",
    "_btn_white", "icon_menu_good", "cm_ballonsm_p", "cm_ballonsm_arrow",
    "renew_main_hud_btn_deco_pickup", "cm_icon_star_big", "glow_banner_right", "_bar_23", "_itembg_stuff_3",
    "_btn_gacha_gray", "_btn_gacha_gray_p", "_btn_gacha_gold", "_btn_gacha_gold_p",
    "gacha_title_bg", "gacha_title_arti_bg", "gacha_get_bg1", "gacha_get_bg2",
    "gacha_10bg_fire", "gacha_10bg_ice", "gacha_10bg_wind", "gacha_10bg_light", "gacha_10bg_dark",
    "icon_menu_bookmark", "icon_menu_bookmarkmoon", "gacha_renew_select_bg",
    "_btn_green", "_btn_green_p", "_btn_gold_p",
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
# Cocos cap rects in the source sprite's top-left coordinate system.
DRAW_CAP_RECTS = {
    "cm_ballonsm": (20, 20, 18, 18), "cm_ballonsm_p": (20, 20, 18, 18),
    "_btn_white": (18, 18, 116, 25), "_bar_23": (32, 4, 10, 4), "z_tile_bar": (5, 1, 54, 2),
    "gacha_wish_btn": (80, 11, 7, 53), "gacha_wish_btn_p": (80, 11, 7, 53),
    "_btn_gold_r_2": (100, 64, 1, 1), "_btn_gold_r_p_2": (100, 64, 1, 1),
    "_btn_blue_r_3": (100, 64, 1, 1), "_btn_blue_r_p_3": (100, 64, 1, 1),
    "_btn_gacha_gray": (40, 11, 176, 54), "_btn_gacha_gray_p": (40, 11, 176, 54),
    "_btn_gacha_gold": (40, 11, 176, 54), "_btn_gacha_gold_p": (40, 11, 176, 54),
    "_btn_small_black_p": (14, 12, 8, 6), "_btn_small_black": (14, 14, 8, 6),
    "_btn_gold": (40, 11, 72, 39), "_btn_gold_p": (40, 11, 72, 39),
    "_btn_green": (14, 14, 124, 33), "_btn_green_p": (14, 14, 124, 33),
}
LAYOUTS = ("gacha_renew", "gacha_renew_covenant", "gacha_renew_pickup", "gacha_renew_info_popup", "gacha_renew_bar_banner", "gacha_special_summon_p", "top_bar", "top_bar_token", "gacha_unit_bar",
           "gacha_wish", "gacha_wish_choose", "gacha_result_unit", "gacha_result_artifact", "gacha_summary",
           "gacha_inven", "gacha_skip_btn", "inventory_artifact_popup", "artifact_detail")
HOME = REPO / "Assets/Game/Modules/Home"
SUMMON = REPO / "Assets/Game/Modules/Summon"
TEXT_IMPORTER = "TextScriptImporter:\n  externalObjects: {}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n"

def linear_black_overlay(image):
    # Cocos mixed the black badge directly into sRGB. Preserve its visible transmission
    # when Unity's linear framebuffer blends it over the gold or blue button.
    def alpha(value):
        transmission = 1 - value / 255
        linear = transmission / 12.92 if transmission <= .04045 else ((transmission + .055) / 1.055) ** 2.4
        return round(255 * (1 - linear))
    overlay = image.convert("RGBA")
    overlay.putalpha(overlay.getchannel("A").point([alpha(value) for value in range(256)]))
    output = io.BytesIO()
    overlay.save(output, format="PNG")
    return output.getvalue()

def import_sprite(root, relative, dest, check, linear_price=False):
    data = read_source(root, relative)
    source_checksum = digest(data)
    with Image.open(io.BytesIO(data)) as image:
        width, height = image.size
        if linear_price:
            data = linear_black_overlay(image)
    write(dest, data, check)
    importer = texture_importer(dest, sprite=True)
    importer = re.sub(r"(?m)^(\s+textureCompression:) \d+$", r"\1 0", importer)
    importer = re.sub(r"(?m)^  userData:.*$", "  userData: AstralOath.ImportOverride", importer)
    cap = DRAW_CAP_RECTS.get(Path(relative).stem)
    if cap:
        left, top, mid_width, mid_height = cap
        right, bottom = width - left - mid_width, height - top - mid_height
        border = f"  spriteBorder: {{x: {left}, y: {bottom}, z: {right}, w: {top}}}"
        pattern = r"(?m)^  spriteBorder: [^\r\n]+"
        importer = re.sub(pattern, border, importer)
        meta = dest.with_name(dest.name + ".meta")
        if not check and meta.is_file():
            meta_data = meta.read_bytes()
            updated, count = re.subn(pattern.encode(), border.encode(), meta_data)
            if count != 1:
                raise ValueError(f"Missing sprite border in {meta}")
            if updated != meta_data:
                meta.write_bytes(updated)
    metadata(dest, check, importer)
    record = {"asset": dest.relative_to(REPO).as_posix(), "source": relative, "sourceSha256": source_checksum,
              "outputSha256": digest(data), "width": width, "height": height}
    if linear_price:
        record["conversion"] = "sRGB black-overlay transmission to linear alpha"
    return record

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
    for name in ("_btn_small_black", "_btn_small_black_p"):
        artwork.append(import_sprite(root, f"img/{name}.png",
                                     SUMMON / "Res/Textures" / (name + "_linear.png"), opts.check, linear_price=True))
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
