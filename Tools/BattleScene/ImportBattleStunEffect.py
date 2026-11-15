"""Import or verify the native looping Stun star effect."""

import argparse
from hashlib import sha256
from io import BytesIO
from pathlib import Path
import plistlib
import re

from ImportBattleShieldAura import REPO, decode_scsp, decode_sct, guid, json_bytes

STEM = "stse_stun"
OUTPUT = REPO / "Assets/Game/Modules/BattleScene/Res/Effects/Stun"
TEXTURE_META = OUTPUT.parent / "Shield/stse_protect.png.meta"
SOURCE_TABLE = "db/cs_player.db"
SOURCE_TABLE_SHA256 = "c031c39f2a96af49d1b4240be6c60c37091289e5794300f7b6229369b06b026f"
SOURCES = {
    ".cfx": STEM + ".cfx.json",
    ".scsp": STEM + ".json",
    ".atlas": STEM + ".atlas.txt",
    ".sct": STEM + ".png",
}


def make_payloads(source):
    if sha256((source / SOURCE_TABLE).read_bytes()).hexdigest() != SOURCE_TABLE_SHA256:
        raise ValueError("Review cs_player rows 205 and 205k for the new release before importing Stun.")
    raw = {
        extension: (source / "effect" / (STEM + extension)).read_bytes()
        for extension in SOURCES
    }
    effect = plistlib.loads(raw[".cfx"])
    if effect.get("primitive") != [{"source": STEM, "scale": "1.7", "format": "spine", "loop": 1}]:
        raise ValueError("Review the changed native Stun CFX primitive.")

    skeleton, spine = decode_scsp(raw[".scsp"])
    slots = [
        (slot["name"], slot["attachment"], slot.get("additive", False))
        for slot in skeleton["slots"]
    ]
    expected_slots = [(f"star{i}", "stun_star", False) for i in range(1, 6)] + [("glow", "flash", True)]
    if spine["spineVersion"] != "2.1.27" or tuple(skeleton["animations"]) != ("animation",) or slots != expected_slots:
        raise ValueError("Review the changed native Stun skeleton.")

    atlas = raw[".atlas"].decode("utf-8-sig").replace("\r\n", "\n")
    regions = [
        line for line in atlas.splitlines()
        if line and not line[0].isspace() and ":" not in line
    ]
    image, texture = decode_sct(raw[".sct"])
    if regions != [STEM + ".sct", "flash", "stun_star"] or image.size != (173, 271):
        raise ValueError("Review the changed native Stun atlas or texture.")
    partial_count = 0
    for red, green, blue, alpha in image.getdata():
        if (alpha == 0 and max(red, green, blue) > 0) or (0 < alpha < 255 and max(red, green, blue) > alpha + 7):
            raise ValueError("Native Stun atlas no longer matches premultiplied alpha.")
        if 0 < alpha < 255:
            partial_count += 1
    if not partial_count:
        raise ValueError("Expected translucent pixels in the native Stun texture.")

    png = BytesIO()
    image.save(png, format="PNG")
    payloads = {
        SOURCES[".cfx"]: json_bytes(effect),
        SOURCES[".scsp"]: json_bytes(skeleton),
        SOURCES[".atlas"]: atlas.encode("utf-8"),
        SOURCES[".sct"]: png.getvalue(),
    }
    payloads["source.json"] = json_bytes({
        "schemaVersion": 1,
        "source": "原始资源/effect/" + STEM,
        "sourceMapping": {
            "table": "原始资源/" + SOURCE_TABLE,
            "tableSha256": SOURCE_TABLE_SHA256,
            "records": {"205": "stic_stun", "205k": "stic_stun_keep"},
            "cs_effect": STEM + ".cfx",
            "cs_eff_bone": "top",
        },
        "premultipliedAlpha": True,
        "partialAlphaPixels": partial_count,
        "spineVersion": spine["spineVersion"],
        "animations": spine["animations"],
        "texture": texture,
        "converterSha256": sha256(
            (REPO / "Tools/CharacterAssets/convert.py").read_bytes().replace(b"\r\n", b"\n")
        ).hexdigest(),
        "files": [{
            "source": "原始资源/effect/" + STEM + extension,
            "sourceSha256": sha256(raw[extension]).hexdigest(),
            "asset": name,
            "assetSha256": sha256(payloads[name]).hexdigest(),
        } for extension, name in SOURCES.items()],
    })
    return payloads


def meta_content(path):
    header = "fileFormatVersion: 2\nguid: " + guid(path) + "\n"
    if path == OUTPUT:
        return header + "folderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n"
    if path.suffix == ".png":
        template = TEXTURE_META.read_text(encoding="utf-8-sig")
        return re.sub(r"guid: [a-f0-9]+", "guid: " + guid(path), template, count=1)
    return header + "TextScriptImporter:\n  externalObjects: {}\n"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=Path("D:/sevenepic"))
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    source = (args.source / "原始资源").resolve(strict=True)
    if OUTPUT.resolve().is_relative_to(source):
        raise ValueError("Read-only sources cannot be overwritten.")
    assets = {OUTPUT / name: data for name, data in make_payloads(source).items()}
    metadata = {Path(str(path) + ".meta"): meta_content(path) for path in [OUTPUT, *assets]}
    if args.check:
        changed = [path.name for path, data in assets.items() if not path.is_file() or path.read_bytes() != data]
        changed.extend(path.name for path, template in metadata.items()
                       if not path.is_file() or path.read_text(encoding="utf-8-sig") != template)
        if changed:
            parser.exit(1, "Missing or changed Stun assets: " + ", ".join(changed) + "\n")
    else:
        OUTPUT.mkdir(parents=True, exist_ok=True)
        for path, data in assets.items():
            if not path.is_file() or path.read_bytes() != data:
                path.write_bytes(data)
        for path, template in metadata.items():
            if not path.exists():
                path.write_text(template, encoding="utf-8")
    print(("Verified" if args.check else "Imported") + " the native Stun dependency closure.")


if __name__ == "__main__":
    main()
