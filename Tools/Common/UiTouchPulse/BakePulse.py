"""Bake the native ui_touch_def weighted meshes for the common Unity Canvas.

Run from the repository root with the project character-assets Python environment.
The external pack is read-only; sampling scratch stays under .artifacts/ui-feedback.
"""

import argparse
import io
import json
from pathlib import Path
import subprocess
import sys

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT / "Tools/CharacterAssets"))
from convert import decode_scsp, decode_sct

CELL = 224
COLUMNS, ROWS = 8, 4
SUPERSAMPLE = 2
OPACITY = .30


def draw(layer, atlas):
    side = CELL * SUPERSAMPLE
    positions = np.array(layer["Positions"], np.float32).reshape(-1, 2)
    positions = positions * np.array([SUPERSAMPLE, -SUPERSAMPLE]) + side / 2
    uv = np.array(layer["UVs"], np.float32).reshape(-1, 2)
    sampled = np.zeros((side, side, 4), np.float32)
    for indices in np.array(layer["Triangles"]).reshape(-1, 3):
        a, b, c = positions[indices]
        low_x, low_y = np.maximum(0, np.floor(np.min([a, b, c], axis=0)).astype(int))
        high_x, high_y = np.minimum(side, np.ceil(np.max([a, b, c], axis=0)).astype(int))
        if high_x <= low_x or high_y <= low_y:
            continue
        ab, ac = b - a, c - a
        denom = ab[0] * ac[1] - ab[1] * ac[0]
        if abs(denom) < 1e-8:
            continue
        y, x = np.ogrid[low_y:high_y, low_x:high_x]
        px, py = x + .5 - a[0], y + .5 - a[1]
        wb = (px * ac[1] - py * ac[0]) / denom
        wc = (ab[0] * py - ab[1] * px) / denom
        wa = 1 - wb - wc
        mask = (wa >= -1e-7) & (wb >= -1e-7) & (wc >= -1e-7)
        if not mask.any():
            continue
        coords = np.column_stack((wa[mask], wb[mask], wc[mask])) @ uv[indices]
        xy = coords * np.array([atlas.shape[1], atlas.shape[0]]) - .5
        xy = np.maximum(0, np.minimum([atlas.shape[1] - 1.001, atlas.shape[0] - 1.001], xy))
        ix, iy = np.floor(xy).astype(int).T
        dx, dy = (xy - np.floor(xy)).T[:, :, None]
        row0 = atlas[iy, ix] * (1 - dx) + atlas[iy, ix + 1] * dx
        row1 = atlas[iy + 1, ix] * (1 - dx) + atlas[iy + 1, ix + 1] * dx
        sampled[low_y:high_y, low_x:high_x][mask] = row0 * (1 - dy) + row1 * dy
    r, g, b, a = layer["Color"]
    opacity = a * OPACITY
    sampled *= [r * opacity, g * opacity, b * opacity, opacity]
    return sampled


def bake(scratch, source):
    model = source / "effect/ui_touch_def.scsp"
    atlas = source / "effect/ui_touch_def.atlas"
    page, _ = decode_sct((source / "effect/ui_touch_def.sct").read_bytes())
    decoded, _ = decode_scsp(model.read_bytes())
    skeleton = scratch / "spine.json"
    samples = scratch / "mesh-samples.json"
    skeleton.write_text(json.dumps(decoded), encoding="utf-8")
    dotnet = ROOT / ".tools/dotnet/dotnet.exe"
    project = Path(__file__).with_name("UiTouchPulse.csproj")
    command = [str(dotnet) if dotnet.is_file() else "dotnet", "run", "--project", str(project)]
    subprocess.run([*command, "--", str(skeleton), str(atlas), str(samples)], check=True)
    layers = json.loads(samples.read_text(encoding="utf-8"))
    if len(layers) != 25:
        raise ValueError(f"Unexpected native frame count: {len(layers)}")
    texture = np.asarray(page, np.float32) / 255
    sheet = Image.new("RGBA", (CELL * COLUMNS, CELL * ROWS))
    for i, frame in enumerate(layers):
        side = CELL * SUPERSAMPLE
        composite = np.zeros((side, side, 4), np.float32)
        for layer in frame:
            sampled = draw(layer, texture)
            if layer["Additive"]:
                composite[:, :, :3] += sampled[:, :, :3]
            else:
                composite = sampled + composite * (1 - sampled[:, :, 3:4])
        composite = composite.reshape(CELL, SUPERSAMPLE, CELL, SUPERSAMPLE, 4).mean(axis=(1, 3))
        pixels = np.rint(np.clip(composite, 0, 1) * 255).astype(np.uint8)
        if pixels[[0, -1], :, :].max() or pixels[:, [0, -1], :].max():
            raise ValueError(f"Frame {i} clips its transparent cells")
        sheet.paste(Image.fromarray(pixels), (i % COLUMNS * CELL, i // COLUMNS * CELL))
    buffer = io.BytesIO()
    sheet.save(buffer, format="PNG")
    return buffer.getvalue()


def main():
    argument = argparse.ArgumentParser()
    argument.add_argument("--source", type=Path, default=Path("D:/sevenepic/原始资源"))
    argument.add_argument("--check", action="store_true")
    args = argument.parse_args()
    scratch = ROOT / ".artifacts/ui-feedback"
    scratch.mkdir(parents=True, exist_ok=True)
    result = bake(scratch, args.source)
    output = ROOT / "Assets/Game/Common/Res/Textures/UiTouchPulse.png"
    if args.check:
        if result != output.read_bytes():
            raise SystemExit("The native pointer atlas differs from a clean bake.")
    else:
        output.write_bytes(result)
    print(f"Verified 25 authored frames at 60 fps ({len(result)} bytes).")


if __name__ == "__main__":
    main()
