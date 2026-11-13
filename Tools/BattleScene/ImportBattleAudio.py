"""Import the source bank shared by the preview slime hit and the Burn ready cue."""
import argparse
import hashlib
import json
import shutil
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUTPUT = ROOT / "Assets/Game/Modules/BattleScene/Res/Sounds"
SHARED_IMPACT_BANK = "Assets/Game/Modules/CharacterPresentation/Res/Shared/Sounds/sound/gerneal.bank.bytes"


def sha256(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=Path("D:/sevenepic"))
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    source = args.source.resolve(strict=True) / "原始资源"
    source_bank = source / "sound/gerneal.bank"
    destination = ROOT / SHARED_IMPACT_BANK
    stg = json.loads((source / "stagept/m_slime_skill_01.stg").read_text(encoding="utf-8"))
    hit = next(node for node in stg["entities"] if node["etty"] == "HIT")
    if hit["effect"] != "hit_new_blunt_normal.cfx":
        raise ValueError("Slime source HIT effect changed; review the audio mapping before import")
    burn_event_name = r"battle\soulburn\ready"
    burn_sample = r"assets\ui\se_ui_battle_soul_burn_on.wav"
    sound_index = (source / "sound/gerneal.yml").read_text(encoding="utf-8")
    if f"    {burn_event_name}:\n        - {burn_sample}\n" not in sound_index:
        raise ValueError("Burn ready event or source sample changed in the shared bank index")
    source_bank_hash = sha256(source_bank)
    if not args.check and (not destination.is_file() or sha256(destination) != source_bank_hash):
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(source_bank, destination)
    if not destination.is_file() or sha256(destination) != source_bank_hash:
        raise ValueError("Imported impact bank is missing or differs from the read-only source")
    manifest = {
        "schemaVersion": 1,
        "importerSha256": sha256(Path(__file__)),
        "files": [{"source": "原始资源/sound/gerneal.bank", "sourceSha256": source_bank_hash,
                   "asset": SHARED_IMPACT_BANK, "assetSha256": sha256(destination)}],
        "evidence": [{"source": "原始资源/" + name, "sourceSha256": sha256(source / name)} for name in (
            "stagept/m_slime_skill_01.stg", "model/slime.scsp", "sound/master.strings.bank", "sound/sfx1.bank",
            "sound/voc.event.bank", "sound/ja/voc1.audio_ja.bank", "sound/gerneal.yml", "sound/events.yml")],
        "events": {
            "preview.slime.s1": {"attack": "event:/model/slime/ani/skill1", "bank": "sfx1.bank",
                "impact": "event:/effect/hit_new_blunt_normal", "impactBank": "gerneal.bank",
                "sourceHitEffect": hit["effect"], "sourceHitSeconds": 0.7},
            "burnReady": {"event": "event:/battle/soulburn/ready", "bank": "gerneal.bank",
                "sourceSample": burn_sample, "sourceEventId": "39f88ea3-22f3-4523-ad8a-16c691a45136"},
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
