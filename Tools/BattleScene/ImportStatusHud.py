"""Import the original status glyphs and their turn digits without changing the source."""
import argparse
import hashlib
import io
import json
from pathlib import Path
import re
import uuid

from PIL import Image


REPO = Path(__file__).resolve().parents[2]
HUD = REPO / "Assets/Game/Modules/BattleScene/Res/Textures/Hud"
OUTPUT = HUD / "Statuses"
# BattleStatusKind, presentation group, source glyph.
STATUSES = (
    ("AttackUp", "Buff", "stic_att_up"),
    ("DefenseUp", "Buff", "stic_def_up"),
    ("Immunity", "Buff", "stic_debuf_impossible"),
    ("Barrier", "Buff", "stic_protect"),
    ("Stun", "Debuff", "stic_stun"),
    ("Counter", "Buff", "stic_counter"),
    ("SpeedUp", "Buff", "stic_speed_up"),
    ("SpeedDown", "Debuff", "stic_speed_dn"),
    ("AttackDown", "Debuff", "stic_att_dn"),
    ("DefenseDown", "Debuff", "stic_def_dn"),
    ("Unhealable", "Debuff", "stic_heal_impossible"),
    ("Silence", "Debuff", "stic_silence"),
    ("EvasionUp", "Buff", "stic_dodge_up"),
    ("CriticalResistanceUp", "Buff", "stic_crires_up"),
    ("Stealth", "Buff", "stic_hide"),
)


def guid(path):
    return uuid.uuid5(uuid.NAMESPACE_URL, "astraloath:" + path.relative_to(REPO).as_posix()).hex


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=Path("D:/sevenepic"))
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    source = (args.source / "原始资源").resolve(strict=True)
    if OUTPUT.resolve().is_relative_to(source):
        raise ValueError("The destination cannot be inside the read-only source.")
    template = (HUD / "game_hud_bar_split.png.meta").read_text(encoding="utf-8-sig")
    template = "\n".join(line.rstrip() for line in template.splitlines()) + "\n"
    sources = {f"buff/{glyph}.png": (28, 28) for _, _, glyph in STATUSES}
    sources.update({f"img/itxt_num{digit}.png": (12, 16) for digit in range(10)})
    rows = []
    writes = {}
    for original, dimensions in sources.items():
        raw = (source / original).resolve(strict=True)
        if not raw.is_relative_to(source):
            raise ValueError(f"Source escaped its root: {original}")
        data = raw.read_bytes()
        with Image.open(io.BytesIO(data)) as img:
            if img.format != "PNG" or img.size != dimensions or not img.convert("RGBA").getbbox():
                raise ValueError(f"Unexpected source image: {original}, {img.format}, {img.size}")
        asset = OUTPUT / raw.name
        rows.append({"source": "原始资源/" + original, "asset": asset.name, "sha256": hashlib.sha256(data).hexdigest()})
        writes[asset] = data

    manifest = {"schemaVersion": 1, "layoutEvidence": ["wnd/hp_bar.csb", "wnd/hp_bar_r.csb"],
                "statuses": [{"kind": kind, "group": group, "asset": glyph + ".png"} for kind, group, glyph in STATUSES],
                "files": rows}
    document = OUTPUT / "source.json"
    writes[document] = (json.dumps(manifest, ensure_ascii=False, indent=2) + "\n").encode("utf-8")
    metadata = {OUTPUT.with_suffix(".meta"): f"fileFormatVersion: 2\nguid: {guid(OUTPUT)}\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {{}}\n",
                document.with_suffix(".json.meta"): f"fileFormatVersion: 2\nguid: {guid(document)}\nTextScriptImporter:\n  externalObjects: {{}}\n"}
    for asset in writes:
        if asset.suffix == ".png":
            metadata[asset.with_suffix(".png.meta")] = re.sub(r"(?m)^guid: [a-f0-9]+$", "guid: " + guid(asset), template, count=1)
    if args.check:
        mismatches = [str(path) for path, content in writes.items() if not path.is_file() or path.read_bytes() != content]
        mismatches += [str(path) for path in metadata if not path.is_file()]
        if mismatches:
            parser.exit(1, "Missing or changed status assets: " + ", ".join(mismatches) + "\n")
    else:
        OUTPUT.mkdir(parents=True, exist_ok=True)
        for path, content in writes.items():
            if not path.is_file() or path.read_bytes() != content:
                path.write_bytes(content)
        for path, content in metadata.items():
            if not path.exists():
                path.write_text(content, encoding="utf-8", newline="\n")
    print(f"{'Verified' if args.check else 'Imported'} {len(rows)} original status HUD PNGs.")


if __name__ == "__main__":
    main()
