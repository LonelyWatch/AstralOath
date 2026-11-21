"""Import the original World Arena pick guides, confirmations and final-ban effects."""

import argparse
import hashlib
import json
import re
import sys
from pathlib import Path

from ImportArenaArt import MODULES, REPO, SOURCE_FOLDER, metadata, texture_importer, write

sys.path.insert(0, str(REPO / "Tools/BattleScene"))
from ImportHudEffects import collect, json_bytes


OUTPUT = MODULES / "Arena/Res/Animations/DraftFeedback"
ROOTS = ("effect/ui_pre_banned.cfx", "effect/ui_livepvp_bann_eff.cfx", "effect/ui_livepvp_arrow_blue.cfx",
         "effect/ui_livepvp_arrow_red.cfx") + tuple(
    f"effect/ui_livepvp_slot_{side}{slot}_set.cfx"
    for slot in range(1, 6)
    for side in ("blue", "red")
)
FOLDER = "folderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n"
TEXT_ASSET = "TextScriptImporter:\n  externalObjects: {}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n"


def texture_metadata(target, check):
    importer = texture_importer(target, sprite=False)
    importer = re.sub(r"(?m)^(\s+textureCompression:) \d+$", r"\1 0", importer)
    importer = re.sub(r"(?m)^  userData:.*$", "  userData: AstralOath.ImportOverride", importer)
    metadata(target, check, importer)
    if check:
        contents = target.with_suffix(".png.meta").read_text(encoding="utf-8-sig")
        conditions = (
            "    enableMipMap: 0",
            "    sRGBTexture: 1",
            "  nPOTScale: 0",
            "  alphaIsTransparency: 0",
            "  textureType: 0",
            "  userData: AstralOath.ImportOverride",
        )
        compression = re.findall(r"(?m)^\s+textureCompression: (\d+)$", contents)
        if not set(conditions).issubset(contents.splitlines()) or not compression or set(compression) != {"0"}:
            raise ValueError(f"Unity texture settings would alter original PMA pixels: {target.relative_to(REPO)}")


def fit_pick_bloom(assets, entry):
    # The game composited the broad flash into the whole scene. Unity uses a
    # padded card surface. Keep the 5 card-shaped slots and their timing intact
    # and fit only the ambient flash to the footprint measured in the match.
    asset = entry["asset"]
    document = json.loads(assets[asset])
    glow = next(bone for bone in document["bones"] if bone["name"] == "glow")
    fit_x, fit_y, strength = .43, .46, .44
    glow["scaleX"] *= fit_x
    glow["scaleY"] *= fit_y
    for clip in ("blue_set", "red_set"):
        for key in document["animations"][clip]["slots"]["glow"]["color"]:
            opacity = round(int(key["color"][-2:], 16) * strength)
            key["color"] = key["color"][:-2] + f"{opacity:02X}"
    assets[asset] = json_bytes(document)
    entry["assetSha256"] = hashlib.sha256(assets[asset]).hexdigest()
    entry["presentationFitting"] = {
        "slot": "glow", "scaleXMultiplier": fit_x, "scaleYMultiplier": fit_y,
        "confirmationAlphaMultiplier": strength,
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=Path("D:/sevenepic"))
    parser.add_argument("--check", action="store_true", help="Verify assets and metadata without writing.")
    args = parser.parse_args()

    source = (args.source / SOURCE_FOLDER).resolve(strict=True)
    if OUTPUT.resolve().is_relative_to(source):
        raise ValueError("Draft feedback output must stay outside the original resource tree.")

    payloads = collect(source, ROOTS)
    manifest = json.loads(payloads["source.json"])
    for entry in manifest["files"]:
        if re.fullmatch(r"ui_livepvp_slot_eff_0[1-5]\.spine\.json", entry["asset"]):
            fit_pick_bloom(payloads, entry)
        if "texture" in entry:
            entry["texture"]["premultipliedAlpha"] = True
    payloads["source.json"] = json_bytes(manifest)

    for directory in (OUTPUT.parent, OUTPUT, OUTPUT / "Particles"):
        metadata(directory, args.check, FOLDER)
    for relative, content in payloads.items():
        target = OUTPUT / relative
        write(target, content, args.check)
        if target.suffix == ".png":
            texture_metadata(target, args.check)
        else:
            metadata(target, args.check, TEXT_ASSET)

    action = "Verified" if args.check else "Imported"
    print(f"{action} {len(payloads) - 1} draft feedback assets and their source manifest.")


if __name__ == "__main__":
    main()
