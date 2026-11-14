"""Import the original FMOD World Arena door sound events as deterministic PCM clips."""

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
    ("DoorClose", "close", "84290154-dee4-4e6f-933a-e106c1ffc24f", 94_080),
    ("DoorOpen", "open", "e04705e6-c46c-40db-abbd-a8874d1431ab", 98_880),
)
MIXER_PADDING = 5120
FOLDER_IMPORTER = "folderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n"
TEXT_IMPORTER = "TextScriptImporter:\n  externalObjects: {}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n"


def rendered(probe, source, temp, direction, output_frames):
    target = temp / (direction + ".wav")
    # Keep native temporary writes on the project drive.
    environment = dict(os.environ, TEMP=str(temp), TMP=str(temp), TMPDIR=str(temp))
    subprocess.run([str(probe[0]), str(probe[1]), str(TOOLS.parent), str(source / "sound"),
                    "--record-pvp-" + direction, str(target)], check=True, env=environment)
    with wave.open(str(target), "rb") as raw:
        if (raw.getnchannels(), raw.getsampwidth(), raw.getframerate()) != (2, 2, 48_000):
            raise ValueError("Unexpected PCM format from original door event: " + direction)
        raw.setpos(MIXER_PADDING)
        data = raw.readframes(output_frames)
    if len(data) != output_frames * 4 or not any(data):
        raise ValueError("Native door event produced incomplete or silent PCM: " + direction)
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
        raise ValueError("Door audio must stay outside the read-only source directory.")
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
    importer = "\n".join(template.splitlines()[2:]) + "\n"
    importer = importer.replace("    compressionFormat: 1", "    compressionFormat: 0")
    importer = re.sub(r"(?m)^  userData:.*$", "  userData: AstralOath.ImportOverride", importer)
    artifact_dir = project / ".artifacts"
    artifact_dir.mkdir(exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="pvp-gate-audio-", dir=artifact_dir) as tmp:
        for clip, direction, event_id, frames in CLIPS:
            native_name = rf"ui\pvp\door_{direction}"
            sample = rf"assets\ui\treff_pd_pvp_battle_{direction}.wav"
            match = re.search(r"(?m)^" + re.escape(native_name) + r":\n((?: +.+\n)+)", index)
            if not match or event_id not in match[1] or sample not in match[1] or "Bank Name: ui1" not in match[1]:
                raise ValueError("Unexpected original FMOD door event mapping: " + native_name)
            wave_data = rendered((args.dotnet.resolve(), args.probe.resolve()), source, Path(tmp), direction, frames)
            filename = clip + ".wav"
            output = OUTPUT / filename
            write(output, wave_data, args.check)
            metadata(output, args.check, importer)
            manifest["events"].append({"name": native_name.replace("\\", "/"), "eventId": event_id,
                "sourceSample": sample, "bank": "ui1", "asset": filename, "sha256": digest(wave_data),
                "sampleRate": 48000, "sampleFrames": frames, "channels": 2})
    output = OUTPUT / "source.json"
    write(output, json_bytes(manifest), args.check)
    metadata(output, args.check, TEXT_IMPORTER)
    print(f"{'Verified' if args.check else 'Imported'} two native World Arena door events (resolved FMOD IDs + PCM).")


if __name__ == "__main__":
    main()
