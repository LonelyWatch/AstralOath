"""Bake the native ui_touch_def weighted meshes for the common Unity Canvas.

Run from the repository root with the project character-assets Python environment.
The external pack is read-only; sampling scratch stays under .artifacts/ui-feedback.
"""

import argparse
import io
import json
from pathlib import Path
import plistlib
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
FPS = 60
FRAMES = 32
RING_FRAMES = 25


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


def thin_ring(composite, frame):
    # Preserve the initial disc. As it opens, keep the rim radius and tighten its profile.
    width = 1 - .30 * np.clip((frame - 8) / 8, 0, 1)
    if width == 1:
        return composite
    yy, xx = np.indices((CELL, CELL), dtype=np.float32)
    center = (CELL - 1) / 2
    x, y = xx - center, yy - center
    radius = np.maximum(np.hypot(x, y), .001)
    peak = np.argmax(composite[CELL // 2, CELL // 2:, 2]) + .5
    mapped = np.maximum(0, peak + (radius - peak) / width)
    sx = np.clip(center + x * mapped / radius, 0, CELL - 1.001)
    sy = np.clip(center + y * mapped / radius, 0, CELL - 1.001)
    left, top = sx.astype(int), sy.astype(int)
    dx, dy = (sx - left)[..., None], (sy - top)[..., None]
    near = composite[top, left] * (1 - dx) + composite[top, left + 1] * dx
    far = composite[top + 1, left] * (1 - dx) + composite[top + 1, left + 1] * dx
    return near * (1 - dy) + far * dy


def sample_sparks(source):
    primitives = plistlib.loads((source / "effect/ui_touch_def.cfx").read_bytes())["primitive"]
    effect = next(item for item in primitives if item["format"] == "particle")
    emitters = plistlib.loads((source / "effect/ui_touch_def_pati1.particle").read_bytes())["emitters"]
    texture, _ = decode_sct((source / emitters[0]["textureFileName"]).read_bytes())
    rng = np.random.default_rng(324)
    sparks = []
    for emitter in sorted(emitters, key=lambda emitter: emitter["particleLifespan"]):
        gradient = np.array([[float(value) for value in stop.split(",")]
                             for stop in emitter["colorBlends"][0].split(";")])
        for index in range(int(emitter["emissionRate"])):
            start = rng.uniform(8, 25)
            end = min(88, float(effect["scale"]) * rng.uniform(.7, 1) * emitter["minRadius"])
            lifetime = emitter["particleLifespan"]
            life = lifetime + rng.uniform(-.12, .61) * emitter["particleLifespanVariance"]
            size = rng.uniform(7, 16) * emitter["particleLifespan"] / .4
            sparks.append((index * .004, start, end, life, size, rng.uniform(0, 2 * np.pi), gradient))
    return sparks, texture.convert("RGBA"), float(effect["opacity"])


def add_sparks(composite, frame, sparks, texture, opacity):
    # Frame samples vary in angle, lifetime, size and velocity. Keep the brightest early dots
    # on the disc, then carry them beyond the rim using the source additive blend and color ramp.
    elapsed = frame / FPS
    for delay, start, end, life, size, angle, gradient in sparks:
        time = (elapsed - delay) / life
        if not 0 <= time < 1:
            continue
        radius = start + (end - start) * time ** 1.25
        side = max(5, round(size * (1 - .65 * time)))
        middle = (CELL - 1) / 2
        left = round(middle + np.cos(angle) * radius - side / 2)
        top = round(middle + np.sin(angle) * radius - side / 2)
        spot = np.array(texture.resize((side, side), Image.Resampling.LANCZOS), np.float32) / 255
        tint = np.array([np.interp(time, gradient[:, 0], gradient[:, col]) for col in range(2, 5)])
        alpha = np.interp(time, gradient[:, 0], gradient[:, 1])
        # Keep sparse particles visible after sampling into the linear UI target.
        emission = (spot[:, :, 3:4] * alpha * opacity) ** .35
        if elapsed > RING_FRAMES / FPS:
            emission *= 1.45
        composite[top:top + side, left:left + side, :3] += spot[:, :, :3] * tint * emission


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
    if len(layers) != RING_FRAMES:
        raise ValueError(f"Unexpected native frame count: {len(layers)}")
    texture = np.asarray(page, np.float32) / 255
    sheet = Image.new("RGBA", (CELL * COLUMNS, CELL * ROWS))
    sparks, particle_texture, particle_opacity = sample_sparks(source)
    for i in range(FRAMES):
        frame = layers[i] if i < RING_FRAMES else ()
        side = CELL * SUPERSAMPLE
        composite = np.zeros((side, side, 4), np.float32)
        for layer in frame:
            sampled = draw(layer, texture)
            if layer["Additive"]:
                composite[:, :, :3] += sampled[:, :, :3]
            else:
                composite = sampled + composite * (1 - sampled[:, :, 3:4])
        composite = composite.reshape(CELL, SUPERSAMPLE, CELL, SUPERSAMPLE, 4).mean(axis=(1, 3))
        if i < RING_FRAMES:
            composite = thin_ring(composite, i)
        add_sparks(composite, i, sparks, particle_texture, particle_opacity)
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
    print(f"Verified {FRAMES} ring and particle frames at {FPS} fps ({len(result)} bytes).")


if __name__ == "__main__":
    main()
