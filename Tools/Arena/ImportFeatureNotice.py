"""Import only the original notice-banner ornaments; the shared panel already exists."""
import argparse,json,re
from pathlib import Path
from PIL import Image
from ImportArenaArt import REPO,SOURCE_FOLDER,read_source,digest,write
from ImportInventoryButtons import meta,FOLDER,TEXT

DEST=REPO/'Assets/Game/Common/Res/Textures/FeatureNotice'
NAMES=('_bar_23','cm_icon_etcarrow')

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source',type=Path,default=Path('D:/sevenepic'))
    parser.add_argument('--check',action='store_true')
    args=parser.parse_args();source=(args.source/SOURCE_FOLDER).resolve(strict=True)
    if not args.check:DEST.mkdir(parents=True,exist_ok=True)
    meta(DEST,FOLDER,args.check)
    template='\n'.join((REPO/'Assets/Game/Modules/BattleScene/Res/Textures/Hud/Skills/sk_c1001_1.png.meta').read_text(encoding='utf-8-sig').splitlines()[2:])+'\n'
    template=re.sub(r'(?m)^(\s+textureCompression:) \d+$',r'\1 0',template)
    template=re.sub(r'(?m)^  userData:.*$','  userData: AstralOath.ImportOverride',template)
    template=re.sub(r'(?m)^  spriteBorder: .*','  spriteBorder: {x: 0, y: 0, z: 0, w: 0}',template)
    assets=[]
    for name in NAMES:
        relative='img/'+name+'.png';raw=read_source(source,relative);out=DEST/(name+'.png')
        write(out,raw,args.check);meta(out,template,args.check)
        with Image.open(out) as picture:size=list(picture.size)
        assets.append({'asset':out.relative_to(REPO).as_posix(),'source':relative,'sourceSha256':digest(raw),'outputSha256':digest(raw),'size':size})
    manifest=DEST/'source-manifest.json'
    record={'layout':'wnd/msgbox_renewal.csb','layoutSha256':digest(read_source(source,'wnd/msgbox_renewal.csb')),
            'reference':'Screenrecorder-2026-11-28-02-29-26-914.mp4 @61.5s',
            'panel':'Existing Common SourceMessageBox retains corrected low-alpha line coverage and Point sampling.',
            'assets':assets}
    write(manifest,(json.dumps(record,ensure_ascii=False,indent=2)+'\n').encode('utf8'),args.check);meta(manifest,TEXT,args.check)
    print('Verified two original ornaments; source pack remains read-only.')

if __name__=='__main__':main()
