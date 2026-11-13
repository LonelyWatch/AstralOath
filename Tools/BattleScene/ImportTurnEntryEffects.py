"""Import the original dust skeleton for battle entry, reusing its already imported atlas and page."""
import argparse
from hashlib import sha256
import json
from pathlib import Path
import sys
import uuid

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / "Tools/CharacterAssets"))
from convert import decode_scsp

OUTPUT = REPO / "Assets/Game/Modules/BattleScene/Res/Effects/TurnEntry"
SHARED = "Assets/Game/Modules/CharacterPresentation/Res/Characters/c1003"
ATLAS = SHARED + "/Atlas/effect/effect.atlas.txt"
PAGE = SHARED + "/Textures/effect/effect.png"

def digest(data):
    return sha256(data).hexdigest()

def json_bytes(value, **options):
    return json.dumps(value, ensure_ascii=False, allow_nan=False, **options).encode("utf-8")

def write_asset(path, data, check):
    if check:
        if not path.is_file() or path.read_bytes() != data:
            raise ValueError(f"Missing or stale turn-entry asset: {path.relative_to(REPO)}")
        return
    path.write_bytes(data)
    meta = path.with_name(path.name + ".meta")
    if not meta.exists():
        write_meta(path, "TextScriptImporter:\n  externalObjects: {}\n")

def write_meta(path, body):
    guid = uuid.uuid5(uuid.NAMESPACE_URL, "astraloath:" + path.relative_to(REPO).as_posix()).hex
    path.with_name(path.name + ".meta").write_text(f"fileFormatVersion: 2\nguid: {guid}\n{body}", encoding="utf-8")

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=Path("D:/sevenepic"))
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    source = (args.source / "原始资源").resolve(strict=True)
    if OUTPUT.resolve().is_relative_to(source):
        raise ValueError("Read-only sources cannot be overwritten")
    effect = source / "effect"
    raw = (effect / "dust.scsp").read_bytes()
    alias = (effect / "dust.atlas").read_bytes()
    atlas_name = alias.decode("utf-8").strip()
    if atlas_name != "effect.atlas":
        raise ValueError(f"Unexpected turn-entry atlas alias: {atlas_name}")
    original_atlas = (effect / atlas_name).read_bytes()
    if "effect.sct" not in original_atlas.decode("utf-8").splitlines():
        raise ValueError("Turn-entry atlas no longer names effect.sct")
    imported_atlas = (REPO / ATLAS).read_bytes()
    if imported_atlas.decode("utf-8").splitlines() != original_atlas.decode("utf-8").splitlines():
        raise ValueError("Shared imported atlas no longer matches the original turn-entry atlas")
    document, _ = decode_scsp(raw)
    if "animation" not in document["animations"]:
        raise ValueError("Expected original dust animation is missing")
    skeleton = json_bytes(document, separators=(",", ":"))
    manifest = {
        "schemaVersion": 1,
        "skeleton": {"source": "原始资源/effect/dust.scsp", "sourceSha256": digest(raw),
                     "asset": "dust.spine.json", "assetSha256": digest(skeleton)},
        "atlasAlias": {"source": "原始资源/effect/dust.atlas", "sourceSha256": digest(alias)},
        "atlas": {"source": "原始资源/effect/" + atlas_name, "sourceSha256": digest(original_atlas),
                  "asset": ATLAS, "assetSha256": digest(imported_atlas)},
        "page": {"source": "原始资源/effect/effect.sct", "sourceSha256": digest((effect / "effect.sct").read_bytes()),
                 "asset": PAGE, "assetSha256": digest((REPO / PAGE).read_bytes())},
        "converterSha256": digest((REPO / "Tools/CharacterAssets/convert.py").read_bytes()),
    }
    if not args.check:
        OUTPUT.mkdir(parents=True, exist_ok=True)
        if not OUTPUT.with_name(OUTPUT.name + ".meta").exists():
            write_meta(OUTPUT, "folderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n")
    write_asset(OUTPUT / "dust.spine.json", skeleton, args.check)
    write_asset(OUTPUT / "source.json", json_bytes(manifest, indent=2) + b"\n", args.check)
    print("Verified turn-entry dust assets." if args.check else "Imported turn-entry dust assets.")

if __name__ == "__main__":
    main()