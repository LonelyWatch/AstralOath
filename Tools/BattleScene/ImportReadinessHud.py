"""Copy the small set of original readiness details sprites; source stays read-only."""
import argparse
import hashlib
import json
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
DEST = REPO / 'Assets/Game/Modules/BattleScene/Res/Textures/Hud/Readiness'
FILES = ['img/_grow_s_white.png', 'img/_btn_small_white.png', 'img/cm_icon_etctime_b2.png',
         'buff/stic_att_up.png', 'buff/stic_def_up.png', 'buff/stic_debuf_impossible.png',
         'buff/stic_protect.png', 'buff/stic_stun.png',
         'face/m0001_s.png', 'face/m0002_s.png', 'face/m0003_s.png', 'face/m0005_s.png']
LAYOUTS = ['wnd/inbattle_order_detail.csb', 'wnd/inbattle_detail.csb', 'wnd/hud_face.csb']

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', type=Path, default=Path('D:/sevenepic/原始资源'))
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    entries = []
    for relative in FILES + LAYOUTS:
        source = args.source / relative
        data = source.read_bytes()
        item = {'source': '原始资源/' + relative, 'sha256': hashlib.sha256(data).hexdigest()}
        if relative in FILES:
            item['asset'] = Path(relative).name
            target = DEST / item['asset']
            if args.check:
                assert target.read_bytes() == data, str(target)
            else:
                DEST.mkdir(parents=True, exist_ok=True)
                target.write_bytes(data)
        entries.append(item)
    manifest = {'sourceRoot': 'D:/sevenepic', 'transfer': 'Byte-identical selected PNGs; CSB files are evidence only, never imported.', 'assets': entries,
                'previewSlimeFaces': {'basis': 'Visual comparison of local slime skin attachments and original face PNGs; not decoded monster database records.',
                                      'fire': 'm0001_s.png', 'ice': 'm0002_s.png', 'wind': 'm0003_s.png', 'dark': 'm0005_s.png'}}
    target = DEST / 'source.json'
    if args.check:
        assert json.loads(target.read_text(encoding='utf-8')) == manifest
    else:
        target.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print('Verified' if args.check else 'Imported', len(FILES), 'readiness sprites and', len(LAYOUTS), 'layout hashes.')

if __name__ == '__main__':
    main()
