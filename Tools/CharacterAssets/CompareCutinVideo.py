"""Locate the default hero cut-ins in a local battle video. No network access or Unity edits."""
import argparse
import csv
import json
import re
import subprocess
from pathlib import Path

import numpy as np
from PIL import Image

REPO = Path(__file__).resolve().parents[2]
SIZE = (40, 24)


def extract(video, ffmpeg, destination):
    frames = destination / "frames"
    frames.mkdir()
    log = destination / "ffmpeg.log"
    command = [str(ffmpeg), "-hide_banner", "-nostdin", "-threads", "4", "-i", str(video), "-an",
               "-vf", "select='isnan(prev_selected_t)+gte(t-prev_selected_t,0.4999)',scale=512:288,showinfo",
               "-fps_mode", "passthrough", "-q:v", "4", "-threads", "2", str(frames / "frame-%05d.jpg")]
    with log.open("w", encoding="utf-8") as file:
        subprocess.run(command, stderr=file, check=True)
    index = destination / "frame-index.tsv"
    with index.open("w", encoding="utf-8", newline="") as file:
        writer = csv.writer(file, delimiter="\t")
        writer.writerow(["seconds", "path"])
        for line in log.read_text(encoding="utf-8", errors="replace").splitlines():
            match = re.search(r"showinfo.*? n:\s*(\d+) pts:\s*-?\d+ pts_time:([\d.eE+-]+)", line)
            if match:
                writer.writerow([match[2], f"frames/frame-{int(match[1])+1:05}.jpg"])
    return index


def references():
    folder = REPO / "Assets/Game/Modules/CharacterPresentation/Res/Characters"
    frames, masks, labels = [], [], []
    for path in sorted(folder.glob("*/Effects/cut/*/*_frames/*.png")):
        with Image.open(path) as image:
            pixels = np.asarray(image.convert("RGBA").resize(SIZE, Image.Resampling.BOX), np.float32) / 255
        mask = pixels[:, :, 3] > 0.87
        mask[:2] = False
        mask[-2:] = False
        rgb = pixels[:, :, :3]
        if mask.sum() < 45 or rgb[mask].std(axis=0).mean() < 0.075:
            continue
        frames.append(rgb.ravel())
        masks.append(np.repeat(mask.ravel(), 3))
        labels.append((path.relative_to(folder).parts[0], path.relative_to(folder).as_posix()))
    return np.array(frames, np.float32), np.array(masks, np.float32), labels


def correlations(targets, masks, area, means, centered, norms):
    """Masked Pearson correlation with a color-distance penalty; reject near-solid flashes."""
    sums = targets @ masks.T
    variance = np.maximum((targets * targets) @ masks.T - sums * sums / area, 0)
    dot = targets @ centered.T
    corr = dot / np.maximum(np.sqrt(variance) * norms, 1e-6)
    color_error = np.sqrt(np.maximum((variance + norms**2 - 2 * dot) / area + (sums / area - means)**2, 0))
    return np.where(variance / area > 0.003, np.clip(corr, -1, 1) - 0.45 * color_error, -1)


def compare(index, output, threshold):
    with index.open(encoding="utf-8-sig", newline="") as file:
        samples = [(float(row["seconds"]), index.parent / row["path"]) for row in csv.DictReader(file, delimiter="\t")]
    if not samples:
        raise ValueError(f"No timestamped video frames in {index}")
    rgb, masks, labels = references()
    area = masks.sum(axis=1)
    means = (rgb * masks).sum(axis=1) / area
    centered = (rgb - means[:, None]) * masks
    norms = np.linalg.norm(centered, axis=1)
    best = {}
    all_matches = []
    for pos in range(0, len(samples), 256):
        batch = samples[pos:pos+256]
        pixels = []
        for _, image_path in batch:
            with Image.open(image_path) as image:
                pixels.append(np.asarray(image.convert("RGB").resize(SIZE, Image.Resampling.BOX), np.float32) / 255)
        images = np.array(pixels)
        matches = {}
        for direction, tiles in (("original", images), ("mirrored", images[:, :, ::-1])):
            scores = correlations(tiles.reshape(len(batch), -1), masks, area, means, centered, norms)
            for frame, reference in zip(*np.where(scores >= threshold)):
                hero, path = labels[reference]
                score = float(scores[frame, reference])
                key = (frame, hero)
                if key not in matches or score > matches[key][0]:
                    matches[key] = (score, path, direction)
        for (frame, hero), (score, path, direction) in sorted(matches.items()):
            time = batch[frame][0]
            row = [hero, f"{time:.3f}", f"{score:.4f}", path, direction]
            all_matches.append(row)
            if hero not in best or score > best[hero][0]:
                best[hero] = (score, row)
    heroes = json.loads((REPO / "Config/CharacterAssets/catalog.generated.json").read_text("utf-8"))["characters"]
    for name, rows in (("matches.tsv", all_matches), ("heroes.tsv", [
        best[hero["id"]][1] if hero["id"] in best else [hero["id"], "", "", "", ""] for hero in heroes
    ])):
        with (output / name).open("w", encoding="utf-8", newline="") as file:
            writer = csv.writer(file, delimiter="\t")
            writer.writerow(["hero", "seconds", "score", "reference", "orientation"])
            writer.writerows(rows)
    print(f"Compared {len(samples)} video frames with {len(labels)} source frames; matched {len(best)}/{len(heroes)} heroes.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    source = parser.add_mutually_exclusive_group(required=True)
    source.add_argument("--video", type=Path)
    source.add_argument("--frame-index", type=Path, help="UTF-8 TSV with seconds and image path relative to the index.")
    parser.add_argument("--ffmpeg", default="ffmpeg")
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--threshold", type=float, default=0.90)
    args = parser.parse_args()
    if not 0 < args.threshold <= 1:
        parser.error("--threshold must be in (0, 1].")
    output = args.output.resolve()
    if not output.is_relative_to(REPO / ".artifacts"):
        parser.error("Write local video evidence under the workspace .artifacts directory.")
    output.mkdir(parents=True, exist_ok=True)
    compare(args.frame_index or extract(args.video, args.ffmpeg, output), output, args.threshold)
