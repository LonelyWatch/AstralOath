"""Import only the source bank required by the preview slime's confirmed STG hit effect."""
import argparse
import hashlib
import json
import shutil
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUTPUT = ROOT / "Assets/Game/Modules/BattleScene/Res/Sounds"


def sha256(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=Path("D:/sevenepic"))
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    source = args.source.resolve(strict=True) / "原始资源"
    source_bank = source / "sound/gerneal.bank"
    destination = OUTPUT / "gerneal.bank.bytes"
    stg = json.loads((source / "stagept/m_slime_skill_01.stg").read_text(encoding="utf-8"))
    hit = next(node for node in stg["entities"] if node["etty"] == "HIT")
    if hit["effect"] != "hit_new_blunt_normal.cfx":
        raise ValueError("Slime source HIT effect changed; review the audio mapping before import")
    if not args.check:
        OUTPUT.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(source_bank, destination)
    if not destination.is_file() or sha256(destination) != sha256(source_bank):
        raise ValueError("Imported impact bank is missing or differs from the read-only source")
    manifest = {
        "schemaVersion": 1,
        "importerSha256": sha256(Path(__file__)),
        "files": [{"source": "原始资源/sound/gerneal.bank", "sourceSha256": sha256(source_bank),
                   "asset": "gerneal.bank.bytes", "assetSha256": sha256(destination)}],
        "evidence": [{"source": "原始资源/" + name, "sourceSha256": sha256(source / name)} for name in (
            "stagept/m_slime_skill_01.stg", "model/slime.scsp", "sound/master.strings.bank", "sound/sfx1.bank",
            "sound/voc.event.bank", "sound/ja/voc1.audio_ja.bank")],
        "events": {
            "preview.slime.s1": {"attack": "event:/model/slime/ani/skill1", "bank": "sfx1.bank",
                "impact": "event:/effect/hit_new_blunt_normal", "impactBank": "gerneal.bank",
                "sourceHitEffect": hit["effect"], "sourceHitSeconds": 0.7},
            "targetReactions": {"source": "CharacterLab/Configs/catalog.json characters[].voices[].eventPath",
                "suffix": "/ani/attacked", "banks": ["voc.event.bank", "ja/voc1.audio_ja.bank"]}
        }
    }
    text = json.dumps(manifest, ensure_ascii=False, indent=2) + "\n"
    manifest_path = OUTPUT / "source.json"
    if args.check:
        if manifest_path.read_text(encoding="utf-8") != text:
            raise ValueError("Battle audio source manifest differs; run import and review")
        print("Battle audio bank and source evidence PASS")
    else:
        manifest_path.write_text(text, encoding="utf-8", newline="\n")
        print("Imported source impact bank", destination.stat().st_size, "bytes")


if __name__ == "__main__":
    main()
