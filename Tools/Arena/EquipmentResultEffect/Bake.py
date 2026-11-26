"""Bake the original itemupgrade_statup Spine curves for an existing Canvas PMA shader."""
import argparse
import io
import json
import re
from pathlib import Path
import subprocess
import sys

import numpy as np
from PIL import Image

REPO = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(REPO / 'Tools/CharacterAssets'))
sys.path.insert(0, str(REPO / 'Tools/Arena'))
from convert import decode_scsp, decode_sct
from ImportArenaArt import digest, guid, write

WIDTH, HEIGHT, COLUMNS, ROWS = 512, 192, 8, 4
DEST = REPO / 'Assets/Game/Modules/Home/Res/Textures/Inventory/EquipmentEnhancement'


def render(layers, atlas):
    target = np.zeros((HEIGHT, WIDTH, 4), np.float32)
    for layer in layers:
        positions = np.asarray(layer['Positions'], np.float32).reshape(-1, 2) * [.5, -.5] + [256, 128]
        uv = np.asarray(layer['UVs'], np.float32).reshape(-1, 2)
        sampled = np.zeros_like(target)
        for indices in np.asarray(layer['Triangles']).reshape(-1, 3):
            a, b, c = positions[indices]
            lx, ly = np.maximum(0, np.floor(np.min([a, b, c], axis=0)).astype(int))
            hx, hy = np.minimum([WIDTH, HEIGHT], np.ceil(np.max([a, b, c], axis=0)).astype(int))
            if hx <= lx or hy <= ly:
                continue
            ab, ac = b - a, c - a
            determinant = ab[0] * ac[1] - ab[1] * ac[0]
            if abs(determinant) < 1e-8:
                continue
            yy, xx = np.ogrid[ly:hy, lx:hx]
            px, py = xx + .5 - a[0], yy + .5 - a[1]
            wb = (px * ac[1] - py * ac[0]) / determinant
            wc = (ab[0] * py - ab[1] * px) / determinant
            wa = 1 - wb - wc
            mask = (wa >= -1e-7) & (wb >= -1e-7) & (wc >= -1e-7)
            if not mask.any():
                continue
            coords = np.column_stack([wa[mask], wb[mask], wc[mask]]) @ uv[indices]
            xy = np.maximum(0, np.minimum([atlas.shape[1] - 1.001, atlas.shape[0] - 1.001], coords * [atlas.shape[1], atlas.shape[0]] - .5))
            ix, iy = np.floor(xy).astype(int).T
            dx, dy = (xy - np.floor(xy)).T[:, :, None]
            row0 = atlas[iy, ix] * (1 - dx) + atlas[iy, ix + 1] * dx
            row1 = atlas[iy + 1, ix] * (1 - dx) + atlas[iy + 1, ix + 1] * dx
            sampled[ly:hy, lx:hx][mask] = row0 * (1 - dy) + row1 * dy
        r, g, b, a = layer['Color']
        sampled *= [r * a, g * a, b * a, a]
        if layer['Additive']:
            target[:, :, :3] += sampled[:, :, :3]
        else:
            target = sampled + target * (1 - sampled[:, :, 3:4])
    return Image.fromarray(np.rint(np.clip(target, 0, 1) * 255).astype(np.uint8))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', type=Path, default=Path('D:/sevenepic/原始资源'))
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    scratch = REPO / '.artifacts/badge-followup/buttons/result-effect-bake'
    scratch.mkdir(parents=True, exist_ok=True)
    raw = {suffix: (args.source / ('effect/itemupgrade_statup.' + suffix)).read_bytes() for suffix in ['scsp', 'atlas', 'sct']}
    skeleton, report = decode_scsp(raw['scsp'])
    skeletonPath = scratch / 'skeleton.json'
    skeletonPath.write_text(json.dumps(skeleton), encoding='utf-8')
    atlasPath = scratch / 'source.atlas'
    atlasPath.write_bytes(raw['atlas'])
    project = Path(__file__).with_name('EquipmentResultEffect.csproj')
    dotnet = REPO / '.tools/dotnet/dotnet.exe'
    subprocess.run([str(dotnet), 'build', str(project), '-o', str(scratch / 'bin'),
                    '-p:BaseIntermediateOutputPath=' + str(scratch / 'obj') + '/', '--nologo'], check=True)
    samples = scratch / 'samples.json'
    subprocess.run([str(dotnet), str(scratch / 'bin/EquipmentResultEffect.dll'), str(skeletonPath), str(atlasPath), str(samples)], check=True)
    value = json.loads(samples.read_text(encoding='utf-8'))
    if len(value['frames']) != 32:
        raise ValueError('Original statup duration/frame count changed: ' + str(len(value['frames'])))
    texture, _ = decode_sct(raw['sct'])
    atlas = np.asarray(texture, np.float32) / 255
    sheet = Image.new('RGBA', (WIDTH * COLUMNS, HEIGHT * ROWS))
    for i, layers in enumerate(value['frames']):
        picture = render(layers, atlas)
        pixels = np.asarray(picture)
        if any(edge.max() for edge in [pixels[0], pixels[-1], pixels[:, 0], pixels[:, -1]]):
            raise ValueError(f'Native result frame{i} clips its cell; expand the cell before importing.')
        if i == len(value['frames']) - 1 and pixels.max():
            raise ValueError('Native result effect does not finish transparent.')
        sheet.paste(picture, (i % COLUMNS * WIDTH, i // COLUMNS * HEIGHT))
    output = io.BytesIO(); sheet.save(output, format='PNG'); data = output.getvalue()
    path = DEST / 'ResultSweep.png'
    write(path, data, args.check)
    template = (REPO / 'Assets/Game/Common/Res/Textures/UiTouchPulse.png.meta').read_text(encoding='utf-8-sig').splitlines()[2:]
    importer = '\n'.join(template) + '\n'
    importer = re.sub(r'(?m)^(\s+maxTextureSize:) \d+$', r'\1 4096', importer)
    importer = re.sub(r'(?m)^(\s+textureCompression:) \d+$', r'\1 0', importer)
    importer = re.sub(r'(?m)^  userData:.*$', '  userData: AstralOath.ImportOverride', importer)
    meta = path.with_name(path.name + '.meta')
    if args.check:
        existing = meta.read_text(encoding='utf-8-sig')
        if 'sRGBTexture: 0' not in existing or 'maxTextureSize: 4096' not in existing or any(
                n != '0' for n in re.findall(r'(?m)^\s+textureCompression: (\d+)$', existing)):
            raise ValueError('Result sweep import would alter the native frame atlas.')
    else:
        identifier = re.search(r'(?m)^guid: ([a-f0-9]+)$', meta.read_text(encoding='utf-8-sig'))[1] if meta.exists() else guid(path)
        meta.write_text('fileFormatVersion: 2\nguid: ' + identifier + '\n' + importer, encoding='utf-8')
    manifest = {'source': 'effect/itemupgrade_statup', 'sourceSha256': {k: digest(v) for k, v in raw.items()},
                'outputSha256': digest(data), 'fps': value['fps'], 'sourceDuration': value['duration'],
                'frames': 32, 'columns': COLUMNS, 'rows': ROWS, 'cellPixels': [WIDTH, HEIGHT], 'scale': .5,
                'blend': 'RGB contains PMA normal layers plus additive energy; alpha contains only normal layer coverage. Reuses Common UiTouchPulse material.',
                'referenceBoundary': 'Blue flame/wide ribbon/purple tail match observed 79.554 and 92.104s; source Lua invocation unavailable. Source curves sampled at1x; no numeric interpolation.',
                'curveSemantics': report.get('curveSemantics')}
    manifestPath = DEST / 'result-sweep-source.json'
    write(manifestPath, (json.dumps(manifest, ensure_ascii=False, indent=2) + '\n').encode('utf-8'), args.check)
    manifestMeta = manifestPath.with_name(manifestPath.name + '.meta')
    if not manifestMeta.exists() and not args.check:
        manifestMeta.write_text('fileFormatVersion: 2\nguid: ' + guid(manifestPath) + '\nTextScriptImporter:\n  externalObjects: {}\n', encoding='utf-8')
    print(f'Verified {len(value["frames"])} native result frames at30fps, duration{value["duration"]}s.')


if __name__ == '__main__':
    main()
