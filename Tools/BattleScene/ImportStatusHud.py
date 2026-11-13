"""Prepare battle status glyphs and preserve the native HUD number textures."""
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
    ("Provoke", "Debuff", "stic_provoke"),
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


GLYPH_PLATFORM_COMPRESSION = re.compile(
    r"(buildTarget: (?:DefaultTexturePlatform|Standalone|iPhone|Android)\n"
    r"(?:    [^\n]*\n)*?    textureCompression: )([0-9]+)"
)


def glyph_sampling(meta, bilinear):
    updated = GLYPH_PLATFORM_COMPRESSION.sub(r"\g<1>0", meta)
    for key, value in (("filterMode", "1" if bilinear else "0"), ("enableMipMap", "0"), ("userData", "AstralOath.ImportOverride")):
        updated, count = re.subn(r"(?m)^( +)" + key + r":[^\n]*$", r"\g<1>" + key + ": " + value, updated)
        if count != 1:
            raise ValueError(f"Missing battle HUD glyph importer property: {key}")
    settings = GLYPH_PLATFORM_COMPRESSION.findall(updated)
    if len(settings) != 4:
        raise ValueError("Missing required battle HUD glyph platform settings.")
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
                "sampling": "Bilinear for projected overhead status icons and turn digits; no mipmaps or compression; explicit import override.",
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
    # These sprites already have source provenance in the HUD / Readiness manifests.
    for asset in [*(HUD / f"itxt_num{digit}_b.png" for digit in range(10)), *sorted((HUD / "Readiness").glob("stic_*.png"))]:
        meta = asset.with_suffix(".png.meta")
        if not asset.is_file() or not meta.is_file():
            raise ValueError(f"Missing existing battle HUD glyph: {asset}")
        metadata[meta] = meta.read_text(encoding="utf-8-sig")
    if args.check:
        mismatches = [str(path) for path, content in writes.items() if not path.is_file() or path.read_bytes() != content]
        mismatches += [str(path) for path in metadata if not path.is_file()]
        for path in metadata:
            if path.name.endswith(".png.meta") and path.is_file():
                existing = path.read_text(encoding="utf-8-sig")
                if existing != glyph_sampling(existing, path.parent == OUTPUT):
                    mismatches.append(str(path))
        if mismatches:
            parser.exit(1, "Missing or changed status assets: " + ", ".join(mismatches) + "\n")
    else:
        OUTPUT.mkdir(parents=True, exist_ok=True)
        for path, content in writes.items():
            if not path.is_file() or path.read_bytes() != content:
                path.write_bytes(content)
        for path, content in metadata.items():
            if not path.exists() or path.name.endswith(".png.meta"):
                existing = path.read_text(encoding="utf-8-sig") if path.exists() else None
                updated = glyph_sampling(existing or content, path.parent == OUTPUT) if path.name.endswith(".png.meta") else content
                if existing != updated:
                    path.write_text(updated, encoding="utf-8", newline="\n")
    print(f"{'Verified' if args.check else 'Imported'} {len(rows)} original status HUD PNGs.")


if __name__ == "__main__":
    main()
