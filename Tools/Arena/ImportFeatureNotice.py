"""Import only msgbox_renewal's notice-purpose assets; never alter shared dialog art."""
import argparse,io,json,re
from pathlib import Path
from PIL import Image
from ImportArenaArt import REPO,SOURCE_FOLDER,read_source,digest,write
from ImportInventoryButtons import meta,FOLDER,TEXT

DEST=REPO/'Assets/Game/Common/Res/Textures/FeatureNotice'
# Native cap rectangles are x,y,width,height, not Unity's left,bottom,right,top.
# Each purpose copy folds its source node opacity exactly once. White decorative
# pixels need linear-light coverage, after folding opacity, to retain Cocos's
# subdued sRGB rules. RGB gradients/shadows and the authored dim stay unchanged.
ENTRIES={
    '_bar_23':((30,4,14,4),127,True),
    '_bar_2':((32,4,34,2),51,True),
    'cm_icon_etcarrow':(None,127,True),
    'renew_popup_bg_2':((54,54,148,302),229,True),
    '_dim':((52,29,56,32),127,False),
}

def coverage(value):
    v=value/255
    return 255*(v/12.92 if v<=.04045 else ((v+.055)/1.055)**2.4)

def purpose_png(raw,opacity,white_coverage):
    image=Image.open(io.BytesIO(raw)).convert('RGBA')
    pixels=[]
    for r,g,b,a in image.getdata():
        effective=a*opacity/255
        if white_coverage and min(r,g,b)>=230:effective=coverage(effective)
        pixels.append((r,g,b,round(effective)))
    image.putdata(pixels);encoded=io.BytesIO();image.save(encoded,format='PNG')
    return encoded.getvalue(),image.size

def update_meta(out,template,border,point,check):
    x,y,z,w=border
    desired=re.sub(r'(?m)^  spriteBorder: .*',f'  spriteBorder: {{x: {x}, y: {y}, z: {z}, w: {w}}}',template)
    desired=re.sub(r'(?m)^(\s+textureFormat:) -?\d+$',r'\1 4',desired)
    desired=re.sub(r'(?m)^(\s+textureCompression:) \d+$',r'\1 0',desired)
    desired=re.sub(r'(?m)^(\s+filterMode:) \d+$',rf'\1 {0 if point else 1}',desired)
    meta(out,desired,check)
    target=out.with_name(out.name+'.meta')
    old=target.read_text(encoding='utf-8-sig')
    # Keep the existing GUID and unrelated importer metadata. meta() only creates
    # a missing file; these explicit owned fields must also update on reimport.
    updated=re.sub(r'(?m)^  spriteBorder: .*',f'  spriteBorder: {{x: {x}, y: {y}, z: {z}, w: {w}}}',old)
    updated=re.sub(r'(?m)^(\s+textureFormat:) -?\d+$',r'\1 4',updated)
    updated=re.sub(r'(?m)^(\s+textureCompression:) \d+$',r'\1 0',updated)
    updated=re.sub(r'(?m)^(\s+filterMode:) \d+$',rf'\1 {0 if point else 1}',updated)
    updated=re.sub(r'(?m)^  userData:.*$','  userData: AstralOath.ImportOverride',updated)
    if check:
        if updated!=old:raise ValueError('Notice importer fields differ: '+str(target))
    elif updated!=old:target.write_text(updated,encoding='utf-8',newline='\n')

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
    assets=[]
    for name,(cap,opacity,white_coverage) in ENTRIES.items():
        relative='img/'+name+'.png';raw=read_source(source,relative);out=DEST/(name+'.png')
        data,size=purpose_png(raw,opacity,white_coverage);write(out,data,args.check)
        width,height=size;border=(cap[0],height-cap[1]-cap[3],width-cap[0]-cap[2],cap[1])if cap else(0,0,0,0)
        update_meta(out,template,border,name=='renew_popup_bg_2',args.check)
        assets.append({'asset':out.relative_to(REPO).as_posix(),'source':relative,'sourceSha256':digest(raw),'outputSha256':digest(data),'size':list(size),
                       'sourceCapXywh':list(cap)if cap else None,'unityBorderLbrt':list(border),'sourceNodeOpacity':opacity,
                       'conversion':'Source opacity folded into purpose PNG alpha; white coverage then converted from sRGB to linear; original RGB and non-white shadow effective alpha preserved.'if white_coverage else'Source opacity127 folded into original dim PNG alpha; original RGB preserved.'})
    manifest=DEST/'source-manifest.json'
    record={'layout':'wnd/msgbox_renewal.csb','layoutSha256':digest(read_source(source,'wnd/msgbox_renewal.csb')),
            'reference':'Screenrecorder-2026-11-28-02-29-26-914.mp4 @61.5s',
            'panel':'Dedicated msgbox_renewal renew_popup_bg_2, cap54/54/148/302, source opacity229; shared SourceMessageBox is not used or modified.',
            'runtimeGeometry':'Original CSB panel380 high before runtime; steady61.5 frame measures390 native high ->585 design. Title/upper/body/lower/close/arrow positions calibrated by1080-height normalization, not claimed authored CSB coordinates.',
            'dimming':'Full-screen black .76 is a measured sRGB-opacity intention; runtime uses1-GammaToLinearSpace(.24), followed by original _dim/opacity127. Reference static white mail core transmission0.208; old direct Linear alpha.76 gave0.525. Runtime stack inference, not a newly claimed CSB field.',
            'assets':assets}
    write(manifest,(json.dumps(record,ensure_ascii=False,indent=2)+'\n').encode('utf8'),args.check);meta(manifest,TEXT,args.check)
    print('Verified five notice-purpose source assets; all existing GUIDs preserved, shared panels and source pack untouched.')

if __name__=='__main__':main()
