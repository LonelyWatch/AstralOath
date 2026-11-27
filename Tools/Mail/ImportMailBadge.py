"""Import only the original HUD count font and its canonical scale-nine border.

The source archive is read-only. Existing Unity GUIDs are retained on every run.
The TMP digit-only atlas is baked by ArenaUiBuilder.MailBadge in Unity.
"""
from pathlib import Path
import hashlib
import json
import re
import uuid

ROOT = Path(__file__).resolve().parents[2]
RAW = Path('D:/sevenepic/原始资源')
RES = ROOT / 'Assets/Game/Modules/Mail/Res'


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    source = RAW / 'font/Dinmed Regular.TTF'
    target = RES / 'Fonts/MailCount.ttf'
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_bytes(source.read_bytes())
    meta = Path(str(target) + '.meta')
    if not meta.exists():
        value = (ROOT / 'Assets/Game/Common/Res/Fonts/EpicSeven.ttf.meta').read_text('utf8')
        guid = uuid.uuid5(uuid.NAMESPACE_URL, 'AstralOath/' + target.relative_to(ROOT).as_posix()).hex
        value = re.sub(r'^guid: .*$', 'guid: ' + guid, value, flags=re.M)
        value = value.replace('  - EpicSeven', '  - Dinmed')
        meta.write_text(value, encoding='utf8', newline='\n')

    # top_bar.csb mail_count: capInsets x=13,y=9,w=3,h=12; source PNG 31x30.
    border = RES / 'Textures/_notification_num.png.meta'
    value = border.read_text('utf8')
    value = re.sub(r'^  spriteBorder: .*$', '  spriteBorder: {x: 13, y: 9, z: 15, w: 9}', value, flags=re.M)
    border.write_text(value, encoding='utf8', newline='\n')
    report = {
        'sourceFont': 'font/Dinmed Regular.TTF',
        'sourceFontSha256': sha(source),
        'outputFont': target.relative_to(ROOT).as_posix(),
        'outputFontSha256': sha(target),
        'sourceLayout': 'wnd/top_bar.csb',
        'sourceLayoutSha256': sha(RAW / 'wnd/top_bar.csb'),
        'sourceImage': 'img/_notification_num.png',
        'sourceImageSha256': sha(RAW / 'img/_notification_num.png'),
        'capInsets': [13, 9, 3, 12],
        'unityBorder': [13, 9, 15, 9],
    }
    (RES / 'mail-badge-source.json').write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n', encoding='utf8')
    print(json.dumps(report, ensure_ascii=False))


if __name__ == '__main__':
    main()
