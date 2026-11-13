"""Prepare the original status glyphs and turn digits for the battle HUD."""
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


def srgb_to_linear(value):
    return value / 12.92 if value <= 0.04045 else ((value + 0.055) / 1.055) ** 2.4


def linear_to_srgb(value):
    return value * 12.92 if value <= 0.0031308 else 1.055 * value ** (1 / 2.4) - 0.055


def adapt_icon(image):
    # Preserve rounded-corner coverage and match display-space transmittance over white.
    # Compensate straight RGB so the glyph retains its source contribution over black.
    result = Image.new("RGBA", image.size)
    pixels = []
    for red, green, blue, alpha in image.convert("RGBA").getdata():
        if alpha == 0 or alpha == 255:
            pixels.append((red, green, blue, alpha))
            continue
        original_alpha = alpha / 255
        target_alpha = round((1 - srgb_to_linear(1 - original_alpha)) * 255)
        channels = tuple(round(linear_to_srgb(srgb_to_linear(channel / 255 * original_alpha) /
                                              (target_alpha / 255)) * 255)
                         for channel in (red, green, blue))
        pixels.append((*channels, target_alpha))
    result.putdata(pixels)
    stream = io.BytesIO()
    result.save(stream, format="PNG")
    return stream.getvalue()


ICON_PLATFORMS = re.compile(
    r"(buildTarget: (?:DefaultTexturePlatform|Standalone|Android)\n"
    r"(?:    [^\n]*\n)*?    textureCompression: )([0-9]+)"
)


def has_uncompressed_icons(meta):
    settings = ICON_PLATFORMS.findall(meta)
    return len(settings) == 3 and all(compression == "0" for _, compression in settings)


def uncompressed_icon(meta):
    updated = ICON_PLATFORMS.sub(r"\g<1>0", meta)
    if not has_uncompressed_icons(updated):
        raise ValueError("Missing required status icon texture importer settings.")
    return "\n".join(line.rstrip() for line in updated.splitlines()) + "\n"


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
        source_bytes = raw.read_bytes()
        is_icon = original.startswith("buff/")
        with Image.open(io.BytesIO(source_bytes)) as img:
            if img.format != "PNG" or img.size != dimensions or not img.convert("RGBA").getbbox():
                raise ValueError(f"Unexpected source image: {original}, {img.format}, {img.size}")
            data = adapt_icon(img) if is_icon else source_bytes
        asset = OUTPUT / raw.name
        rows.append({"source": "原始资源/" + original, "asset": asset.name,
                     "sourceSha256": hashlib.sha256(source_bytes).hexdigest(), "assetSha256": hashlib.sha256(data).hexdigest()})
        writes[asset] = data

    manifest = {"schemaVersion": 2, "layoutEvidence": ["wnd/hp_bar.csb", "wnd/hp_bar_r.csb"],
                "iconTransfer": "Compensate original display-space transmittance for Unity Linear; preserve soft corners.",
                "digitTransfer": "Byte-identical original PNG.",
                "statuses": [{"kind": kind, "group": group, "asset": glyph + ".png"} for kind, group, glyph in STATUSES],
                "files": rows}
    document = OUTPUT / "source.json"
    writes[document] = (json.dumps(manifest, ensure_ascii=False, indent=2) + "\n").encode("utf-8")
    metadata = {OUTPUT.with_suffix(".meta"): f"fileFormatVersion: 2\nguid: {guid(OUTPUT)}\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {{}}\n",
                document.with_suffix(".json.meta"): f"fileFormatVersion: 2\nguid: {guid(document)}\nTextScriptImporter:\n  externalObjects: {{}}\n"}
    for asset in writes:
        if asset.suffix == ".png":
            meta = asset.with_suffix(".png.meta")
            metadata[meta] = re.sub(r"(?m)^guid: [a-f0-9]+$", "guid: " + guid(asset), template, count=1)
    if args.check:
        mismatches = [str(path) for path, content in writes.items() if not path.is_file() or path.read_bytes() != content]
        mismatches += [str(path) for path in metadata if not path.is_file()]
        for path in metadata:
            if path.name.startswith("stic_") and path.is_file():
                if not has_uncompressed_icons(path.read_text(encoding="utf-8-sig")):
                    mismatches.append(str(path))
        if mismatches:
            parser.exit(1, "Missing or changed status assets: " + ", ".join(mismatches) + "\n")
    else:
        OUTPUT.mkdir(parents=True, exist_ok=True)
        for path, content in writes.items():
            if not path.is_file() or path.read_bytes() != content:
                path.write_bytes(content)
        for path, content in metadata.items():
            if not path.exists():
                path.write_text(uncompressed_icon(content) if path.name.startswith("stic_") else content,
                                encoding="utf-8", newline="\n")
            elif path.name.startswith("stic_"):
                existing = path.read_text(encoding="utf-8-sig")
                if not has_uncompressed_icons(existing):
                    path.write_text(uncompressed_icon(existing), encoding="utf-8", newline="\n")
    print(f"{'Verified' if args.check else 'Imported'} {len(rows)} original status HUD PNGs.")


if __name__ == "__main__":
    main()
