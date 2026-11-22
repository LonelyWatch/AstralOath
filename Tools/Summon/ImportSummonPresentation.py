"""Import the original summon choreography and render its authored FMOD audio tracks."""

import argparse
import io
import json
import os
from pathlib import Path
import plistlib
import re
import struct
import subprocess
import sys
import tempfile
import wave

REPO = Path(__file__).resolve().parents[2]
sys.path[:0] = [str(REPO / "Tools/CharacterAssets"), str(REPO / "Tools/Arena")]
import convert
from import_characters import atlas_pages, resolve_atlas
from ImportArenaArt import digest, metadata, png_bytes, read_source, texture_importer, write

DEST = REPO / "Assets/Game/Modules/Summon/Res/Presentation"
ROOTS = tuple((
    "1_blue", "1_yellow1", "1_violet1", "2_b_blue", "2_b_yellow", "2_b_violet",
    "2_class", "2_blue", "2_yellow", "2_violet", "2_blue_yellow", "2_blue_violet", "2_yellow_violet",
    "3_b", "3_f", "3_character_bone", "3_portrait_bone", "3_artifact_bone",
))
AUDIO = {
    **{name: r"ui\gacha_new\gocha_effect_" + suffix for name, suffix in (
        ("OpenBlue", "01_normal"), ("OpenGold", "01_rare"), ("OpenViolet", "01_special"),
        ("OpenGoldMoonlight", "01_rare_moonlight"), ("OpenVioletMoonlight", "01_special_moonlight"),
        ("Hall", "02"), ("HallMoonlight", "02_moonlight"), ("Figure", "03"),
        ("BurstBlue", "04_normal"), ("BurstGold", "04_rare"), ("BurstViolet", "04_special"),
        ("BurstFakeGold", "04_fake_rare"), ("BurstFakeViolet", "04_fake_special"),
        ("BurstFakeViolet2", "04_fake_special2"), ("Summary", "05"),
    )},
    "ArtifactReveal": r"ui\gacha\summon_result_artifact",
    "FlipCommon": r"ui\gacha_card_flip_common", "FlipRare": r"ui\gacha_card_flip_rare",
    "FlipReady": r"ui\gacha_card_flip_ready", "New": r"ui\gacha_card_new",
    "Theme": r"bgm\summon_title", "Ceremony": r"bgm\summon_efx",
}
FOLDER = "folderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n"
TEXT = "TextScriptImporter:\n  externalObjects: {}\n"


def json_bytes(data):
    return (json.dumps(data, ensure_ascii=False, indent=2, allow_nan=False) + "\n").encode("utf-8")


def stem(value):
    if not re.fullmatch(r"[a-zA-Z0-9_]+", value):
        raise ValueError(f"Unsafe native animation file stem: {value}")
    return value


def decode_skeleton(name, data):
    if name != "gacha_eff_3_intro_1":
        return convert.decode_scsp(data)

    # This export repeats the same pivot rotation. Only accept an exact copy
    # of the original key and curve bytes for this animation.
    original = convert._record_curve_source
    repeated_rotation = False

    def record(reader, skeleton, animation, kind, start, blocks):
        nonlocal repeated_rotation
        try:
            original(reader, skeleton, animation, kind, start, blocks)
        except ValueError as error:
            if kind != 1 or animation.name != "animation" or not str(error).startswith("Duplicate curve timeline:"):
                raise
            index = struct.unpack_from("<I", reader.data, start + 4)[0]
            if skeleton.bones[index].name != "pivot" or repeated_rotation:
                raise
            now = reader.curveBlocks[-1]
            before = skeleton.curveSource[(animation.name, "bones", "pivot", "rotate")]
            before_start = before["offset"] - (now["offset"] - start)
            if reader.data[before_start:before["end"]] != reader.data[start:now["end"]]:
                raise
            repeated_rotation = True

    convert._record_curve_source = record
    try:
        document, report = convert.decode_scsp(data)
    finally:
        convert._record_curve_source = original
    # The shared decoder now preserves additional scale tracks explicitly.
    # This one repeats the canonical track. Keep the published JSON stable
    # after checking every decoded key and curve for exact equality.
    animation = document["animations"]["animation"]
    pivot_scale = animation["bones"]["pivot"]["scale"]
    if not repeated_rotation or animation.get(convert.ORDERED_SCALE_FIELD) != {"pivot": [pivot_scale]}:
        raise ValueError("Native summon pivot record changed; reverify choreography.")
    del animation[convert.ORDERED_SCALE_FIELD]
    return document, report


def collect_visual(source):
    textures = {}
    skeletons = {}
    sources = {}
    payloads = {}

    def raw(path):
        data = read_source(source, path)
        sources[path] = digest(data)
        return data

    def load_skeleton(name):
        if name in skeletons:
            return skeletons[name]
        skeleton_path = "effect/" + name + ".scsp"
        alias = "effect/" + name + ".atlas"
        raw(alias)
        actual_atlas = resolve_atlas(source, alias)
        atlas = raw(actual_atlas)
        document, report = decode_skeleton(name, raw(skeleton_path))
        skeleton_file = name + ".spine.json"
        atlas_file = Path(actual_atlas).name + ".txt"
        payloads[skeleton_file] = json.dumps(document, ensure_ascii=False, allow_nan=False, separators=(",", ":")).encode("utf-8")
        payloads[atlas_file] = atlas
        pages = []
        for page in atlas_pages(source, alias):
            key = page["source"]
            if key not in textures:
                output = Path(key).with_suffix(".png").name
                if output in payloads:
                    raise ValueError(f"Conflicting source texture alias: {key}")
                texture, _ = convert.decode_sct(raw(key))
                payloads[output] = png_bytes(texture)
                textures[key] = output
            pages.append({"name": page["name"], "asset": textures[key]})
        skeletons[name] = {"id": name, "document": skeleton_file, "atlas": atlas_file, "pages": pages,
                           "animations": report["animations"]}
        return skeletons[name]

    groups = []
    for name in ROOTS:
        path = "effect/gacha_effect_" + name + ".cfx"
        parts = []
        native = plistlib.loads(raw(path))
        for order, entry in enumerate(native["primitive"]):
            if entry["format"] != "spine":
                raise ValueError(f"Unsupported visual primitive: {path} {entry['format']}")
            asset = load_skeleton(stem(entry["source"]))
            animation = entry.get("ani") or "animation"
            animations = {row["name"] for row in asset["animations"]}
            if not set(animation.split(",")).issubset(animations):
                raise ValueError(f"Unknown animation in native CFX {path}: {animation}")

            def number(key, default=0):
                return float(str(entry.get(key, "")).strip() or default)

            parts.append({"source": asset["id"], "animation": animation, "order": order, "x": number("x"),
                          "y": number("y"), "z": number("z"), "globalZ": number("globalZ"),
                          "delay": number("delay"), "scale": number("scale", 1), "rotate": number("rotate"),
                          "speed": number("speed", 1), "loop": bool(number("loop"))})
        if name == "2_class":
            # The standalone class CFX omits order. The original composite supplies depth 35
            # between the main hall (30) and the figure lighting (39–51).
            authored = plistlib.loads(raw("effect/gacha_effect_2_b_test.cfx"))["primitive"]
            parts[0]["z"] = float(next(item["z"] for item in authored if item["source"] == parts[0]["source"]))
        parts.sort(key=lambda part: (part["globalZ"], part["z"], part["order"]))
        groups.append({"id": name, "parts": parts})
    return {"groups": groups, "skeletons": list(skeletons.values())}, payloads, sources


def audio_rows(source):
    index = read_source(source, "sound/events.yml").decode("utf-8-sig")
    rows = []
    for name, path in AUDIO.items():
        match = re.search(r"(?m)^" + re.escape(path) + r":\n((?: +.+\n)+)", index)
        if not match:
            raise ValueError(f"Missing original sound event {path}")
        block = match[1]
        bank = re.search(r"(?m)^    Bank Name: (ui1|bgm4)$", block)[1]
        guid = re.search(r"(?m)^    id: '[{]([a-f0-9-]{36})[}]'$", block)[1]
        samples = re.findall(r"(?m)^        (assets\\.+?): assets\\", block)
        if not samples:
            raise ValueError(f"Missing original event samples in {path}")
        event = "event:/" + path.replace("\\", "/")
        rows.append({"id": name, "event": event, "guid": guid, "bank": bank, "samples": samples, "asset": name + ".wav"})
    return rows


def voice_rows(source):
    # Keep the same release's set of heroes whose 3D presentation is already
    # available. Match native *get* events, including Charlotte's *_gc_1 alias.
    heroes = json.loads((REPO / "Assets/Game/Modules/BattleScene/Configs/battle-catalog.json").read_text("utf-8"))["heroes"]
    allowed = {hero["id"] for hero in heroes}
    index = read_source(source, "sound/events.yml").decode("utf-8-sig")
    voices = {}
    for path in sorted((source / "sound/ja").glob("voc*.audio_ja.yml")):
        bank = path.stem
        data = path.read_text("utf-8-sig")
        for match in re.finditer(r"(?im)^\s+- .*[\\/]((voc_(c\d{4})_(?:get|gc)(?:_1)?)\.wav)\s*$", data):
            _, key, hero_id = match.groups()
            if hero_id in allowed:
                if hero_id in voices:
                    raise ValueError("Duplicate original acquisition voice for " + hero_id)
                voices[hero_id] = (bank, key)
    rows = []
    for hero in sorted(allowed):
        if hero not in voices:
            raise ValueError("Missing original Japanese acquisition voice: " + hero)
        bank, key = voices[hero]
        events = re.finditer(r"(?m)^(voc\\character\\[a-zA-Z0-9_]+\\evt\\get):\n((?: +.+\n)+)", index)
        owner = next((item for item in events if f"        {key}: {key}\n" in item[2]), None)
        if not owner:
            raise ValueError(f"Original acquisition sample is not linked to an acquisition event: {key}")
        guid = re.search(r"(?m)^    id: '[{]([a-f0-9-]{36})[}]'$", owner[2])[1]
        rows.append({"id": "Voice_" + hero, "event": "event:/" + owner[1].replace("\\", "/"), "guid": guid,
                     "bank": "ja/" + bank, "samples": [key], "asset": "Voice_" + hero + ".wav", "audioTableKey": key})
    return rows


def stereo_clip(path, expected_frames, normalize_master_padding):
    with wave.open(str(path), "rb") as audio:
        if (audio.getnchannels(), audio.getsampwidth(), audio.getframerate()) != (2, 2, 48000):
            raise ValueError(f"Wrong PCM format: {path.name}")
        if audio.getnframes() < normalize_master_padding + expected_frames:
            raise ValueError(f"Short native audio capture {path.name}: {audio.getnframes()} < {expected_frames}")
        audio.setpos(normalize_master_padding)
        data = audio.readframes(expected_frames)
    if not any(data):
        raise ValueError(f"Silent native audio capture: {path.name}")
    buffer = io.BytesIO()
    with wave.open(buffer, "wb") as wav:
        wav.setparams((2, 2, 48000, expected_frames, "NONE", "not compressed"))
        wav.writeframes(data)
    return buffer.getvalue()


def validate_music_stream(content, name):
    # A stalled streaming decoder repeats its active PCM buffer bit for bit.
    # Check audible one-second windows at separate points in the music.
    with wave.open(io.BytesIO(content)) as clip:
        rate = clip.getframerate()
        for second in (2, 5, 12, 20):
            clip.setpos(second * rate)
            window = clip.readframes(2 * rate)
            stride = clip.getnchannels() * clip.getsampwidth()
            probe = window[:rate * stride]
            if len(window) == 2 * len(probe) and any(probe) and window.find(probe, stride) >= 0:
                raise ValueError(f"{name} repeats the streaming decoder buffer at {second}s.")


def record_audio(source, dotnet, probe, rows, check):
    work = REPO / ".artifacts/summon-presentation"
    work.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(dir=work, prefix="fmod-") as directory:
        folder = Path(directory)
        args = [str(dotnet), str(probe), str(REPO), str(source / "sound")]
        for row in rows:
            key = row.get("audioTableKey", row["event"])
            args += [key, "{" + row["guid"] + "}", row["bank"], str(folder / row["asset"])]
        environment = dict(os.environ, TEMP=str(folder), TMP=str(folder), TMPDIR=str(folder))
        result = subprocess.run(args, capture_output=True, text=True, encoding="utf-8", env=environment, check=True)
        records = {}
        for line in result.stdout.splitlines():
            event, duration, samples = line.split("\t")
            records[event] = (int(duration), int(samples))
        payloads = {}
        for row in rows:
            duration, buffer_size = records[row.get("audioTableKey", row["event"])]
            frames = duration * 48
            padding = buffer_size * 5
            acquired = stereo_clip(folder / row["asset"], frames, padding)
            if row['bank'] == 'bgm4':
                validate_music_stream(acquired, row['asset'])
            if check:
                # FMOD can use different native random event variations.
                # A fresh non-silent render proves the source is available.
                # The first retained take is verified against its own SHA-256.
                persisted = DEST / row["asset"]
                with wave.open(str(persisted), "rb") as music:
                    if (music.getnchannels(), music.getsampwidth(), music.getframerate(), music.getnframes()) != (2, 2, 48000, frames):
                        raise ValueError(f"Unexpected archived native audio format: {persisted}")
                acquired = persisted.read_bytes()
                if row['bank'] == 'bgm4':
                    validate_music_stream(acquired, row['asset'])
            payloads[row["asset"]] = acquired
            row.update(sampleFrames=frames, mixerPaddingFrames=padding)
        return payloads


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=Path("D:/sevenepic"))
    parser.add_argument("--dotnet", type=Path, default=REPO / ".tools/dotnet/dotnet.exe")
    parser.add_argument("--probe", type=Path, default=REPO / ".artifacts/summon-presentation/probe/AudioProbe.dll")
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    source = (args.source / "原始资源").resolve(strict=True)
    if DEST.resolve().is_relative_to(source):
        raise ValueError("Original source is read-only.")
    graph, payloads, source_hashes = collect_visual(source)
    rows = audio_rows(source) + voice_rows(source)
    for path in ("sound/events.yml", "sound/master.bank", "sound/master.strings.bank", "sound/ui1.bank",
                 "sound/ui1.yml", "sound/bgm4.bank", "sound/bgm4.yml", "sound/voc.event.bank"):
        source_hashes[path] = digest(read_source(source, path))
    for bank in sorted({row["bank"] for row in rows if row["bank"].startswith("ja/")}):
        for suffix in (".bank", ".yml"):
            path = "sound/" + bank + suffix
            source_hashes[path] = digest(read_source(source, path))
    payloads.update(record_audio(source, args.dotnet.resolve(strict=True), args.probe.resolve(strict=True), rows, args.check))
    graph["audio"] = rows
    payloads["presentation.json"] = json_bytes(graph)
    payloads["source-manifest.json"] = json_bytes({
        "sources": source_hashes,
        "outputs": {key: digest(data) for key, data in payloads.items()},
        "method": "Native CFX layers and FMOD Studio authored events",
    })
    metadata(DEST, args.check, FOLDER)
    template = (REPO / "Assets/Game/Modules/BattleScene/Res/Sounds/PvpGate/SoulBurnRelease.wav.meta").read_text("utf-8-sig")
    audio_import = "\n".join(template.splitlines()[2:]).replace("preloadAudioData: 0", "preloadAudioData: 1") + "\n"
    for name, content in payloads.items():
        path = DEST / name
        write(path, content, args.check)
        if path.suffix == ".png":
            options = texture_importer(path, sprite=False)
            options = re.sub(r"(?m)^(\s+textureCompression:) \d+$", r"\1 0", options)
            options = re.sub(r"(?m)^(\s+maxTextureSize:) \d+$", r"\1 4096", options)
            options = re.sub(r"(?m)^  userData:.*$", "  userData: AstralOath.ImportOverride", options)
        elif path.suffix == ".wav":
            options = audio_import if name not in ("Theme.wav", "Ceremony.wav") else (
                audio_import.replace("loadType: 0", "loadType: 2").replace("compressionFormat: 0", "compressionFormat: 1")
                .replace("preloadAudioData: 1", "preloadAudioData: 0"))
        else:
            options = TEXT
        metadata(path, args.check, options)
    print(f"Verified {len(graph['groups'])} source compositions, {len(graph['skeletons'])} skeletons and {len(rows)} FMOD audio tracks.")


if __name__ == "__main__":
    main()
