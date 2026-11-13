"""Import or verify the native persistent Invincible effect."""

import argparse
from hashlib import sha256
from io import BytesIO
from pathlib import Path
import plistlib
import re

from ImportBattleShieldAura import REPO, guid, json_bytes, decode_scsp, decode_sct

STEM = "stse_invincible"
OUTPUT = REPO / "Assets/Game/Modules/BattleScene/Res/Effects/Invincible"
TEXTURE_META = OUTPUT.parent / "Shield/stse_protect.png.meta"
SOURCES = {
    ".cfx": STEM + ".cfx.json",
    ".scsp": STEM + ".json",
    ".atlas": STEM + ".atlas.txt",
    ".sct": STEM + ".png",
}


def make_payloads(raw):
    effect = plistlib.loads(raw[".cfx"])
    if effect.get("primitive") != [{
        "source": STEM, "y": "20", "z": "1", "opacity": "1.0",
        "scale": "1.5", "format": "spine", "loop": True,
    }]:
        raise ValueError("Review the changed native Invincible CFX primitive.")
    document, spine = decode_scsp(raw[".scsp"])
    slots = [slot["name"] for slot in document["slots"]]
    if slots != ["shield", "edge", "shield_f", "g1", "g2", "glow", "shine1", "shine2"] or tuple(document["animations"]) != ("animation",):
        raise ValueError("Review the changed native Invincible skeleton.")
    atlas = raw[".atlas"].decode("utf-8-sig")
    image, texture = decode_sct(raw[".sct"])
    if atlas.lstrip().splitlines()[0] != STEM + ".sct" or image.size != (603, 506):
        raise ValueError("Review the changed native Invincible atlas or texture.")
    partial_count = 0
    for r, g, b, alpha in image.getdata():
        if not alpha and (r or g or b) or 0 < alpha < 255 and max(r, g, b) > alpha + 7:
            raise ValueError("Native Invincible texture no longer matches premultiplied alpha.")
        if 0 < alpha < 255:
            partial_count += 1
    if not partial_count:
        raise ValueError("Expected translucent pixels in the native Invincible texture.")

    png = BytesIO()
    image.save(png, format="PNG")
    payloads = {
        SOURCES[".cfx"]: json_bytes(effect),
        SOURCES[".scsp"]: json_bytes(document),
        SOURCES[".atlas"]: atlas.replace("\r\n", "\n").encode("utf-8"),
        SOURCES[".sct"]: png.getvalue(),
    }
    payloads["source.json"] = json_bytes({
        "schemaVersion": 1,
        "source": "原始资源/effect/" + STEM,
        "sourceMapping": {
            "cs_icon": "stic_invincible",
            "cs_effect": STEM + ".cfx",
            "cs_eff_bone": None,
            "anchorEvidence": "CFX local y=20. Native status-table bone has not been independently verified.",
        },
        "premultipliedAlpha": True,
        "partialAlphaPixels": partial_count,
        "spineVersion": spine["spineVersion"],
        "animations": spine["animations"],
        "slots": slots,
        "texture": texture,
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
    root = (args.source / "原始资源").resolve(strict=True)
    if OUTPUT.resolve().is_relative_to(root):
        raise ValueError("Read-only sources cannot be overwritten.")
    raw = {extension: (root / "effect" / (STEM + extension)).read_bytes() for extension in SOURCES}
    payloads = make_payloads(raw)
    assets = {OUTPUT / name: data for name, data in payloads.items()}
    metadata = {Path(str(path) + ".meta"): meta_content(path) for path in [OUTPUT, *assets]}
    if args.check:
        changed = [path.name for path, data in assets.items() if not path.is_file() or path.read_bytes() != data]
        changed.extend(path.name for path, template in metadata.items()
                       if not path.is_file() or path.read_text(encoding="utf-8-sig") != template)
        if changed:
            parser.exit(1, "Missing or changed Invincible assets: " + ", ".join(changed) + "\n")
    else:
        OUTPUT.mkdir(parents=True, exist_ok=True)
        for path, data in assets.items():
            if not path.is_file() or path.read_bytes() != data:
                path.write_bytes(data)
        for path, template in metadata.items():
            if not path.exists():
                path.write_text(template, encoding="utf-8")
    print(("Verified" if args.check else "Imported") + " the native Invincible aura dependency closure.")


if __name__ == "__main__":
    main()
