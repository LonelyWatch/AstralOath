"""Import only the original live-PvP team HUD sprites; never modify source assets."""
import argparse
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "Tools/Arena"))
from ImportArenaArt import digest, metadata, read_source, texture_importer, write

OUTPUT = ROOT / "Assets/Game/Modules/BattleScene/Resources/AstralOathOnlineHud"
NAMES = ("_battle_pvp_icon_vs", "game_hud_bar_pvplive", "game_hud_bar_hp",
         "game_hud_bar_hp_red", "game_hud_bar_split", "_hero_s_frame_enemy", "_hero_s_bg_ally",
         "time_progress_bg", "time_progress_bar_blue", "time_progress_bar_red")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=Path("D:/sevenepic/原始资源"))
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    source = args.source.resolve(strict=True)
    rows = []
    for name in NAMES:
        relative = "img/" + name + ".png"
        data = read_source(source, relative)
        target = OUTPUT / (name + ".png")
        write(target, data, args.check)
        importer = texture_importer(target, sprite=True)
        importer = re.sub(r"(?m)^(\s+textureCompression:) \d+$", r"\1 0", importer)
        importer = re.sub(r"(?m)^  userData:.*$", "  userData: AstralOath.ImportOverride", importer)
        metadata(target, args.check, importer)
        actual_meta = target.with_name(target.name + ".meta").read_text(encoding="utf-8-sig")
        if any(value != "0" for value in re.findall(r"(?m)^\s+textureCompression: (\d+)$", actual_meta)):
            raise ValueError(f"Online HUD artwork must remain uncompressed: {target.name}")
        if "userData: AstralOath.ImportOverride" not in actual_meta:
            raise ValueError(f"Online HUD import override missing: {target.name}")
        rows.append({"source": relative, "asset": target.relative_to(ROOT).as_posix(), "sha256": digest(data)})
    layout = "wnd/pvplive_global_hp.csb"
    report = {"sourceRoot": str(source), "assets": rows,
              "layout": {"source": layout, "sha256": digest(read_source(source, layout))},
              "transfer": "Byte-identical selected PNGs. CSB is layout evidence, not a runtime asset."}
    manifest = OUTPUT / "source.json"
    write(manifest, (json.dumps(report, ensure_ascii=False, indent=2) + "\n").encode("utf-8"), args.check)
    metadata(manifest, args.check, "TextScriptImporter:\n  externalObjects: {}\n")
    metadata(OUTPUT, args.check, "folderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n")
    print("Verified" if args.check else "Imported", len(rows), "native PvP HUD sprites.")


if __name__ == "__main__":
    main()
