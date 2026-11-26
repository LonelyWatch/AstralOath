"""Import only the original enhancement bitmap digits; never modifies the external source."""
import hashlib
import json
from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parents[2]
SOURCE = Path('D:/sevenepic/原始资源/img')
TARGET = ROOT / 'Assets/Game/Common/Res/Textures/EnhancementDigits'
TEMPLATE = ROOT / 'Assets/Game/Common/Res/Textures/_upgrade_num_red.png.meta'

def digest(data):
    return hashlib.sha256(data).hexdigest()


def lossless_meta(text):
    # GameAssetImportRules otherwise replaces per-platform settings on every import.
    # Keep these tiny RGBA numeral faces and shadows at their original 26x34 pixels.
    replacements = [
        (r'(?m)^  userData:.*$', '  userData: AstralOath.ImportOverride'),
        (r'(?m)^  isReadable: \d+$', '  isReadable: 0'),
        (r'(?m)^    enableMipMap: \d+$', '    enableMipMap: 0'),
        (r'(?m)^    filterMode: \d+$', '    filterMode: 1'),
        (r'(?m)^  nPOTScale: \d+$', '  nPOTScale: 0'),
        (r'(?m)^    textureFormat: -?\d+$', '    textureFormat: 4'),  # RGBA32
        (r'(?m)^    textureCompression: \d+$', '    textureCompression: 0'),
        (r'(?m)^    crunchedCompression: \d+$', '    crunchedCompression: 0'),
    ]
    for pattern, replacement in replacements:
        text, count = re.subn(pattern, replacement, text)
        assert count, f'Missing numeral importer field: {pattern}'
    for platform in ['Standalone', 'Android']:
        pattern = (r'(?ms)^  - serializedVersion: \d+\n    buildTarget: ' + platform +
                   r'\n.*?(?=^  - serializedVersion:|^  spriteSheet:)')
        match = re.search(pattern, text)
        assert match, f'Missing numeral platform settings: {platform}'
        block, count = re.subn(r'(?m)^    overridden: \d+$', '    overridden: 1', match[0])
        assert count == 1, f'Missing numeral platform override: {platform}'
        text = text[:match.start()] + block + text[match.end():]
    return text


def main():
    check = '--check' in sys.argv
    records = []
    if not check:
        TARGET.mkdir(parents=True, exist_ok=True)
    for token in [*map(str, range(10)), 'plus']:
        name = f'itxt_num{token}_b.png'
        src, dst = SOURCE / name, TARGET / name
        data = src.read_bytes()
        meta = dst.with_suffix('.png.meta')
        if check:
            assert dst.read_bytes() == data, name
            text = meta.read_text('utf-8-sig')
            assert text == lossless_meta(text), f'Lossless numeral import settings drifted: {name}'
        else:
            dst.write_bytes(data)
            if meta.exists():
                text = meta.read_text('utf-8-sig')
            else:
                text = TEMPLATE.read_text('utf-8-sig')
                text = re.sub(r'(?m)^guid: .*$', 'guid: '+hashlib.md5(('AstralOath.EnhancementDigits/'+name).encode()).hexdigest(), text)
                text = re.sub(r'spriteBorder: \{[^}]*\}', 'spriteBorder: {x: 0, y: 0, z: 0, w: 0}', text)
            meta.write_text(lossless_meta(text), 'utf-8')
        records.append(dict(source=str(src), path=str(dst.relative_to(ROOT)), sha256=digest(data)))
    manifest = dict(reference='unit_item_upgrade.csb n_up/n_num bitmap characters; original +3 81.037s / +15 109.012s, 2026-11-25 recording',
                    transform='Source PNG bytes unchanged; BitmapNumber draws original Sprite quads directly. The hidden compatibility label retains the editor-packed TMP font.', records=records)
    dest = TARGET / 'source-manifest.json'
    if check:
        assert json.loads(dest.read_text('utf-8')) == manifest
    else:
        dest.write_text(json.dumps(manifest, ensure_ascii=False, indent=2)+'\n', 'utf-8')
    print(f'Enhancement bitmap digits: {len(records)} verified' if check else f'Enhancement bitmap digits: {len(records)} imported')

if __name__ == '__main__':
    main()
