"""Import the source HUD sprites referenced by the battle roster CSV files."""
import argparse
import csv
import hashlib
import io
import json
import re
import uuid
from pathlib import Path

from PIL import Image


REPO = Path(__file__).resolve().parents[2]
HUD = REPO / "Assets/Game/Modules/BattleScene/Res/Textures/Hud"
MANIFEST = HUD / "source-manifest.json"
ROLE_NAMES = {"thief": "assassin", "soul_weaver": "manauser"}


def table(name):
    with (REPO / "Config/Tables" / f"{name}.csv").open(encoding="utf-8-sig", newline="") as source:
        return list(csv.DictReader(source))


def roster_art(heroes, skills, source):
    files = {
        "Skills/_frame_passive.png": "skill/_frame_passive.png",
        "Skills/cm_cool_skill_passive.png": "img/cm_cool_skill_passive.png",
    }
    for hero in heroes:
        identifier = hero["id"]
        for suffix in ("s", "su"):
            name = f"{identifier}_{suffix}.png"
            files[f"Faces/{name}"] = f"face/{name}"
        role = ROLE_NAMES.get(hero["heroClass"], hero["heroClass"])
        for name in (f"cm_icon_prom{hero['element']}.png", f"cm_icon_role_{role}.png"):
            files[name] = f"img/{name}"
    for skill in skills:
        for field in ("iconAsset", "speedUpIconAsset"):
            if not skill.get(field):
                continue
            path = Path(skill[field]).relative_to(HUD.relative_to(REPO))
            if path.parent != Path("Skills") or path.suffix != ".png":
                raise ValueError(f"Skill icon must be a PNG under Skills: {path}")
            files[path.as_posix()] = f"skill/{path.name}"
        if skill["target"] == "passive":
            name = f"pa_{skill['heroId']}_{skill['slot']}.png"
            original = f"skill/{name}"
            if contained(source, f"原始资源/{original}").is_file():
                files[f"Skills/{name}"] = original
    return files


def contained(root, relative):
    path = (root / relative).resolve()
    if not path.is_relative_to(root):
        raise ValueError(f"Asset escaped its root: {relative}")
    return path


def import_art(source, files, check=False):
    manifest = json.loads(MANIFEST.read_text(encoding="utf-8-sig"))
    registered = {row["asset"]: row for row in manifest["assets"]}
    template = (HUD / "Skills/sk_c1001_1.png.meta").read_text(encoding="utf-8-sig")
    template = "\n".join(line.rstrip() for line in template.splitlines()) + "\n"
    additions = []
    outputs = []
    hud = HUD.resolve()
    for asset, original in sorted(files.items()):
        relative = f"原始资源/{original}"
        data = contained(source, relative).read_bytes()
        with Image.open(io.BytesIO(data)) as image:
            if image.format != "PNG":
                raise ValueError(f"Expected PNG: {relative}")
            bounds = image.convert("RGBA").getchannel("A").getbbox()
            if bounds is None:
                raise ValueError(f"Entirely transparent sprite: {relative}")
            entry = {"asset": asset, "source": relative, "sha256": hashlib.sha256(data).hexdigest(),
                     "width": image.width, "height": image.height, "alphaBoundsLTRB": list(bounds)}
        previous = registered.get(asset)
        if previous is None:
            additions.append(entry)
        elif previous != entry:
            raise ValueError(f"Existing source manifest differs: {asset}")
        target = contained(hud, asset)
        metadata = target.with_suffix(".png.meta")
        if check:
            if not target.is_file() or target.read_bytes() != data or not metadata.is_file():
                raise ValueError(f"Imported sprite or metadata differs: {asset}")
        else:
            outputs.append((target, metadata, data))
    if check and additions:
        raise ValueError("Missing roster entries in the source manifest: " + ", ".join(row["asset"] for row in additions))
    for target, metadata, data in outputs:
        target.parent.mkdir(parents=True, exist_ok=True)
        if not target.exists() or target.read_bytes() != data:
            target.write_bytes(data)
        if not metadata.exists():
            guid = uuid.uuid5(uuid.NAMESPACE_URL, "astraloath:" + target.relative_to(REPO).as_posix()).hex
            metadata.write_text(re.sub(r"(?m)^guid: [0-9a-f]+$", "guid: " + guid, template, count=1),
                                encoding="utf-8", newline="\n")
    if additions:
        manifest["assets"].extend(additions)
        MANIFEST.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
    print(f"{'Verified' if check else 'Imported'} {len(files)} roster HUD sprites, {len(additions)} new manifest entries.")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=Path("D:/sevenepic"))
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    source = args.source.resolve(strict=True)
    import_art(source, roster_art(table("battle_heroes"), table("battle_skills"), source), args.check)


if __name__ == "__main__":
    main()
