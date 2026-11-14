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
from PIL import Image, ImageChops, ImageDraw

REPO = Path(__file__).resolve().parents[2]
MODULES = REPO / "Assets/Game/Modules"
SOURCE_FOLDER = "原始资源"
sys.path.insert(0, str(REPO / "Tools/CharacterAssets"))
from convert import decode_scsp, decode_sct

ARENA_IMAGES = """
_bg_black_gold_c _bg_black_gold_s _box_black_gold _box_blue2 _box_w4 _box_r_w4 _box_r_op80
_tap_select _tap_select_p _btn_small_white _btn_small_white_p _btn_white_flat _btn_white_flat_p _select _select2 _sorting2
_battle_pvp_icon_vs _bar_1 _bar_2 _bar_4 _bar_5 _bar_9 _bar_23 _dim _white
battle_pvp_icon_win battle_pvp_icon_draw battle_pvp_icon_lose
cm_btn_sorting_up cm_btn_sorting_down cm_btn_sorting_up_p cm_btn_sorting_down_p cm_hud_top_btn_esc cm_hud_top_btn_back cm_hud_top_btn_help
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
    "_btn_white_flat": (25, 25, 25, 25), "_btn_white_flat_p": (25, 25, 25, 25),
    "_bar_1": (40, 0, 40, 0), "_bar_2": (40, 0, 40, 0), "_bar_5": (40, 0, 40, 0),
    "emoji_picker_frame": (14, 14, 14, 14), "arena_create_dialog_bg": (54, 54, 54, 54),
    "directory_create": (14, 16, 14, 12), "directory_create_pressed": (14, 16, 14, 12),
    "directory_refresh": (14, 16, 14, 12), "directory_refresh_pressed": (14, 16, 14, 12),
    "directory_rule": (32, 4, 32, 4), "directory_tab": (41, 0, 10, 0),
    "_bar_4": (32, 0, 32, 0), "_bar_23": (30, 0, 30, 0), "renew_popup_bg_alpha": (34, 38, 34, 38),
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
        if path.stem in UNCOMPRESSED_UI and any(value != "0" for value in re.findall(r"(?m)^\s+textureCompression: (\d+)$", old)):
            raise ValueError(f"Unity compression would alter interface artwork: {meta}")
    elif not old:
        write(meta, f"fileFormatVersion: 2\nguid: {identifier}\n{importer}".encode(), False)


UNCOMPRESSED_UI = {"emoji_picker_frame", "c1001_avatar_room", "arena_avatar_frame", "arena_create_dialog_bg", "directory_tab", "directory_rule", "directory_refresh", "directory_refresh_pressed", "directory_create", "directory_create_pressed"}


def texture_importer(path, sprite):
    if sprite:
        template = MODULES / "BattleScene/Res/Textures/Hud/Skills/sk_c1001_1.png.meta"
    else:
        template = MODULES / "CharacterPresentation/Res/Characters/c1031/Textures/portrait/c1031.png.meta"
    text = template.read_text(encoding="utf-8-sig")
    result = "\n".join(line.rstrip() for line in text.splitlines()[2:]) + "\n"
    if path.stem in UNCOMPRESSED_UI:
        result = re.sub(r"(?m)^(\s+textureCompression:) \d+$", r"\1 0", result)
    if path.stem in UNCOMPRESSED_UI:
        result = re.sub(r"(?m)^  userData:.*$", "  userData: AstralOath.ImportOverride", result)
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



def arena_room_art(root, check):
    portrait_source = "face/c1001_su.png"
    portrait_data = read_source(root, portrait_source)
    portrait_crop = (72, 17, 212, 157)
    portrait = Image.open(io.BytesIO(portrait_data)).convert("RGBA").crop(portrait_crop)
    portrait = portrait.transpose(Image.Transpose.FLIP_LEFT_RIGHT)
    edge = Image.new("L", (portrait.width * 4, portrait.height * 4))
    ImageDraw.Draw(edge).ellipse((0, 0, edge.width - 1, edge.height - 1), fill=255)
    edge = edge.resize(portrait.size, Image.Resampling.LANCZOS)
    portrait.putalpha(ImageChops.multiply(portrait.getchannel("A"), edge))

    border_source = "img/_box_black_gold.png"
    border_data = read_source(root, border_source)
    border_crop = (31, 31, 85, 85)
    border = Image.open(io.BytesIO(border_data)).convert("RGBA").crop(border_crop)

    frame_source = "item/border/icon_border_pvp_season20_rank_3.png"
    frame_data = read_source(root, frame_source)
    variants = (
        ("c1001_avatar_room.png", png_bytes(portrait), portrait_source, portrait_data,
         {"method": "original-asset-crop", "cropLtrb": portrait_crop, "mirrorX": True, "mask": "circle", "resizesSource": False}),
        ("emoji_picker_frame.png", png_bytes(border), border_source, border_data,
         {"method": "original-asset-crop", "cropLtrb": border_crop, "scale9InsetsLtrb": [14, 14, 14, 14],
          "sourceLayout": "wnd/chat_emoji_tip.csb"}),
        ("arena_avatar_frame.png", frame_data, frame_source, frame_data, {"method": "original-asset", "compression": "none"}),
    )
    return [write_converted("Arena", "Textures/" + name, data, [{"source": source, "sha256": digest(raw)}], check, conversion)
            for name, data, source, raw, conversion in variants]


def arena_create_dialog_art(root, check):
    source = "img/renew_popup_bg_alpha.png"
    raw = read_source(root, source)
    source_image = Image.open(io.BytesIO(raw)).convert("RGBA")
    alpha = np.array(source_image.getchannel("A"))
    extra_alpha = np.rint(np.interp(np.arange(source_image.height), [0, 231, 434], [15, 15, 0])).astype(np.uint8)
    # The glass fill is 230 in the source; keep the original frame and edge alpha.
    alpha += (alpha == 230) * extra_alpha[:, None]
    source_image.putalpha(Image.fromarray(alpha))
    return write_converted("Arena", "Textures/arena_create_dialog_bg.png", png_bytes(source_image),
                           [{"source": source, "sha256": digest(raw)}], check,
                           {"method": "source-glass-opacity-adjustment", "sourceLayout": "wnd/pvplive_lounge_creat.csb",
                            "sourceGlassAlpha": 230, "upperGlassAlpha": 245, "lowerGlassAlpha": 230,
                            "scale9InsetsLtrb": [54, 54, 54, 54]})


def arena_directory_art(root, check):
    specs = (
        ("directory_create", "_btn_gold", [14, 12, 124, 33]),
        ("directory_create_pressed", "_btn_gold_p", [14, 12, 124, 33]),
        ("directory_refresh", "_btn_small_black", [14, 12, 8, 6]),
        ("directory_refresh_pressed", "_btn_small_black_p", [14, 12, 8, 6]),
        ("directory_rule", "_bar_2", [32, 4, 34, 2]),
        ("directory_tab", "_select2", [41, 0, 9, 112]),
    )
    entries = []
    for output, source_name, cap in specs:
        source = f"img/{source_name}.png"
        data = read_source(root, source)
        entries.append(write_converted("Arena", f"Textures/{output}.png", data,
                       [{"source": source, "sha256": digest(data)}], check,
                       {"method": "native-scale-nine", "sourceLayout": "wnd/pvplive_base.csb", "capRectXywh": cap}))
    return entries


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
    arena += arena_room_art(root, args.check)
    arena.append(arena_create_dialog_art(root, args.check))
    arena += arena_directory_art(root, args.check)
    home = source_pngs("Home", [f"img/{name}.png" for name in HOME_IMAGES] +
                       ["bgpack/lobby_bg3.png", "face/c5009_s.png", "item/border/icon_border_pvp_season20_rank_3.png"] +
                       [f"item/sticker/ma_sticker_emo{index}.png" for index in (1, 2, 3)], root, args.check)
    home += home_portrait(root, args.check)
    manifest("Arena", arena, [
        "wnd/pvplive_base.csb", "wnd/pvplive_mock_battle_item.csb", "wnd/pvplive_lounge_creat.csb",
        "wnd/pvplive_mod_card.csb", "wnd/pvplive_lounge_pw.csb", "wnd/pvplive_lounge.csb", "wnd/chat.csb",
        "wnd/chat_emoji_tip.csb", "wnd/chat_emoji_tip_item.csb",
    ], root, args.check)
    manifest("Home", home, ["wnd/lobby_ui_renew.csb", "wnd/lobby_ui_ver2.csb"], root, args.check)
    print(f"{'Verified' if args.check else 'Imported'} {len(arena)} Arena and {len(home)} Home assets.")


if __name__ == "__main__":
    main()
