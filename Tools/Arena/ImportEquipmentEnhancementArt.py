"""Import ordinary equipment's original v2 rune dial, materials and native UI audio."""
import argparse, array, io, json, re, sys, wave
from pathlib import Path
import numpy as np
from PIL import Image
from ImportArenaArt import REPO, SOURCE_FOLDER, decode_sct, digest, metadata, png_bytes, read_source, write
from ImportSummoningArt import TEXT_IMPORTER, import_sprite, texture_importer, DRAW_CAP_RECTS
sys.path.insert(0,str(REPO/'Tools/BattleScene'))
from ImportRtaStage import atlas_regions, FOLDER_IMPORTER
from ImportPvpGateAudio import rendered

DEST=REPO/'Assets/Game/Modules/Home/Res/Textures/Inventory/EquipmentEnhancement'
SOUNDS=REPO/'Assets/Game/Modules/Home/Res/Sounds/EquipmentEnhancement'
EVENTS={
 'SlotIn':'ui/upgrade/slot_in', 'SlotOut':'ui/upgrade/slot_out',
 'Start':'effect/itemupgrade_main_v2_start', 'Finish':'effect/itemupgrade_main_v2_circle',
 'Good':'effect/ui_enchant_bonus', 'Great':'effect/ui_enchant_ultimate',
}
def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--check',action='store_true')
 p.add_argument('--source',type=Path,default=Path('D:/sevenepic'))
 p.add_argument('--probe',type=Path,default=REPO/'.artifacts/equipment-enhancement-reference/audio-probe/AudioProbe.dll')
 args=p.parse_args();source=(args.source/SOURCE_FOLDER).resolve(strict=True)
 DRAW_CAP_RECTS.update({'_btn_blue':(40,11,72,39),'_btn_blue_p':(40,11,72,39),'upgrade_arcane_btn':(120,11,1,49),'upgrade_arcane_btn_p':(120,11,1,49),'renew_popup_bg':(80,76,206,422),'_slider_bg':(6,0,401,12)})
 if not args.check:DEST.mkdir(parents=True,exist_ok=True);SOUNDS.mkdir(parents=True,exist_ok=True)
 for folder in [DEST,SOUNDS.parent,SOUNDS]:metadata(folder,args.check,FOLDER_IMPORTER)
 assets=[];sources={}
 for prefix in ['equip','acce']:
  for i in range(4):
   rel=f'item/icon_eq_stone_{prefix}_{i}.png'
   assets.append(import_sprite(source,rel,DEST/Path(rel).name,args.check))
 for rel in ['img/upgrade_arcane_btn.png','img/upgrade_arcane_btn_p.png','img/upgrade_arcane_stat_no.png','img/_itembg_stuff_1.png','img/_itembg_stuff_2.png','img/_itembg_stuff_3.png','img/_itembg_stuff_4.png','img/_itembg_stuff_5.png','img/_btn_blue.png','img/_btn_blue_p.png','img/icon_menu_info_2.png','img/renew_popup_bg.png','img/_slider_bg.png','img/option_bar_circle.png']:
  assets.append(import_sprite(source,rel,DEST/Path(rel).name,args.check))
 for name,regions in {
  'itemupgrade_mainbg_v2':['upgrade_bg','panta_bg','star_01','star_02','star_03'],
  'itemupgrade_main_v2':['stone','stone_b','panta_in','stone_glow','flash','lightning_core_05'],
 }.items():
  base='effect/'+name;atlas=read_source(source,base+'.atlas');page=read_source(source,base+'.sct')
  for ext in ['.sct','.atlas','.scsp']:sources[base+ext]=digest(read_source(source,base+ext))
  image,_=decode_sct(page);parsed=atlas_regions(atlas,image)
  for region in regions:
   rgba=parsed[region].copy();alpha=rgba[:,:,3:4]
   rgba[:,:,:3]=np.divide(rgba[:,:,:3],alpha,out=np.zeros_like(rgba[:,:,:3]),where=alpha>0)
   data=png_bytes(Image.fromarray(np.rint(255*rgba.clip(0,1)).astype('uint8')))
   path=DEST/(region+'.png');write(path,data,args.check)
   importer=texture_importer(path,True).replace('textureCompression: 1','textureCompression: 0')
   importer=re.sub(r'(?m)^  userData:.*$','  userData: AstralOath.ImportOverride',importer)
   metadata(path,args.check,importer)
   assets.append({'asset':path.relative_to(REPO).as_posix(),'region':region,'source':base,'outputSha256':digest(data)})
 # Sound identities are confirmed against the supplied 2026-11-25 recording; good/great remain original-bank candidates.
 index=read_source(source,'sound/events.yml').decode('utf-8-sig');audio=[]
 audioImporter=(REPO/'Assets/Game/Common/Res/Sounds/UiConfirm.wav.meta').read_text(encoding='utf-8-sig').split('AudioImporter:',1)[1]
 audioImporter='AudioImporter:'+re.sub(r'(?m)^  userData:.*$','  userData: AstralOath.ImportOverride',audioImporter).replace('compressionFormat: 1','compressionFormat: 0')
 temp=REPO/'.artifacts/equipment-enhancement-reference/audio';temp.mkdir(parents=True,exist_ok=True)
 for name,event in EVENTS.items():
  native=event.replace('/','\\');block=re.search(r'(?m)^'+re.escape(native)+r':\n((?: +[^\n]+\n)+)',index).group(1)
  event_id=re.search(r"id: '\{([^}]+)\}'",block).group(1)
  data=rendered((REPO/'.tools/dotnet/dotnet.exe',args.probe.resolve()),source,temp,'import-'+name,native,event_id,240000)
  # Preserve authored pre-roll and every nonzero PCM sample, then 50 ms silence. The probe's fixed five-second
  # capture window is not the event's duration and must not retain several seconds of unused tail silence.
  with wave.open(io.BytesIO(data)) as reader:
   samples=array.array('h',reader.readframes(reader.getnframes()))
  active=[i//2 for i,value in enumerate(samples) if value != 0]
  if not active:raise ValueError('Original enhancement event is silent: '+event)
  frames=min(240000,active[-1]+1+2400)
  buffer=io.BytesIO()
  with wave.open(buffer,'wb') as writer:
   writer.setparams((2,2,48000,0,'NONE',''));writer.writeframes(samples[:frames*2].tobytes())
  data=buffer.getvalue()
  path=SOUNDS/(name+'.wav');write(path,data,args.check);metadata(path,args.check,audioImporter)
  audio.append({'asset':path.relative_to(REPO).as_posix(),'event':'event:/'+event,'eventId':event_id,'bank':'sound/ui1.bank','sourceSamples':re.findall(r'(?m)^        ([^:]+):',block),'pcmFrames':frames,'durationSeconds':frames/48000,'outputSha256':digest(data)})
 for rel in ['wnd/unit_item_upgrade.csb','sound/ui1.bank','sound/events.yml']:sources[rel]=digest(read_source(source,rel))
 manifest=DEST/'source-manifest.json';write(manifest,(json.dumps({'sources':sources,'assets':assets,'audio':audio},ensure_ascii=False,indent=2)+'\n').encode(),args.check);metadata(manifest,args.check,TEXT_IMPORTER)
 print(f'Imported/verified {len(assets)} native images and {len(audio)} events.')
if __name__=='__main__':main()
