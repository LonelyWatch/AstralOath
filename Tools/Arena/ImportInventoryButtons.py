"""Import only the shared inventory button styles from the read-only source pack."""
import argparse
import io
import json
import re
from pathlib import Path

from PIL import Image
from ImportArenaArt import REPO, SOURCE_FOLDER, digest, guid, read_source, write
from ImportSummoningArt import linear_black_overlay

DEST = REPO / 'Assets/Game/Common/Res/Textures/InventoryButtons'
TEXT = 'TextScriptImporter:\n  externalObjects: {}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n'
FOLDER = 'folderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n'


def meta(path, body, check):
    target = path.with_name(path.name + '.meta')
    expected = f'fileFormatVersion: 2\nguid: {guid(path)}\n{body}'
    if not target.exists():
        if check:
            raise ValueError(f'Missing metadata: {target}')
        target.write_text(expected, encoding='utf-8')
    elif check:
        content = target.read_text(encoding='utf-8-sig')
        border = re.search(r'(?m)^  spriteBorder: .*', body)
        if border and border[0] not in content:
            raise ValueError(f'Sprite border differs: {target}')
        if border and any(v != '0' for v in re.findall(r'(?m)^\s+textureCompression: (\d+)$', content)):
            raise ValueError(f'UI texture compression differs: {target}')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', type=Path, default=Path('D:/sevenepic'))
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    source = (args.source / SOURCE_FOLDER).resolve(strict=True)
    if not args.check:
        DEST.mkdir(parents=True, exist_ok=True)
    meta(DEST, FOLDER, args.check)
    template = (REPO / 'Assets/Game/Modules/BattleScene/Res/Textures/Hud/Skills/sk_c1001_1.png.meta').read_text(encoding='utf-8-sig').splitlines()[2:]
    template = '\n'.join(template) + '\n'
    entries = []
    # Unity borders are left/bottom/right/top, derived from the cited source cap rects.
    for name, border in [('_btn_blue', (45, 14, 45, 12)), ('_btn_blue_p', (45, 14, 45, 12)),
                         ('_btn_green', (40, 11, 40, 11)), ('_btn_green_p', (40, 11, 40, 11)),
                         ('_btn_gold', (40, 11, 40, 11)), ('_btn_gold_p', (40, 11, 40, 11)),
                         ('_btn_small_black', (14, 16, 14, 12)), ('_btn_small_black_p', (14, 16, 14, 12)),
                         ('_btn_small_white', (14, 16, 14, 12)), ('_btn_small_white_p', (14, 16, 14, 12)),
                         ('cost_plate', (14, 14, 14, 14)), ('token_gold', (0, 0, 0, 0))]:
        relative = 'item/token_gold.png' if name == 'token_gold' else 'img/' + ('_btn_small_black_p' if name == 'cost_plate' else name) + '.png'
        raw = read_source(source, relative)
        data = raw
        conversion = None
        if name == 'cost_plate':
            with Image.open(io.BytesIO(raw)) as image:
                image = image.convert('RGBA')
                image.putalpha(image.getchannel('A').point(lambda a: round(a * 127 / 255)))
                data = linear_black_overlay(image)
            conversion = 'Source node opacity127 integrated into alpha, then sRGB black transmission converted to linear alpha.'
        output = DEST / (name + '.png')
        write(output, data, args.check)
        importer = re.sub(r'(?m)^(\s+textureCompression:) \d+$', r'\1 0', template)
        importer = re.sub(r'(?m)^  userData:.*$', '  userData: AstralOath.ImportOverride', importer)
        x, y, z, w = border
        importer = re.sub(r'(?m)^  spriteBorder: .*', f'  spriteBorder: {{x: {x}, y: {y}, z: {z}, w: {w}}}', importer)
        meta(output, importer, args.check)
        record = {'asset': output.relative_to(REPO).as_posix(), 'source': relative, 'sourceSha256': digest(raw),
                  'outputSha256': digest(data), 'unityBorderLbrt': list(border)}
        if conversion:
            record['conversion'] = conversion
        entries.append(record)
    manifest = DEST / 'source-manifest.json'
    layouts = ['inventory_equip_popup', 'unit_item_upgrade', 'equip_belt']
    value = {'assets': entries, 'referenceLayouts': [
        {'source': 'wnd/' + name + '.csb', 'sha256': digest(read_source(source, 'wnd/' + name + '.csb'))} for name in layouts]}
    write(manifest, (json.dumps(value, ensure_ascii=False, indent=2) + '\n').encode('utf-8'), args.check)
    meta(manifest, TEXT, args.check)
    print(f'Verified {len(entries)} shared button textures; original pack remains read-only.')


if __name__ == '__main__':
    main()
