"""Derive only the collapsed lobby's source-preserving Linear coverage variant.

The expanded drawer and the authorized read-only source remain untouched.
"""
from pathlib import Path
import argparse
import hashlib
import json
import re
import uuid
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[2]
SOURCE = Path('D:/sevenepic/原始资源/img/renew_glow_quick.png')
OUT = ROOT / 'Assets/Game/Modules/Home/Res/Textures/Inventory/More'
NAME = 'renew_glow_quick_collapsed_linear.png'


def linear(value):
    return value / 12.92 if value <= .04045 else ((value + .055) / 1.055) ** 2.4


def srgb(value):
    return 12.92 * value if value <= .0031308 else 1.055 * value ** (1 / 2.4) - .055


def smoothstep(low, high, value):
    t = max(0, min(1, (value - low) / (high - low)))
    return t * t * (3 - 2 * t)


def coverage_weight(x, rgba):
    r, g, b, a = rgba
    # 461.484 source pixels are visible; x462+ is outside the collapsed viewport.
    if x >= 462 or a in (0, 255) or not r <= g <= b:
        return 0
    # Measured dark backing ends at maxRGB76/spread30 in this visible source crop.
    # Feather before bright gray ornaments / colored art; do not introduce a hard
    # palette cutoff into the authored continuous gradient. Bright cores >=112
    # and colors with spread>=48 keep their original complete RGBA.
    return (1 - smoothstep(76, 112, b)) * (1 - smoothstep(30, 48, b - r))


def derive(source):
    result = source.copy()
    mask = Image.new('L', source.size)
    originals = list(source.getdata())
    pixels = []
    weights = []
    changed = 0
    for index, rgba in enumerate(originals):
        r, g, b, a = rgba
        weight = coverage_weight(index % source.width, rgba)
        gray = (r + g + b) / (3 * 255)
        converted = (1 - linear(1 - a / 255 + a / 255 * gray)) / (1 - linear(gray)) if weight else a / 255
        new_a = round(max(0, min(1, a / 255 + weight * (converted - a / 255))) * 255)
        pixels.append((r, g, b, new_a))
        weights.append(round(weight * 255))
        changed += new_a != a
    result.putdata(pixels)
    mask.putdata(weights)
    return result, mask, changed


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def white_pixel(rgba, unity):
    r, g, b, a = rgba
    return [round((srgb(1 - a / 255 + a / 255 * linear(c / 255)) if unity else
                   1 - a / 255 + a / 255 * c / 255) * 255, 3) for c in (r, g, b)]


def source_meta(destination):
    target = Path(str(destination) + '.meta')
    template = (OUT / 'renew_glow_quick.png.meta').read_text(encoding='utf-8')
    guid = re.search(r'^guid: (.+)$', target.read_text(encoding='utf-8'), re.M)[1] if target.exists() else uuid.uuid5(
        uuid.NAMESPACE_URL, 'AstralOath/' + destination.relative_to(ROOT).as_posix()).hex
    text = re.sub(r'^guid: .*$', 'guid: ' + guid, template, flags=re.M)
    text = re.sub(r'^    textureFormat: -1$', '    textureFormat: 4', text, flags=re.M)
    target.write_text(text, encoding='utf-8', newline='\n')


def evidence(source, result, mask, destination, record):
    destination = destination.resolve()
    allowed = (ROOT / '.artifacts/home-social-visual-fix').resolve()
    if destination != allowed and allowed not in destination.parents:
        raise ValueError('Evidence must remain below .artifacts/home-social-visual-fix.')
    destination.mkdir(parents=True, exist_ok=True)
    if any(destination.iterdir()):
        raise FileExistsError('Keep existing visual evidence; use a new empty output directory.')
    mask.save(destination / 'coverage-mask.png')
    diff = Image.new('L', source.size)
    changes = [out[3] - src[3] for src, out in zip(source.getdata(), result.getdata())]
    diff.putdata([min(255, delta * 4) for delta in changes])
    diff.save(destination / 'alpha-difference-x4.png')
    crop = (0, 0, 462, 720)
    panels = []
    for caption, image, unity in [('Source sRGB over white', source, False), ('Before: Unity Linear over white', source, True), ('After: Unity Linear over white', result, True)]:
        panel = Image.new('RGB', (462, 748), '#191d27')
        pixels = [tuple(round(c) for c in white_pixel(v, unity)) for v in image.crop(crop).getdata()]
        preview = Image.new('RGB', (462, 720)); preview.putdata(pixels)
        panel.paste(preview, (0, 28)); ImageDraw.Draw(panel).text((7, 8), caption, fill='white')
        panels.append(panel)
    contact = Image.new('RGB', (462 * 3, 748))
    for i, panel in enumerate(panels): contact.paste(panel, (i * 462, 0))
    contact.save(destination / 'white-reference-comparison.png')
    # A generated palette boundary must not add an edge where the actual source
    # has a smooth <=2/255 color+alpha step. Large source ornament edges are
    # reported separately rather than falsely classified as a new contour.
    source_pixels = list(source.getdata()); width = source.width
    smooth_max = {'delta': 0}; all_max = {'delta': 0}
    for y in range(source.height):
        for x in range(461):
            index = y * width + x
            neighbors = [(x + 1, y)] + ([(x, y + 1)] if y + 1 < source.height else [])
            for nx, ny in neighbors:
                other = ny * width + nx
                delta = abs(changes[index] - changes[other])
                value = {'delta': delta, 'a': [x, y], 'b': [nx, ny], 'sourceA': source_pixels[index], 'sourceB': source_pixels[other]}
                if delta > all_max['delta']: all_max = value
                if max(abs(a - b) for a, b in zip(source_pixels[index], source_pixels[other])) <= 2 and delta > smooth_max['delta']:
                    smooth_max = value
    record.update(boundaryAudit={'maxCorrectionStepOnSmoothSource': smooth_max, 'maxCorrectionStepAnySourceEdge': all_max,
                                 'interpretation': 'Brightness76 and spread30 have smoothstep feather, no hard internal palette cutoff. Spatial cutoff x462 lies beyond visible461.484. Actual rendered capture still required.'})
    (destination / 'coverage-audit.json').write_text(json.dumps(record, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')


def main():
    parser = argparse.ArgumentParser(); parser.add_argument('--evidence-dir'); args = parser.parse_args()
    source = Image.open(SOURCE).convert('RGBA'); result, mask, changed = derive(source)
    target = OUT / NAME; result.save(target); source_meta(target)
    samples = []
    for xy in [(350, 360), (430, 544), (438, 544)]:
        original = source.getpixel(xy); output = result.getpixel(xy)
        samples.append(dict(xy=xy, sourceRgba=original, outputRgba=output, sourceSrgbOverWhite=white_pixel(original, False),
                            beforeLinearOverWhite=white_pixel(original, True), afterLinearOverWhite=white_pixel(output, True)))
    record = dict(source=SOURCE.as_posix(), sourceSha256=digest(SOURCE), asset=target.relative_to(ROOT).as_posix(),
                  outputSha256=digest(target), size=source.size, changedAlphaPixels=changed, rgbChangedPixels=0,
                  mask='x<462; 0<a<255; R<=G<=B; weight=(1-smoothstep(76,112,B))*(1-smoothstep(30,48,B-R)). Otherwise weight0.',
                  maskBasis='Visible source crop is461.484px; measured blue-gray backing maxRGB76/spread30. Feather to112/48 protects bright-gray ornament cores and colored art without hard palette contours. Offscreen x462+ unchanged.',
                  conversion='g=(R+G+B)/(3*255); aLinear=(1-sRGBtoLinear(1-a+a*g))/(1-sRGBtoLinear(g)); outputAlpha=round(255*lerp(a,aLinear,weight)). Source RGB always unchanged.',
                  limits='White-reference neutral-gray contrast calibration, not exact all-colored-background blend equivalence. Source brighter ornaments are retained; antialiased transition pixels use continuous weights. Requires real visual follow-up; no Native or audio pass claim.', samples=samples)
    manifest_path = OUT / 'source-manifest.json'
    manifest = json.loads(manifest_path.read_text(encoding='utf-8'))
    manifest['collapsedLobbyNavigation']['coverageVariant'] = record
    manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    if args.evidence_dir: evidence(source, result, mask, Path(args.evidence_dir), record)
    print(json.dumps(dict(asset=record['asset'], changedAlphaPixels=changed, rgbChangedPixels=0, samples=samples), ensure_ascii=False))


if __name__ == '__main__':
    main()
