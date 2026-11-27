"""Import the four original rounded message-box button skins without touching the source pack."""
import argparse
import json
import re
from pathlib import Path

from PIL import Image
from ImportArenaArt import REPO, SOURCE_FOLDER, digest, read_source, write
from ImportInventoryButtons import meta, FOLDER, TEXT

DEST = REPO / 'Assets/Game/Common/Res/Textures/MessageBoxButtons'
NAMES = ('_btn_blue_r_3', '_btn_blue_r_p_3', '_btn_gold_r_2', '_btn_gold_r_p_2')


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
    assets = []
    for name in NAMES:
        relative = 'img/' + name + '.png'
        raw = read_source(source, relative)
        output = DEST / (name + '.png')
        write(output, raw, args.check)
        with Image.open(output) as image:
            if image.size != (200, 128):
                raise ValueError(f'Unexpected source dimensions: {relative}: {image.size}')
        importer = re.sub(r'(?m)^(\s+textureCompression:) \d+$', r'\1 0', template)
        importer = re.sub(r'(?m)^  userData:.*$', '  userData: AstralOath.ImportOverride', importer)
        # Source cap(x,y,w,h)=(100,64,1,1), texture200x128: left,bottom,right,top.
        importer = re.sub(r'(?m)^  spriteBorder: .*', '  spriteBorder: {x: 100, y: 63, z: 99, w: 64}', importer)
        meta(output, importer, args.check)
        assets.append({'asset': output.relative_to(REPO).as_posix(), 'source': relative,
                       'sourceSha256': digest(raw), 'outputSha256': digest(raw), 'size': [200, 128],
                       'sourceCapXywh': [100, 64, 1, 1], 'unityBorderLbrt': [100, 63, 99, 64]})
    value = {'referenceLayout': {'source': 'wnd/msgbox.csb', 'sha256': digest(read_source(source, 'wnd/msgbox.csb'))},
             'sourceButtonSize': [388, 128], 'sourceNodeScale': [.53, .53], 'canvasDesignScale': 1.5,
             'designButtonSize': [308.46, 101.76], 'pixelsPerUnitMultiplier': 1 / (.53 * 1.5),
             'assets': assets}
    manifest = DEST / 'source-manifest.json'
    write(manifest, (json.dumps(value, ensure_ascii=False, indent=2) + '\n').encode('utf-8'), args.check)
    meta(manifest, TEXT, args.check)
    print(f'Verified {len(assets)} rounded message-box button skins; source pack remains read-only.')


if __name__ == '__main__':
    main()
