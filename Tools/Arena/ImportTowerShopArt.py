"""Import the specific arena and shop sprites used by the 2026-11-28 reference."""
from pathlib import Path
import argparse
from ImportArenaArt import MODULES, BORDERS, UNCOMPRESSED_UI, metadata, read_source, texture_importer, write

ARENA = ["img/base_pvp", "img/pvp_slot_select", "img/_box_r_w2", "img/icon_menu_pvplog",
         "img/icon_menu_store", "img/battle_pvp_hud_bar_bg", "img/game_hud_bar_none",
         "img/_btn_white", "img/icon_filter_knight", "img/cm_icon_etcbp"] + [
             "img/game_hud_bar_" + element for element in ("fire","ice","wind","dark","light")] + [
             "emblem/icon_pvp_sa_league_" + rank for rank in
             ("bronze", "silver", "gold", "master", "challenger", "champion", "emperor", "legend")]
SHOP = ["img/renew_shop_bg", "img/renew_box_shop_short", "item/token_pvpgold", "img/_btn_small_white",
        "shop/shop_product_muragora_b", "shop/shop_product_stamina_b"] + [
            "shop/shop_product_" + key + "_b" for key in ("est2e", "est4e", "est2j", "est4j")] + [
            "shop/sp_selectbox_pvp_b_" + suffix for suffix in
            ("immune","counter","att","res","cri_dmg","max_hp","speed","penetrate","scar","acc","cri")]
SHOP += ["img/" + name for name in ("_tap_select_blue3", "icon_menu_seasonalpackage", "icon_menu_package",
         "icon_menu_heavenlystone", "icon_menu_normal", "icon_menu_skin", "icon_menu_event_shop",
         "icon_menu_info_2", "renew_shop_bg_clipping")]
SHOP += [f"img/shop_icon_sale{percent}_s" for percent in range(10, 80, 10)]

def main():
    args = argparse.ArgumentParser()
    args.add_argument("--source", type=Path, default=Path("D:/sevenepic/原始资源"))
    args.add_argument("--check", action="store_true")
    options = args.parse_args()
    root = options.source.resolve(strict=True)
    BORDERS.update({"_box_r_w2": (14, 14, 14, 14), "pvp_slot_select": (77, 8, 77, 8), "_btn_white": (10, 10, 10, 10)})
    for module, folder, images in [("Arena", "Tower", ARENA), ("Shop", "Arena", SHOP)]:
        for key in images:
            path = MODULES / module / "Res/Textures" / folder / (Path(key).name + ".png")
            UNCOMPRESSED_UI.add(path.stem)
            write(path, read_source(root, key + ".png"), options.check)
            metadata(path, options.check, texture_importer(path, True))

if __name__ == "__main__":
    main()
