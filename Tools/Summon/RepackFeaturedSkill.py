"""Repack the summon preview from a completed SummonFeaturedSkillCapture run."""
import argparse
import hashlib
import io
import json
from pathlib import Path
from tempfile import TemporaryDirectory
from shutil import copyfile

from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
TARGET = ROOT / "Assets/Game/Modules/Summon/Res/Presentation/IseriaSkillLoop"


def padded(image, width, height, gutter):
    tile = Image.new("RGB", (width + 2 * gutter, height + 2 * gutter))
    tile.paste(image, (gutter, gutter))
    tile.paste(image.crop((0, 0, 1, height)).resize((gutter, height)), (0, gutter))
    tile.paste(image.crop((width - 1, 0, width, height)).resize((gutter, height)), (width + gutter, gutter))
    tile.paste(tile.crop((0, gutter, tile.width, gutter + 1)).resize((tile.width, gutter)), (0, 0))
    tile.paste(tile.crop((0, gutter + height - 1, tile.width, gutter + height)).resize((tile.width, gutter)),
               (0, gutter + height))
    return tile


def repack(frames):
    model = (frames / "target.txt").read_text().strip()
    if model != "preview.slime":
        raise ValueError("Expected a completed skill capture against the original slime model")
    manifest = TARGET / "loop.json"
    spec = json.loads(manifest.read_text(encoding="utf-8"))
    width, height, gutter = spec["width"], spec["height"], spec["gutterPixels"]
    columns, rows = spec["columns"], spec["rows"]
    cell_width, cell_height = width + 2 * gutter, height + 2 * gutter
    digest = hashlib.sha256()
    with TemporaryDirectory(dir=frames.parent) as directory:
        staging = Path(directory)
        for page, record in enumerate(spec["atlasHashes"]):
            sheet = Image.new("RGB", (columns * cell_width, rows * cell_height))
            for tile in range(columns * rows):
                index = page * columns * rows + tile
                if index >= spec["frameCount"]:
                    break
                time = spec["sourceStartSeconds"] + index / spec["framesPerSecond"]
                n = round(time * spec["sourceFramesPerSecond"])
                data = (frames / f"frame-{n:04}.png").read_bytes()
                digest.update(data)
                with Image.open(io.BytesIO(data)) as source:
                    if source.size != (960, 540):
                        raise ValueError(f"Unexpected frame size at {n}")
                    image = source.convert("RGB").resize((width, height), Image.Resampling.LANCZOS)
                sheet.paste(padded(image, width, height, gutter), (tile % columns * cell_width, tile // columns * cell_height))
            dest = staging / record["path"]
            sheet.save(dest, quality=90, optimize=True, subsampling=0)
            record["sha256"] = hashlib.sha256(dest.read_bytes()).hexdigest()
        spec["sourceTargetModel"] = model
        spec["sourceFramesSha256"] = digest.hexdigest()
        (staging / "loop.json").write_text(json.dumps(spec, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
        for name in [*spec["pages"], "loop.json"]:
            copyfile(staging / name, TARGET / name)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("frames", type=Path)
    repack(parser.parse_args().frames)
