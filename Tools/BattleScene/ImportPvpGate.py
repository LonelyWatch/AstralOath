"""Import the World Arena gate and countdown from their original effect dependency closure."""

import argparse
import json
from pathlib import Path
import sys

from ImportHudEffects import MODULE, RAW_DIRECTORY, TOOLS, collect, json_bytes

sys.path.insert(0, str(TOOLS / "Arena"))
from ImportArenaArt import metadata, texture_importer, write

OUTPUT = MODULE / "Res/Effects/PvpGate"
ROOTS = (
    "effect/pvp_battle_ready.cfx",
    "effect/pvp_battle_igni.cfx",
    "effect/treff_pd_pvp_battle_close.cfx",
    "effect/treff_pd_pvp_battle_open.cfx",
)
SHARED_PARTICLES = ("Particles/pati_01.png", "Particles/ticle_02.png", "Particles/aura_04.png")
TEXT_IMPORTER = "TextScriptImporter:\n  externalObjects: {}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n"
FOLDER_IMPORTER = "folderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n"


def import_gate(source, check):
    payloads = collect(source, roots=ROOTS)
    manifest = json.loads(payloads["source.json"])
    for entry in manifest["files"]:
        relative = entry["asset"]
        if relative not in SHARED_PARTICLES:
            continue
        shared = OUTPUT.parent / "Hud" / relative
        write(shared, payloads.pop(relative), check=True)
        entry["asset"] = "../Hud/" + relative
    for entry in manifest["files"]:
        entry["dependencies"] = [
            "../Hud/" + relative if relative in SHARED_PARTICLES else relative
            for relative in entry["dependencies"]
        ]
    payloads["source.json"] = json_bytes(manifest)
    for folder in (OUTPUT, OUTPUT / "Particles"):
        if not check:
            folder.mkdir(parents=True, exist_ok=True)
        metadata(folder, check, FOLDER_IMPORTER)
    for relative, payload in payloads.items():
        path = OUTPUT / relative
        write(path, payload, check)
        importer = texture_importer(path, sprite=False) if path.suffix == ".png" else TEXT_IMPORTER
        metadata(path, check, importer)
    action = "Verified" if check else "Imported"
    print(f"{action} {len(payloads) - 1} gate assets, {len(SHARED_PARTICLES)} shared particle textures and the source manifest.")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=Path("D:/sevenepic"))
    parser.add_argument("--check", action="store_true", help="Verify generated assets without changing them.")
    args = parser.parse_args()
    source = (args.source / RAW_DIRECTORY).resolve(strict=True)
    if OUTPUT.resolve().is_relative_to(source):
        raise ValueError("Gate assets must stay outside the read-only source directory.")
    import_gate(source, args.check)


if __name__ == "__main__":
    main()
