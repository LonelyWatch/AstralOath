"""Import or verify authored Stun, Sleep, and Frostbite status effects."""

import argparse
from hashlib import sha256
from io import BytesIO
from pathlib import Path
import plistlib
import re

from ImportBattleShieldAura import REPO, decode_scsp, decode_sct, guid, json_bytes

EFFECTS = REPO / "Assets/Game/Modules/BattleScene/Res/Effects"
TEXTURE_META = EFFECTS / "Shield/stse_protect.png.meta"
SOURCE_TABLE = "db/cs_player.db"
SOURCE_TABLE_SHA256 = "c031c39f2a96af49d1b4240be6c60c37091289e5794300f7b6229369b06b026f"


def make_payloads(source, status):
    stem = "stse_" + status
    sources = {".cfx": stem + ".cfx.json", ".scsp": stem + ".json",
               ".atlas": stem + ".atlas.txt", ".sct": stem + ".png"}
    sleep = status == "sleep"
    records = ({"206": "stic_sleep", "206k": "stic_sleep_keep"} if sleep
               else {"205": "stic_stun", "205k": "stic_stun_keep"})
    if sha256((source / SOURCE_TABLE).read_bytes()).hexdigest() != SOURCE_TABLE_SHA256:
        raise ValueError(f"Review cs_player rows {list(records)} for the new release before importing {status}.")
    raw = {
        extension: (source / "effect" / (stem + extension)).read_bytes()
        for extension in sources
    }
    effect = plistlib.loads(raw[".cfx"])
    expected = {"source": stem, "scale": "1.7", "format": "spine", "loop": 1}
    if sleep:
        expected.update(y="-50", z="1", scale="0.4")
    if effect.get("primitive") != [expected]:
        raise ValueError("Review the changed native status CFX primitive.")

    skeleton, spine = decode_scsp(raw[".scsp"])
    slots = [
        (slot["name"], slot["attachment"], slot.get("additive", False))
        for slot in skeleton["slots"]
    ]
    expected_slots = [(f"star{i}", "stun_star", False) for i in range(1, 6)] + [("glow", "flash", True)]
    if sleep:
        expected_slots = [(f"z{i}", "z", False) for i in range(1, 6)]
    if spine["spineVersion"] != "2.1.27" or tuple(skeleton["animations"]) != ("animation",) or slots != expected_slots:
        raise ValueError("Review the changed native status skeleton.")

    atlas = raw[".atlas"].decode("utf-8-sig").replace("\r\n", "\n")
    regions = [
        line for line in atlas.splitlines()
        if line and not line[0].isspace() and ":" not in line
    ]
    image, texture = decode_sct(raw[".sct"])
    expected_regions = [stem + ".sct", *(["z"] if sleep else ["flash", "stun_star"])]
    expected_size = (90, 85) if sleep else (173, 271)
    if regions != expected_regions or image.size != expected_size:
        raise ValueError("Review the changed native status atlas or texture.")
    partial_count = 0
    for red, green, blue, alpha in image.getdata():
        if (alpha == 0 and max(red, green, blue) > 0) or (0 < alpha < 255 and max(red, green, blue) > alpha + 7):
            raise ValueError("Native status atlas no longer matches premultiplied alpha.")
        if 0 < alpha < 255:
            partial_count += 1
    if not partial_count:
        raise ValueError("Expected translucent pixels in the native status texture.")

    png = BytesIO()
    image.save(png, format="PNG")
    payloads = {
        sources[".cfx"]: json_bytes(effect),
        sources[".scsp"]: json_bytes(skeleton),
        sources[".atlas"]: atlas.encode("utf-8"),
        sources[".sct"]: png.getvalue(),
    }
    payloads["source.json"] = json_bytes({
        "schemaVersion": 1,
        "source": "原始资源/effect/" + stem,
        "sourceMapping": {
            "table": "原始资源/" + SOURCE_TABLE,
            "tableSha256": SOURCE_TABLE_SHA256,
            "records": records,
            "cs_effect": stem + ".cfx",
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
            "source": "原始资源/effect/" + stem + extension,
            "sourceSha256": sha256(raw[extension]).hexdigest(),
            "asset": name,
            "assetSha256": sha256(payloads[name]).hexdigest(),
        } for extension, name in sources.items()],
    })
    return payloads


def make_frostbite_payloads(source):
    if sha256((source / SOURCE_TABLE).read_bytes()).hexdigest() != SOURCE_TABLE_SHA256:
        raise ValueError("Review cs_player row 239 before importing Frostbite.")
    names = {
        "deb_freezing.cfx": "deb_freezing.cfx.json",
        "freezing.scsp": "freezing.json",
        "freezing.atlas": "freezing.atlas.txt",
        "freezing.sct": "freezing.png",
    }
    raw = {name: (source / "effect" / name).read_bytes() for name in names}
    cfx = plistlib.loads(raw["deb_freezing.cfx"])
    if cfx["primitive"] != [{"format": "spine", "source": "freezing", "x": "-10", "y": "30", "z": "30"}]:
        raise ValueError("Review the Frostbite application layer and its anchor.")
    skeleton, report = decode_scsp(raw["freezing.scsp"])
    atlas = raw["freezing.atlas"].decode("utf-8-sig").replace("\r\n", "\n")
    image, texture = decode_sct(raw["freezing.sct"])
    if (report["spineVersion"] != "2.1.27" or tuple(skeleton["animations"]) != ("animation",)
            or atlas.lstrip().splitlines()[0] != "freezing.sct" or image.size != (511, 663)):
        raise ValueError("Review the original Frostbite skeleton, atlas and texture.")
    if any(max(red, green, blue) > alpha + 7 for red, green, blue, alpha in image.getdata()):
        raise ValueError("Review the Frostbite texture's premultiplied alpha.")
    png = BytesIO()
    image.save(png, format="PNG")
    payloads = {names["deb_freezing.cfx"]: json_bytes(cfx), names["freezing.scsp"]: json_bytes(skeleton),
                names["freezing.atlas"]: atlas.encode("utf-8"), names["freezing.sct"]: png.getvalue()}
    payloads["source.json"] = json_bytes({
        "schemaVersion": 1, "sourceMapping": {"table": SOURCE_TABLE, "tableSha256": SOURCE_TABLE_SHA256,
            "records": {"239": "stic_freezing"}, "cs_effect": "deb_freezing.cfx", "cs_eff_bone": "target"},
        "spineVersion": report["spineVersion"], "animations": report["animations"],
        "premultipliedAlpha": True, "texture": texture,
        "converterSha256": sha256((REPO / "Tools/CharacterAssets/convert.py").read_bytes().replace(b"\r\n", b"\n")).hexdigest(),
        "assets": [{"source": "effect/" + name, "sourceSha256": sha256(raw[name]).hexdigest(),
                    "asset": output, "assetSha256": sha256(payloads[output]).hexdigest()} for name, output in names.items()],
    })
    return payloads


def meta_content(path, output):
    header = "fileFormatVersion: 2\nguid: " + guid(path) + "\n"
    if path == output:
        return header + "folderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n"
    if path.suffix == ".png":
        template = TEXTURE_META.read_text(encoding="utf-8-sig")
        return re.sub(r"guid: [a-f0-9]+", "guid: " + guid(path), template, count=1)
    return header + "TextScriptImporter:\n  externalObjects: {}\n"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--effect", choices=("stun", "sleep", "frostbite"), default="stun")
    parser.add_argument("--source", type=Path, default=Path("D:/sevenepic"))
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    source = (args.source / "原始资源").resolve(strict=True)
    output = EFFECTS / args.effect.capitalize()
    if output.resolve().is_relative_to(source):
        raise ValueError("Read-only sources cannot be overwritten.")
    payloads = make_frostbite_payloads(source) if args.effect == "frostbite" else make_payloads(source, args.effect)
    assets = {output / name: data for name, data in payloads.items()}
    metadata = {Path(str(path) + ".meta"): meta_content(path, output) for path in [output, *assets]}
    if args.check:
        changed = [path.name for path, data in assets.items() if not path.is_file() or path.read_bytes() != data]
        changed.extend(path.name for path, template in metadata.items()
                       if not path.is_file() or path.read_text(encoding="utf-8-sig") != template)
        if changed:
            parser.exit(1, f"Missing or changed {args.effect} assets: " + ", ".join(changed) + "\n")
    else:
        output.mkdir(parents=True, exist_ok=True)
        for path, data in assets.items():
            if not path.is_file() or path.read_bytes() != data:
                path.write_bytes(data)
        for path, template in metadata.items():
            if not path.exists():
                path.write_text(template, encoding="utf-8")
    print(("Verified" if args.check else "Imported") + f" the native {args.effect} dependency closure.")


if __name__ == "__main__":
    main()
