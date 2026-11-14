"""Import the pinned, read-only Epic Seven UI art needed by Home and Arena."""

import argparse
import hashlib
import io
import json
import re
import sys
import uuid
from pathlib import Path

import numpy as np
from PIL import Image

REPO = Path(__file__).resolve().parents[2]
MODULES = REPO / "Assets/Game/Modules"
SOURCE_FOLDER = "原始资源"
sys.path.insert(0, str(REPO / "Tools/CharacterAssets"))
from convert import decode_scsp, decode_sct

ARENA_IMAGES = """
_bg_black_gold_c _box_black_gold _box_blue2 _box_w4 _box_r_w4 _box_r_op80
_tap_select _tap_select_p _btn_small_white _btn_small_white_p _select _select2 _sorting2
_battle_pvp_icon_vs _bar_1 _bar_2 _bar_5 _bar_9 _bar_23 _dim _white
battle_pvp_icon_win battle_pvp_icon_draw battle_pvp_icon_lose
cm_btn_sorting_up cm_btn_sorting_down cm_hud_top_btn_esc cm_hud_top_btn_back cm_hud_top_btn_help
cm_icon_etcarrow2 cm_icon_etcinfor cm_icon_etclock cm_icon_etcrepeat cm_icon_etczoomer cm_icon_etcclose cm_icon_etcback
icon_menu_all icon_menu_battle icon_menu_battle_add icon_menu_member icon_menu_global icon_menu_arena
icon_menu_talk icon_menu_emoji icon_menu_onlychat icon_menu_noti_setting icon_menu_opacity icon_menu_banned
icon_menu_bgmusic icon_menu_bgpack icon_menu_invite icon_menu_pick icon_menu_roundcard icon_menu_safeguard
icon_quick_setting icon_rta_openingrule vignetting vignetting4 pvp_rta_ss21_d game_hud_target_c2 renew_popup_bg_alpha
pvplive_mod_01 pvplive_mod_02 pvplive_mod_03 pvplive_mod_04 pvplive_mod_05
""".split()
HOME_IMAGES = """
renew_cm_hud_coreloop renew_cm_hud_epicpass renew_cm_hud_evnet renew_gauge_profile_bar renew_gauge_profile_bg
renew_icon_quick_sanctuary renew_icon_quick_secretshop main_hud_btn_bunny main_hud_btn_rhianna
main_hud_btn_arena main_hud_btn_arena_p cm_hud_top_btn_mail cm_hud_top_btn_bag cm_hud_top_btn_alarm
cm_hud_top_btn_chatting cm_hud_top_btn_quick cm_hud_top_titlebg_renew hero_rank
""".split()
BORDERS = {
    "_box_black_gold": (32, 32, 32, 32), "_box_blue2": (10, 10, 10, 10),
    "_box_w4": (13, 13, 13, 13), "_box_r_w4": (16, 16, 16, 16),
    "_box_r_op80": (16, 16, 16, 16), "_tap_select": (24, 20, 24, 20),
    "_tap_select_p": (24, 20, 24, 20), "_btn_small_white": (13, 13, 13, 13),
    "_btn_small_white_p": (13, 13, 13, 13), "_select": (25, 25, 25, 25),
    "_bar_1": (40, 0, 40, 0), "_bar_2": (40, 0, 40, 0), "_bar_5": (40, 0, 40, 0),
    "_bar_23": (30, 0, 30, 0), "renew_popup_bg_alpha": (34, 38, 34, 38),
}


def digest(data):
    return hashlib.sha256(data).hexdigest()


def guid(path):
    return uuid.uuid5(uuid.NAMESPACE_URL, "astraloath:" + path.relative_to(REPO).as_posix()).hex


def read_source(root, relative):
    path = (root / relative).resolve(strict=True)
    if not path.is_relative_to(root):
        raise ValueError(f"Source escaped its root: {relative}")
    return path.read_bytes()


def png_bytes(image):
    stream = io.BytesIO()
    image.save(stream, format="PNG")
    return stream.getvalue()


def write(path, data, check):
    if check:
        if not path.is_file() or path.read_bytes() != data:
            raise ValueError(f"Missing or different import: {path.relative_to(REPO)}")
        return
    path.parent.mkdir(parents=True, exist_ok=True)
    if not path.is_file() or path.read_bytes() != data:
        path.write_bytes(data)


def metadata(path, check, importer):
    meta = path.with_name(path.name + ".meta")
    old = meta.read_text(encoding="utf-8-sig") if meta.is_file() else ""
    match = re.search(r"(?m)^guid: ([0-9a-f]{32})$", old)
    if old and not match:
        raise ValueError(f"Unity GUID missing: {meta}")
    identifier = match[1] if match else guid(path)
    if check:
        if not match or importer.split(":", 1)[0] not in old:
            raise ValueError(f"Unity importer missing: {meta}")
        border = re.search(r"(?m)^  spriteBorder: .*", importer)
        if border and border[0] not in old:
            raise ValueError(f"Unity sprite border differs: {meta}")
    elif not old:
        write(meta, f"fileFormatVersion: 2\nguid: {identifier}\n{importer}".encode(), False)


def texture_importer(path, sprite):
    if sprite:
        template = MODULES / "BattleScene/Res/Textures/Hud/Skills/sk_c1001_1.png.meta"
    else:
        template = MODULES / "CharacterPresentation/Res/Characters/c1031/Textures/portrait/c1031.png.meta"
    text = template.read_text(encoding="utf-8-sig")
    result = "\n".join(line.rstrip() for line in text.splitlines()[2:]) + "\n"
    if path.stem in BORDERS:
        x, y, z, w = BORDERS[path.stem]
        result = re.sub(r"(?m)^  spriteBorder: .*", f"  spriteBorder: {{x: {x}, y: {y}, z: {z}, w: {w}}}", result)
    return result


def source_pngs(module, sources, root, check):
    entries = []
    for source in sources:
        data = read_source(root, source)
        path = MODULES / module / "Res/Textures" / Path(source).name
        with Image.open(io.BytesIO(data)) as image:
            if image.format != "PNG" or not image.convert("RGBA").getchannel("A").getbbox():
                raise ValueError(f"Invalid source sprite: {source}")
            width, height = image.size
        write(path, data, check)
        metadata(path, check, texture_importer(path, sprite=True))
        entries.append({"asset": path.relative_to(REPO).as_posix(), "source": source,
                        "sourceSha256": digest(data), "outputSha256": digest(data),
                        "width": width, "height": height})
    return entries


def write_converted(module, relative, data, sources, check, conversion):
    path = MODULES / module / "Res" / relative
    write(path, data, check)
    importer = texture_importer(path, sprite=not conversion.get("premultipliedAlpha")) if path.suffix == ".png" else (
        "TextScriptImporter:\n  externalObjects: {}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n")
    metadata(path, check, importer)
    return {"asset": path.relative_to(REPO).as_posix(), "sources": sources,
            "outputSha256": digest(data), "conversion": conversion}


def home_portrait(root, check):
    model = "portrait/c5009"
    scsp, atlas, sct = (read_source(root, model + suffix) for suffix in (".scsp", ".atlas", ".sct"))
    document, report = decode_scsp(scsp)
    if "idle" not in document["animations"] or not any(skin["name"] == "normal" for skin in document["skins"]):
        raise ValueError("The selected Home animation or skin is missing.")
    image, texture = decode_sct(sct)
    if image.size != (1636, 1032) or "c5009.sct" not in atlas.decode("utf-8-sig"):
        raise ValueError("Home portrait atlas does not match its authored page.")
    converted = []
    inputs = (
        ("Animations/c5009.spine.json", (json.dumps(document, ensure_ascii=False, separators=(",", ":")) + "\n").encode(),
         model + ".scsp", scsp, {"spineVersion": report["spineVersion"], "converterVersion": report["converterVersion"],
                                  "animations": report["animations"], "skin": "normal", "scale": 0.01}),
        ("Atlas/c5009.atlas.txt", atlas, model + ".atlas", atlas, {"texturePage": "c5009.sct"}),
        ("Textures/c5009.png", png_bytes(image), model + ".sct", sct,
         {"premultipliedAlpha": True, "width": image.width, "height": image.height}),
    )
    for relative, data, source, original, conversion in inputs:
        converted.append(write_converted("Home", relative, data,
                                         [{"source": source, "sha256": digest(original)}], check, conversion))
    return converted



def courtyard(root, check):
    source = "bg/tireil_4_garden"
    atlas = read_source(root, source + ".atlas")
    texture = read_source(root, source + ".sct")
    image, _ = decode_sct(texture)
    geometry = re.compile(
        r"(?m)^([^:\r\n ]+)\r?\n +rotate: (true|false)\r?\n +xy: (\d+), (\d+)\r?\n +size: (\d+), (\d+)"
    )
    regions = {row[0]: row[1:] for row in geometry.findall(atlas.decode("utf-8-sig"))}

    def region(name):
        rotate, x, y, width, height = regions[name]
        x, y, width, height = map(int, (x, y, width, height))
        w, h = (height, width) if rotate == "true" else (width, height)
        piece = image.crop((x, y, x + w, y + h))
        if rotate == "true":
            piece = piece.transpose(Image.Transpose.ROTATE_270)
        # SCT pages store premultiplied color. The derived UI sprite uses straight alpha.
        pixels = np.asarray(piece, dtype=np.uint8).copy()
        alpha = pixels[:, :, 3:4].astype(np.uint32)
        rgb = pixels[:, :, :3].astype(np.uint32)
        pixels[:, :, :3] = np.minimum(255, (rgb * 255 + alpha // 2) // np.maximum(1, alpha))
        pixels[alpha[:, :, 0] == 0, :3] = 0
        return Image.fromarray(pixels, "RGBA")

    # Still scene assembled from exact source atlas regions. Positions and scales are
    # screen-matched to the room reference, so the original world atlas remains intact.
    layers = (
        ("garden_11", (0, 0), (1580, 435), 0),
        ("garden_10", (0, 115), (1580, 128), 0),
        ("garden_8", (-28, 192), (1636, 247), 0),
        ("garden_9", (800, 42), (801, 365), 0),
        ("garden_0", (0, 311), (1580, 409), 0),
        ("garden_4", (-118, 252), (1003, 200), 34),
        ("garden_4", (743, 246), (913, 200), 34),
        ("forest2_2", (-165, -12), (540, 456), 0),
        ("forest2_3", (386, -111), (755, 557), 0),
        ("garden_1", (-40, -32), (720, 175), 0),
        ("forest2_18_3", (-23, 573), (419, 198), 0),
        ("forest2_17_1", (38, 594), (292, 122), 0),
        ("forest2_16_2", (630, 578), (186, 145), 0),
        ("forest2_15_1", (757, 640), (202, 108), 0),
    )
    result = Image.new("RGBA", (1580, 720), (9, 15, 13, 255))
    for name, position, size, bottom_feather in layers:
        layer = region(name).resize(size, Image.Resampling.LANCZOS)
        if bottom_feather:
            alpha = np.array(layer.getchannel("A"), dtype=np.float32)
            alpha[-bottom_feather:, :] *= np.linspace(1.0, 0.0, bottom_feather)[:, None]
            layer.putalpha(Image.fromarray(alpha.astype(np.uint8)))
        result.alpha_composite(layer, position)
    sources = [{"source": source + ext, "sha256": digest(raw)}
               for ext, raw in ((".atlas", atlas), (".sct", texture))]
    return write_converted("Arena", "Textures/room_courtyard.png", png_bytes(result), sources, check,
                           {"method": "spine-atlas-static-layer-composition", "width": 1580, "height": 720,
                            "layers": [{"region": name, "topLeft": xy, "size": size, "bottomFeatherPixels": feather}
                                       for name, xy, size, feather in layers]})



def manifest(module, entries, layouts, root, check):
    result = {"schemaVersion": 1, "release": "pack-15-r4262", "assets": entries,
              "sourceLayouts": [{"source": path, "sha256": digest(read_source(root, path))} for path in layouts]}
    path = MODULES / module / "Res/Textures/source-manifest.json"
    write(path, (json.dumps(result, ensure_ascii=False, indent=2) + "\n").encode(), check)
    metadata(path, check, "TextScriptImporter:\n  externalObjects: {}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=Path("D:/sevenepic"))
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    root = (args.source / SOURCE_FOLDER).resolve(strict=True)
    if REPO.resolve().is_relative_to(root):
        raise ValueError("The project must stay outside the source archive.")
    arena = source_pngs("Arena", [f"img/{name}.png" for name in ARENA_IMAGES] +
                        ["emblem/icon_pvp_sa_league_place.png"], root, args.check)
    arena.append(courtyard(root, args.check))
    home = source_pngs("Home", [f"img/{name}.png" for name in HOME_IMAGES] +
                       ["bgpack/lobby_bg3.png", "face/c5009_s.png", "item/border/icon_border_pvp_season20_rank_3.png"], root, args.check)
    home += home_portrait(root, args.check)
    manifest("Arena", arena, [
        "wnd/pvplive_base.csb", "wnd/pvplive_mock_battle_item.csb", "wnd/pvplive_lounge_creat.csb",
        "wnd/pvplive_mod_card.csb", "wnd/pvplive_lounge_pw.csb", "wnd/pvplive_lounge.csb", "wnd/chat.csb",
    ], root, args.check)
    manifest("Home", home, ["wnd/lobby_ui_renew.csb", "wnd/lobby_ui_ver2.csb"], root, args.check)
    print(f"{'Verified' if args.check else 'Imported'} {len(arena)} Arena and {len(home)} Home assets.")


if __name__ == "__main__":
    main()
