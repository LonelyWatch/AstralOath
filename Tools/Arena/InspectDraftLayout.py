"""Audit authored Cocos Studio draft geometry without modifying the source archive."""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import re
import struct

from PIL import Image, ImageDraw, ImageOps


LAYOUTS = (
    "pvplive_ready", "pvplive_ready_watching", "pvplive_ready_formation",
    "pvplive_ready_pre_ben", "pvplive_ready_pre_ben_eff", "pvplive_pick_sel",
    "ban_popup", "ban_popup_header", "unit_bar", "unit_bar_simple", "unit_list", "unit_list_unfold",
)
CONTACT_ASSETS = """
    img/base_pvplive_vs.png img/base_pvplive_eff.png img/_bg_black_gold_c.png
    img/pvplive_progress_bg.png img/pvplive_progress_bar_blue.png img/pvplive_progress_bar_red.png
    img/pvplive_slot1.png img/pvplive_slot2.png img/pvplive_slot3.png img/pvplive_slot4.png img/pvplive_slot5.png
    img/pvplive_slot1_blue.png img/pvplive_slot1_red.png img/pvplive_slot3_safeguard.png
    img/hero_bg_normal.png img/hero_bg_selected.png img/hero_bg_mask.png face/c1001_l.png
    img/icon_menu_banned.png img/_cm_banned_res.png img/_grow.png img/_grow_s.png
    img/_btn_red.png img/_btn_green.png img/renew_popup_bg.png
    img/hero_config_bg.png img/hero_config_panelleader.png img/hero_config_panelback.png
    img/hero_config_btn_change.png img/cm_hero_cirbg2.png img/_hero_s_frame_ally.png font/score_0.png
""".split()
RESOURCE_REFERENCE = re.compile(rb"(?:Default|img|face|font|emblem)/[A-Za-z0-9_./-]+\.(?:png|fnt|ttf)", re.I)
WIDGET_RESOURCES = {
    "Sprite": (1,), "ImageView": (1,), "LoadingBar": (1,), "Button": (1, 2, 3),
    "CheckBox": (1, 2, 3, 4, 5), "Panel": (1,), "ListView": (1,), "ScrollView": (1,),
}


class Csb:
    """Read only the public Cocos Studio 2.x NodeTree and WidgetOptions fields used here."""

    def __init__(self, data):
        self.data = data

    def read(self, fmt, at):
        return struct.unpack_from("<" + fmt, self.data, at)

    def pointer(self, at):
        return at + self.read("I", at)[0] if at else 0

    def fields(self, at):
        if not at:
            return []
        vtable = at - self.read("i", at)[0]
        size = self.read("H", vtable)[0]
        if size < 4 or size & 1 or vtable < 0 or vtable + size > len(self.data):
            raise ValueError(f"Invalid FlatBuffer vtable at 0x{at:x}.")
        return [at + off if off else 0 for off in self.read("H" * ((size - 4) // 2), vtable + 4)]

    @staticmethod
    def field(fields, index):
        return fields[index] if index < len(fields) else 0

    def string(self, field):
        at = self.pointer(field)
        if not at:
            return None
        size = self.read("I", at)[0]
        if size > len(self.data) - at - 4:
            raise ValueError(f"Invalid FlatBuffer string at 0x{at:x}.")
        return self.data[at + 4:at + 4 + size].decode("utf-8")

    def row(self, fields, index):
        return self.fields(self.pointer(self.field(fields, index)))

    def vector(self, field):
        at = self.pointer(field)
        if not at:
            return []
        length = self.read("I", at)[0]
        if length > (len(self.data) - at - 4) // 4:
            raise ValueError(f"Invalid FlatBuffer vector at 0x{at:x}.")
        return [self.pointer(at + 4 + i * 4) for i in range(length)]

    def node(self, at, parent="", order=0):
        tree = self.fields(at)
        kind = self.string(self.field(tree, 0))
        options = self.row(self.row(tree, 2), 0)
        widget = options if kind in ("Node", "GameNode") else self.row(options, 0)
        name = self.string(self.field(widget, 0)) or "(unnamed)"
        path = f"{parent}/{name}" if parent else name

        def scalar(index, fmt, default):
            location = self.field(widget, index)
            return self.read(fmt, location)[0] if location else default

        def pair(index, default):
            location = self.field(widget, index)
            return [round(value, 3) for value in self.read("ff", location)] if location else default

        color = self.field(widget, 10)
        abgr = self.read("BBBB", color) if color else (255, 255, 255, 255)
        resources = []
        for i in WIDGET_RESOURCES.get(kind, ()):
            source = self.row(options, i)
            location = self.string(self.field(source, 0))
            if location:
                resources.append(location.replace("\\", "/"))

        return {
            "name": name, "path": path, "type": kind, "siblingOrder": order,
            "position": pair(7, None), "size": pair(11, None), "scale": pair(8, [1, 1]),
            "rotationSkew": pair(2, [0, 0]), "anchor": pair(9, None),
            "rgb": list(abgr[1:]), "structAlpha": abgr[0], "opacity": scalar(5, "B", 255),
            "zOrder": scalar(3, "i", 0), "visible": bool(scalar(4, "B", 1)),
            "flipX": bool(scalar(12, "B", 0)), "flipY": bool(scalar(13, "B", 0)),
            "resources": resources,
            "children": [self.node(child, path, i) for i, child in enumerate(self.vector(self.field(tree, 1)))],
        }

    def extract(self):
        root = self.fields(self.read("I", 0)[0])
        return {
            "version": self.string(self.field(root, 0)),
            "resourcesInBinary": sorted({item.decode() for item in RESOURCE_REFERENCE.findall(self.data)}),
            "tree": self.node(self.pointer(self.field(root, 3))),
        }


def contact_sheet(source, destination):
    width, height, cols = 320, 145, 4
    rows = (len(CONTACT_ASSETS) + cols - 1) // cols
    sheet = Image.new("RGB", (cols * width, rows * height), (27, 32, 44))
    draw = ImageDraw.Draw(sheet)
    for i, name in enumerate(CONTACT_ASSETS):
        x, y = i % cols * width, i // cols * height
        with Image.open(source / name) as raw:
            picture = raw.convert("RGBA")
            draw.text((x + 7, y + 6), f"{Path(name).name}  {raw.width}x{raw.height}", fill="#E6EBF3")
            scaled = ImageOps.contain(picture, (width - 16, height - 29), Image.Resampling.LANCZOS)
            sheet.paste(scaled, (x + (width - scaled.width) // 2, y + 22 + (height - 29 - scaled.height) // 2), scaled)
    sheet.save(destination, quality=94)


def main():
    repo = Path(__file__).resolve().parents[2]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=Path("D:/sevenepic"))
    parser.add_argument("--output", type=Path, default=repo / ".artifacts/arena-draft/revision")
    args = parser.parse_args()
    source = (args.source / "原始资源").resolve(strict=True)
    output = args.output.resolve()
    if output.is_relative_to(source):
        raise ValueError("Audit output must stay outside the original resource tree.")
    output.mkdir(parents=True, exist_ok=True)
    report = {
        "schemaVersion": 1,
        "coordinateSystem": "Cocos local coordinates, Y upward, geometry authored before runtime adjustments",
        "layouts": [],
    }
    for name in LAYOUTS:
        relative = f"wnd/{name}.csb"
        data = (source / relative).read_bytes()
        report["layouts"].append({"source": relative, "sourceSha256": hashlib.sha256(data).hexdigest(), **Csb(data).extract()})
    (output / "native-bp-layout.json").write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n",
                                                encoding="utf-8", newline="\n")
    contact_sheet(source, output / "native-bp-assets.jpg")
    print(f"Audited {len(LAYOUTS)} original layouts and {len(CONTACT_ASSETS)} native art files.")


if __name__ == "__main__":
    main()
