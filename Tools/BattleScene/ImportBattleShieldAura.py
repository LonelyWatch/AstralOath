"""Import the complete original persistent barrier effect into BattleScene."""

import argparse
from hashlib import sha256
from io import BytesIO
import json
from pathlib import Path
import plistlib
import re
import sys
import uuid

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / "Tools/CharacterAssets"))
from convert import decode_scsp, decode_sct

OUTPUT = REPO / "Assets/Game/Modules/BattleScene/Res/Effects/Shield"
STEM = "stse_protect"
TEXTURE_META = REPO / "Assets/Game/Modules/BattleScene/Res/Effects/Hud/Particles/ticle_02.png.meta"


def json_bytes(value):
    return (json.dumps(value, ensure_ascii=False, indent=2, allow_nan=False) + "\n").encode("utf-8")


def guid(path):
    return uuid.uuid5(uuid.NAMESPACE_URL, "astraloath:" + path.relative_to(REPO).as_posix()).hex


def make_payloads(raw):
    cfx = plistlib.loads(raw[".cfx"])
    primitives = cfx["primitive"]
    if len(primitives) != 1 or primitives[0] != {
        "source": STEM, "y": "20", "z": "1", "scale": "1.5", "format": "spine", "loop": True,
    }:
        raise ValueError("Review the changed source CFX before updating the shield presentation.")

    document, skeleton_report = decode_scsp(raw[".scsp"])
    slots = [slot["name"] for slot in document["slots"]]
    if slots != ["shield", "edge", "shield_f", "g1", "g2", "glow"] or tuple(document["animations"]) != ("animation",):
        raise ValueError("Review the changed native shield layers or animation before importing.")
    atlas = raw[".atlas"].decode("utf-8-sig")
    image, texture_report = decode_sct(raw[".sct"])
    if atlas.lstrip().splitlines()[0] != STEM + ".sct" or "\nsiheld_edge\n" not in atlas.replace("\r\n", "\n"):
        raise ValueError("Shield source atlas or edge region changed.")
    if image.size != (466, 446):
        raise ValueError("Shield source texture dimensions changed.")
    png = BytesIO()
    image.save(png, format="PNG")

    payloads = {
        STEM + ".cfx.json": json_bytes(cfx),
        STEM + ".json": json_bytes(document),
        STEM + ".atlas.txt": atlas.replace("\r\n", "\n").encode("utf-8"),
        STEM + ".png": png.getvalue(),
    }
    mapping = {
        ".cfx": STEM + ".cfx.json",
        ".scsp": STEM + ".json",
        ".atlas": STEM + ".atlas.txt",
        ".sct": STEM + ".png",
    }
    manifest = {
        "schemaVersion": 2,
        "source": "原始资源/effect/" + STEM,
        "sourceMapping": {
            "table": "原始资源/db/cs_player.db",
            "tableSha256": "c031c39f2a96af49d1b4240be6c60c37091289e5794300f7b6229369b06b026f",
            "record": "shield_caster_maxhp",
            "cs_icon": "stic_protect",
            "cs_effect": "stse_protect.cfx",
            "cs_eff_bone": "shadow",
        },
        "transfer": "Decode all original Spine slots, animations, blends and colors without stripping or recoloring.",
        "premultipliedAlpha": True,
        "spineVersion": skeleton_report["spineVersion"],
        "animations": skeleton_report["animations"],
        "texture": texture_report,
        "converterSha256": sha256((REPO / "Tools/CharacterAssets/convert.py").read_bytes()).hexdigest(),
        "files": [
            {
                "source": "原始资源/effect/" + STEM + extension,
                "sourceSha256": sha256(raw[extension]).hexdigest(),
                "asset": name,
                "assetSha256": sha256(payloads[name]).hexdigest(),
            }
            for extension, name in mapping.items()
        ],
    }
    payloads["source.json"] = json_bytes(manifest)
    return payloads


def write_meta(path, template):
    meta = Path(str(path) + ".meta")
    if meta.exists():
        return
    header = "fileFormatVersion: 2\nguid: " + guid(path) + "\n"
    if path == OUTPUT:
        content = header + "folderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n"
    elif path.suffix == ".png":
        content = re.sub(r"guid: [a-f0-9]+", "guid: " + guid(path), template, count=1)
    else:
        content = header + "TextScriptImporter:\n  externalObjects: {}\n"
    meta.write_text(content, encoding="utf-8")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=Path("D:/sevenepic"))
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    source = (args.source / "原始资源").resolve(strict=True)
    if OUTPUT.resolve().is_relative_to(source):
        raise ValueError("Read-only sources cannot be overwritten.")
    raw = {
        extension: (source / "effect" / (STEM + extension)).read_bytes()
        for extension in (".cfx", ".scsp", ".atlas", ".sct")
    }
    payloads = make_payloads(raw)
    if args.check:
        changed = [
            name for name, content in payloads.items()
            if not (OUTPUT / name).exists() or (OUTPUT / name).read_bytes() != content
        ]
        if changed:
            parser.exit(1, "Missing or changed shield assets: " + ", ".join(changed) + "\n")
    else:
        OUTPUT.mkdir(parents=True, exist_ok=True)
        template = "\n".join(line.rstrip() for line in TEXTURE_META.read_text(encoding="utf-8-sig").splitlines()) + "\n"
        for name, content in payloads.items():
            path = OUTPUT / name
            if not path.exists() or path.read_bytes() != content:
                path.write_bytes(content)
            write_meta(path, template)
        write_meta(OUTPUT, template)
    print(("Verified" if args.check else "Imported") + " the shield aura dependency closure.")


if __name__ == "__main__":
    main()
