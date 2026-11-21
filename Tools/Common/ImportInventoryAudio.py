"""Render the inventory UI events from the local, read-only FMOD banks."""

import argparse
import array
import hashlib
import io
import json
from pathlib import Path
import re
import sys
import tempfile
import wave

PROJECT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(PROJECT / "Tools/BattleScene"))
from ImportPvpGateAudio import rendered
from ImportArenaArt import guid, write

SOUNDS = PROJECT / "Assets/Game/Common/Res/Sounds"
EVENTS = {
    "InventoryOpen": "ui/top_bar/inventory",
    "QuickMenuOpen": "ui/top_bar/quick_menu_on",
    "QuickMenuClose": "ui/top_bar/quick_menu_off",
    "EquipWeapon": "ui/equip/equip_weapon",
    "EquipHelmet": "ui/equip/equip_helm",
    "EquipArmor": "ui/equip/equip_armor",
    "EquipNecklace": "ui/equip/equip_neck",
    "EquipRing": "ui/equip/equip_ring",
    "EquipBoots": "ui/equip/equip_boot",
    "EquipArtifact": "ui/equip/equip_artifact",
    "EquipRemove": "ui/equip_off",
    "ItemLock": "ui/unit_detail/btn_lock",
    "HeroPageOpen": "ui/menu/menu_1",
}


def trimmed(data):
    with wave.open(io.BytesIO(data)) as source:
        samples = array.array("h", source.readframes(source.getnframes()))
    active = [i // 2 for i, value in enumerate(samples) if abs(value) > 12]
    if not active:
        raise ValueError("Empty UI event")
    first, last = max(0, active[0] - 8), active[-1] + 64
    output = io.BytesIO()
    with wave.open(output, "wb") as writer:
        writer.setparams((2, 2, 48000, 0, "NONE", ""))
        writer.writeframes(samples[first * 2:last * 2].tobytes())
    return output.getvalue(), last - first


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--raw", type=Path, default=Path("D:/sevenepic/原始资源"))
    parser.add_argument("--probe", type=Path, default=PROJECT / ".artifacts/inventory-audio-tools/AudioProbe.dll")
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    index = (args.raw / "sound/events.yml").read_text(encoding="utf-8-sig")
    manifest = SOUNDS.parent / "Textures/ui-feedback-source.json"
    original = manifest.read_text(encoding="utf-8-sig")
    existing = json.loads(original)["audio"]
    importer = (SOUNDS / "UiRosterScroll.wav.meta").read_text().split("AudioImporter:", 1)[1]
    importer = re.sub(r"(?m)^  userData:.*$", "  userData: AstralOath.ImportOverride", importer)
    importer = "AudioImporter:" + importer.replace("compressionFormat: 1", "compressionFormat: 0")
    renderer = (PROJECT / ".tools/dotnet/dotnet.exe", args.probe.resolve())
    entries = []
    with tempfile.TemporaryDirectory(dir=PROJECT / ".artifacts", prefix="inventory-audio-") as tmp:
        for role, event in EVENTS.items():
            native = event.replace("/", "\\")
            block = re.search(r"(?m)^" + re.escape(native) + r":\n((?: +[^\n]+\n)+)", index).group(1)
            if "Bank Name: ui1" not in block:
                raise ValueError("Unexpected FMOD bank for " + event)
            event_id = re.search(r"id: '\{([^}]+)\}'", block).group(1)
            samples = re.findall(r"(?m)^        assets\\ui\\([^:]+):", block)
            rendered_pcm = rendered(renderer, args.raw, Path(tmp), role, native, event_id, 96000)
            pcm, frames = trimmed(rendered_pcm)
            name = "Ui" + role + ".wav"
            write(SOUNDS / name, pcm, args.check)
            meta = SOUNDS / (name + ".meta")
            identifier = guid(SOUNDS / name)
            if meta.exists():
                identifier = re.search(r"(?m)^guid: ([a-f0-9]{32})$", meta.read_text()).group(1)
            write(meta, f"fileFormatVersion: 2\nguid: {identifier}\n{importer}".encode(), args.check)
            entries.append({"path": name, "event": "event:/" + event, "eventId": event_id, "bank": "sound/ui1.bank",
                            "sourceSamples": samples, "pcmFrames": frames, "sha256": hashlib.sha256(pcm).hexdigest()})
    # Preserve the records for other controls and the formatting of the rest of the manifest.
    names = {entry["path"] for entry in entries}
    records = [entry for entry in existing if entry["path"] not in names] + entries
    if records != existing:
        if args.check:
            raise ValueError("Inventory audio manifest differs")
        audio = json.dumps(records, indent=2, ensure_ascii=False).replace("\n", "\n  ")
        original = re.sub(r'(?s)("audio": )\[.*?\n  \](,\n  "pulseFrameAtlas")',
                          lambda m: m[1] + audio + m[2], original, count=1)
        manifest.write_text(original, encoding="utf-8", newline="\n")
    print(f"Verified {len(entries)} native UI event clips.")


if __name__ == "__main__":
    main()
