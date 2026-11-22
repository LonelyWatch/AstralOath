"""Render the original FMOD gate, Soul Burn, chained action and result events to PCM."""

import argparse
import io
import os
from pathlib import Path
import re
import subprocess
import sys
import tempfile
import wave

from ImportHudEffects import MODULE, RAW_DIRECTORY, TOOLS, json_bytes

sys.path.insert(0, str(TOOLS / "Arena"))
from ImportArenaArt import digest, metadata, read_source, write

OUTPUT = MODULE / "Res/Sounds/PvpGate"
CLIPS = (
    ("DoorClose", "close", r"ui\pvp\door_close", r"assets\ui\treff_pd_pvp_battle_close.wav",
     "84290154-dee4-4e6f-933a-e106c1ffc24f", 94_080),
    ("DoorOpen", "open", r"ui\pvp\door_open", r"assets\ui\treff_pd_pvp_battle_open.wav",
     "e04705e6-c46c-40db-abbd-a8874d1431ab", 98_880),
    ("SoulBurnRelease", "soul-burn", r"effect\ui_livepvp_soulburn_eff", r"assets\ui\ui_livepvp_soulburn_eff.wav",
     "5f151765-7475-467b-8c1e-f0f559099301", 76_800),
    ("ChainFlash", "chain-flash", r"effect\stse_chain", r"assets\model\se_chr_cm_bonus_turn.wav",
     "6fc907ae-9e8e-4159-8dee-914c28f8dbfb", 55_536),
    ("ResultVictory", "result-win", r"ui\pvp\result_win",
     tuple(rf"assets\ui\se_ui_colosseum_battle_win_0{i}.wav" for i in range(1, 4)),
     "d06af45b-59ea-48c5-a976-cf6ffb490c1d", 272_640),
    ("ResultDefeat", "result-lose", r"ui\pvp\result_lose",
     tuple(rf"assets\ui\se_ui_colosseum_battle_lose_0{i}.wav" for i in range(1, 4)),
     "808fb8d9-40e3-4f9c-ac9b-01354474a39d", 240_480),
)
MIXER_PADDING = 5120
FOLDER_IMPORTER = "folderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n"
TEXT_IMPORTER = "TextScriptImporter:\n  externalObjects: {}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n"


def rendered(probe, source, temp, direction, native_name, event_id, output_frames):
    target = temp / (direction + ".wav")
    # Keep native temporary writes on the project drive.
    environment = dict(os.environ, TEMP=str(temp), TMP=str(temp), TMPDIR=str(temp))
    subprocess.run([str(probe[0]), str(probe[1]), str(TOOLS.parent), str(source / "sound"),
                    "--record-ui-event", str(target), "event:/" + native_name.replace("\\", "/"),
                    "{" + event_id + "}", str(max(3000, output_frames // 48 + 500))], check=True, env=environment)
    with wave.open(str(target), "rb") as raw:
        if (raw.getnchannels(), raw.getsampwidth(), raw.getframerate()) != (2, 2, 48_000):
            raise ValueError("Unexpected PCM format from original PvP event: " + direction)
        raw.setpos(MIXER_PADDING)
        data = raw.readframes(output_frames)
    if len(data) != output_frames * 4 or not any(data):
        raise ValueError("Native PvP event produced incomplete or silent PCM: " + direction)
    writer = io.BytesIO()
    with wave.open(writer, "wb") as result:
        result.setparams((2, 2, 48_000, output_frames, "NONE", "not compressed"))
        result.writeframes(data)
    return writer.getvalue()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=Path("D:/sevenepic"))
    parser.add_argument("--dotnet", type=Path, default=TOOLS.parent / ".tools/dotnet/dotnet.exe")
    parser.add_argument("--probe", type=Path, default=TOOLS.parent / ".artifacts/arena-draft/gate-audio-bin/AudioProbe.dll")
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    source = (args.source / RAW_DIRECTORY).resolve(strict=True)
    if OUTPUT.resolve().is_relative_to(source):
        raise ValueError("PvP audio must stay outside the read-only source directory.")
    project = TOOLS.parent
    index = read_source(source, "sound/events.yml").decode("utf-8-sig")
    manifest = {"schemaVersion": 1, "method": "FMOD Studio 2.03.15 authored event PCM render",
                "mixerStartupFramesRemoved": MIXER_PADDING, "events": [], "sources": []}
    for path in ("sound/master.bank", "sound/master.strings.bank", "sound/ui1.bank", "sound/ui1.yml", "sound/events.yml"):
        manifest["sources"].append({"source": path, "sha256": digest(read_source(source, path))})
    if not args.check:
        OUTPUT.mkdir(parents=True, exist_ok=True)
    metadata(OUTPUT, args.check, FOLDER_IMPORTER)
    template = (project / "Assets/Game/Common/Res/Sounds/UiConfirm.wav.meta").read_text(encoding="utf-8-sig")
    importer = "\n".join(line.rstrip() for line in template.splitlines()[2:]) + "\n"
    importer = importer.replace("    compressionFormat: 1", "    compressionFormat: 0")
    importer = re.sub(r"(?m)^  userData:.*$", "  userData: AstralOath.ImportOverride", importer)
    artifact_dir = project / ".artifacts"
    artifact_dir.mkdir(exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="pvp-gate-audio-", dir=artifact_dir) as tmp:
        for clip, direction, native_name, sample, event_id, frames in CLIPS:
            match = re.search(r"(?m)^" + re.escape(native_name) + r":\n((?: +.+\n)+)", index)
            samples = (sample,) if isinstance(sample, str) else sample
            if (not match or event_id not in match[1] or "Bank Name: ui1" not in match[1]
                    or any(s not in match[1] for s in samples)):
                raise ValueError("Unexpected original FMOD PvP event mapping: " + native_name)
            wave_data = rendered((args.dotnet.resolve(), args.probe.resolve()), source, Path(tmp), direction, native_name, event_id, frames)
            filename = clip + ".wav"
            output = OUTPUT / filename
            write(output, wave_data, args.check)
            metadata(output, args.check, importer)
            manifest["events"].append({"name": native_name.replace("\\", "/"), "eventId": event_id,
                ("sourceSample" if isinstance(sample, str) else "sourceSamples"): sample,
                "bank": "ui1", "asset": filename, "sha256": digest(wave_data),
                "sampleRate": 48000, "sampleFrames": frames, "channels": 2})
    output = OUTPUT / "source.json"
    write(output, json_bytes(manifest), args.check)
    metadata(output, args.check, TEXT_IMPORTER)
    print(f"{'Verified' if args.check else 'Imported'} {len(CLIPS)} native World Arena events (resolved FMOD IDs + PCM).")


if __name__ == "__main__":
    main()
