"""Restore the original battle combo sprites and verify the damage bitmap sources."""
import argparse
import hashlib
import json
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
HUD = REPO / 'Assets/Game/Modules/BattleScene/Res/Textures/Hud'
DEST = HUD / 'Damage'
COMBO = [f'game_eff_dmg_p{number}.png' for number in range(10)] + ['game_eff_dmg_hits.png', 'ef_blood03.png']
SOURCES = {f'img/{name}': DEST / name for name in COMBO}
SOURCES.update({
    'font/damage.fnt': HUD / 'Fonts/damage.fnt.txt',
    'font/damage_0.png': HUD / 'Fonts/damage_0.png',
    'wnd/combo.csb': None,
})


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', type=Path, default=Path('D:/sevenepic/原始资源'))
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    manifest = {
        'sourceRoot': 'D:/sevenepic',
        'transfer': 'Byte-identical PNG and BMFont copies. combo.csb is layout evidence only.',
        'comboLayout': {
            'layerSize': [320, 100],
            'tens': [90, 42],
            'ones': [130, 42],
            'caption': [215, 42],
            'backdrop': [205, 50],
            'pivot': [0.5, 0.5],
            'timelines': 0,
        },
        'assets': [],
    }
    for name, target in SOURCES.items():
        data = (args.source / name).read_bytes()
        row = {'source': f'原始资源/{name}', 'sha256': hashlib.sha256(data).hexdigest()}
        if target is not None:
            row['asset'] = target.relative_to(HUD).as_posix()
            if args.check:
                if target.read_bytes() != data:
                    raise ValueError(f'Original battle feedback asset mismatch: {target}')
            else:
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_bytes(data)
        manifest['assets'].append(row)
    target = DEST / 'source.json'
    if args.check:
        if json.loads(target.read_text(encoding='utf-8')) != manifest:
            raise ValueError(f'Original battle feedback manifest mismatch: {target}')
    else:
        target.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print('Verified' if args.check else 'Imported', len(COMBO), 'combo sprites, 2 bitmap-font files and 1 layout hash.')


if __name__ == '__main__':
    main()
