"""Minimal, reproducible Mail imports from the read-only original archive."""
from pathlib import Path
import hashlib,json,re,struct,shutil,uuid,sys,subprocess,plistlib
from io import BytesIO
ROOT=Path(__file__).resolve().parents[2]
RAW=Path('D:/sevenepic/原始资源')
OUT=ROOT/'Assets/Game/Modules/Mail/Res'
ART={n:'img/'+n+'.png' for n in ['base_menu','_box_r_list','_box_popup','_bar_24','_bar_1','_bar_2','_bar_5','_tap_select_blue','_btn_green_r','_btn_green_r_p','_btn_white_r_p','_btn_blue_r_2','_btn_blue_r_p_2','_btn_gold','_btn_gold_p','cm_icon_gm','cm_icon_mail','cm_icon_mail_log','cm_icon_etcdown','cm_icon_etctime_b','_sorting','_itembg_stuff_1','_itembg_stuff_2','_box_black_white','_box_big_blue','_frame_s','_cm_popup_bg','_dim','_cm_popnoti_bg','cm_icon_etcinfor','cm_hud_top_btn_mail','cm_hud_top_btn_mail_p','_notification_num','renew_popup_bg_2_alpha','renew_popup_bg','_bar_23','_btn_blue_r_3','_btn_blue_r_p_3','_btn_gold_r_2','_btn_gold_r_p_2']}
ART.update({n:'face/'+n+'.png' for n in ['npc1010_s','npc1004_s','c1001_s','c1005_s']})
ART.update({n:'item/'+p+'.png' for n,p in {'to_stamina':'token_stamina','to_crystal':'token_crystal','to_covenant':'token_ticket_rare','to_gold':'token_gold','to_epic':'token_ticket_rare','to_galaxy':'token_ticket_moon','to_mystic':'token_ticket_special','to_goldtransmit':'token_rarecoin','charm_artifact_1':'icon_stone_art1','charm_artifact_2':'icon_stone_art2','charm_artifact_3':'icon_stone_art3','bottle':'icon_bottle_01','powder':'token_powder','gm_gift':'sp_promotion_package','supply_box':'daily_reward_box'}.items()})
ART.update({'art3_1':'item_arti/icon_art3_1.png','weapon':'item/icon_eq_weapon_arena001.png','helmet':'item/icon_eq_helm_arena001.png'})
# Cocos capInsets (x,y,width,height) observed directly from the mail CSBs.
BORDERS={'_box_big_blue':(24,24,24,24),'_bar_24':(22,4,38,4),'_bar_23':(30,4,30,4),'_bar_2':(32,4,32,4),'renew_popup_bg_2_alpha':(0,30,0,30),'_btn_blue_r_3':(100,63,99,64),'_btn_blue_r_p_3':(100,63,99,64),'_btn_gold_r_2':(100,63,99,64),'_btn_gold_r_p_2':(100,63,99,64),'_box_popup':(44,48,48,44),'_box_r_list':(21,21,21,21),'_btn_green_r':(30,14,30,12),'_btn_green_r_p':(30,14,30,12),'_btn_white_r_p':(30,14,30,12),'_btn_blue_r_2':(30,14,30,12),'_btn_blue_r_p_2':(30,14,30,12),'_box_black_white':(30,30,30,30),'_cm_popnoti_bg':(50,0,50,0)}
AUDIO={'MailOpen':'ui-main_hud-btn_mail_0.wav','SingleReward':'ui-gain_rare_0.wav','BundleReward':'ui-price_clear_0.wav','RewardTitle':'effect-uieff_msgbox_title_0.wav'}
BORDERS['_notification_num']=(13,9,15,9)
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def guid(p):return uuid.uuid5(uuid.NAMESPACE_URL,'AstralOath/'+p.relative_to(ROOT).as_posix()).hex

def raster_effect_frames(frames,atlases,width=1024,height=192):
 import numpy as np
 from PIL import Image
 sheet=Image.new('RGBA',(width*4,height*((len(frames)+3)//4)))
 for frame,layers in enumerate(frames):
  comp=np.zeros((height,width,4),np.float32)
  for layer in layers:
   atlas=atlases[layer.get('page',0)]
   xy=np.array(layer['xy'],np.float32).reshape(-1,2)*[1,-1]+[width/2,height/2];uv=np.array(layer['uv'],np.float32).reshape(-1,2)
   sampled=np.zeros_like(comp)
   for ids in np.array(layer['tri']).reshape(-1,3):
    a,b,c=xy[ids];lo=np.maximum([0,0],np.floor(np.min([a,b,c],axis=0)).astype(int));hi=np.minimum([width,height],np.ceil(np.max([a,b,c],axis=0)).astype(int));lx,ly=lo;hx,hy=hi
    if hx<=lx or hy<=ly:continue
    ab,ac=b-a,c-a;denom=ab[0]*ac[1]-ab[1]*ac[0]
    if abs(denom)<1e-8:continue
    yy,xx=np.ogrid[ly:hy,lx:hx];px,py=xx+.5-a[0],yy+.5-a[1];wb=(px*ac[1]-py*ac[0])/denom;wc=(ab[0]*py-ab[1]*px)/denom;wa=1-wb-wc;mask=(wa>=-1e-7)&(wb>=-1e-7)&(wc>=-1e-7)
    coords=np.column_stack((wa[mask],wb[mask],wc[mask]))@uv[ids];tex=coords*np.array([atlas.shape[1],atlas.shape[0]])-.5;tex=np.maximum(0,np.minimum([atlas.shape[1]-1.001,atlas.shape[0]-1.001],tex));ix,iy=np.floor(tex).astype(int).T;dx,dy=(tex-np.floor(tex)).T[:,:,None];near=atlas[iy,ix]*(1-dx)+atlas[iy,ix+1]*dx;far=atlas[iy+1,ix]*(1-dx)+atlas[iy+1,ix+1]*dx;sampled[ly:hy,lx:hx][mask]=near*(1-dy)+far*dy
   r,g,b,a=layer['color'];sampled*= [r*a,g*a,b*a,a]
   if layer['add']:comp[:,:,:3]+=sampled[:,:,:3]
   else:comp=sampled+comp*(1-sampled[:,:,3:4])
  sheet.paste(Image.fromarray(np.uint8(np.clip(comp,0,1)*255)),(frame%4*width,frame//4*height))
 return sheet

def import_effects(template):
 import numpy as np
 from PIL import Image
 sys.path.insert(0,str(ROOT/'Tools/CharacterAssets'))
 from convert import decode_scsp,decode_sct
 scratch=ROOT/'.artifacts/mail-system-review/effect-bake';scratch.mkdir(parents=True,exist_ok=True)
 source=RAW/'effect';name='uieff_msgbox_title'
 skeleton,_=decode_scsp((source/(name+'.scsp')).read_bytes())
 (scratch/'spine.json').write_text(json.dumps(skeleton),encoding='utf8')
 program=r"""using System.Text.Json;
using AstralOath.ThirdParty.Spine38;
var atlas=new Atlas(args[1],new NoTexture());
var data=new SkeletonJson(atlas){Scale=.20f}.ReadSkeletonData(args[0]);var sk=new Skeleton(data);var anim=data.FindAnimation("animation");var frames=new List<object>();
for(int f=0;f<24;f++){sk.SetToSetupPose();anim.Apply(sk,-1,f/30f,false,null,1,MixBlend.Setup,MixDirection.In);sk.UpdateWorldTransform();var layers=new List<object>();foreach(var s in sk.DrawOrder){if(s.Attachment is not RegionAttachment r)throw new InvalidDataException("Expected native region attachment");float[] xy=new float[8];r.ComputeWorldVertices(s.Bone,xy,0);layers.Add(new{xy,uv=r.UVs,tri=new[]{0,1,2,2,3,0},color=new[]{s.R*r.R,s.G*r.G,s.B*r.B,s.A*r.A},add=s.Data.BlendMode==BlendMode.Additive});}frames.Add(layers);}File.WriteAllText(args[2],JsonSerializer.Serialize(frames));
class NoTexture:TextureLoader{public void Load(AtlasPage p,string path){} public void Unload(object t){}}
"""
 (scratch/'Program.cs').write_text(program,encoding='utf8')
 (scratch/'Skin.cs').write_text((ROOT/'Packages/com.astraloath.spine38/Runtime/Skin.cs').read_text().replace('OrderedDictionary<','AstralOath.ThirdParty.Spine38.Collections.OrderedDictionary<'))
 (scratch/'Bake.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup><Compile Include="Program.cs"/><Compile Include="Skin.cs"/><Compile Include="../../../Packages/com.astraloath.spine38/Runtime/**/*.cs" Exclude="../../../Packages/com.astraloath.spine38/Runtime/Skin.cs"/></ItemGroup></Project>')
 subprocess.run([str(ROOT/'.tools/dotnet/dotnet.exe'),'run','--project',str(scratch/'Bake.csproj'),'--',str(scratch/'spine.json'),str(source/(name+'.atlas')),str(scratch/'frames.json')],check=True,stdout=subprocess.DEVNULL)
 page,_=decode_sct((source/(name+'.sct')).read_bytes());atlas=np.asarray(page.convert('RGBA'),np.float32)/255
 sheet=raster_effect_frames(json.loads((scratch/'frames.json').read_text()),[atlas])
 path=OUT/'Textures/RewardTitleFrames.png';sheet.save(path)
 pulse=(ROOT/'Assets/Game/Common/Res/Textures/UiTouchPulse.png.meta').read_text();pulse=re.sub(r'^guid: .*$', 'guid: '+guid(path),pulse,flags=re.M);pulse=re.sub(r'maxTextureSize: 2048','maxTextureSize: 4096',pulse);pulse=re.sub(r'textureFormat: -1','textureFormat: 4',pulse);pulse=re.sub(r'textureCompression: 1','textureCompression: 0',pulse);pulse=re.sub(r'^  userData:.*$', '  userData: AstralOath.ImportOverride',pulse,flags=re.M);Path(str(path)+'.meta').write_text(pulse,newline='\n')
 rows=[dict(id='RewardTitleFrames',sources=[dict(path='effect/'+name+ext,sha256=sha(source/(name+ext)))for ext in ['.cfx','.scsp','.sct','.atlas']],path=path.relative_to(ROOT).as_posix(),sha256=sha(path),frames=24,fps=30,cell=[1024,192],layout=[4,6],sourceScale=.20,sampleTimes=[i/30 for i in range(24)])]
 for n in ['zodiac_star1','pati_03']:
  src=RAW/'particle'/(n+'.sct');im,_=decode_sct(src.read_bytes());dst=OUT/'Textures'/('fx_'+n+'.png');im.save(dst)
  meta=re.sub(r'^guid: .*$','guid: '+guid(dst),template,flags=re.M);meta=re.sub(r'^  spriteBorder: .*$', '  spriteBorder: {x: 0, y: 0, z: 0, w: 0}',meta,flags=re.M);meta=re.sub(r'    textureFormat: -1','    textureFormat: 4',meta);meta=re.sub(r'^  userData:.*$', '  userData: AstralOath.ImportOverride',meta,flags=re.M);meta=meta.replace('sRGBTexture: 1','sRGBTexture: 0');Path(str(dst)+'.meta').write_text(meta,newline='\n')
  rows.append(dict(id='fx_'+n,source='particle/'+n+'.sct',sha256=sha(src),path=dst.relative_to(ROOT).as_posix(),outputSha256=sha(dst)))
 particle=source/'ui_reward_popup_pati_blue_copy.particle';j=plistlib.loads(particle.read_bytes());fields=['emissionRate','duration','emitterType','particleLifespan','particleLifespanVariance','sourcePositionx','sourcePositiony','sourcePositionVariancex','sourcePositionVariancey','maxRadius','maxRadiusVariance','minRadius','minRadiusVariance','angle','angleVariance','speed','speedVariance','gravityx','gravityy','startParticleSize','startParticleSizeVariance','finishParticleSize','finishParticleSizeVariance','rotationStart','rotationStartVariance'];code='// Generated by Tools/Mail/ImportMailAssets.py from ui_reward_popup_pati_blue_copy.particle; do not edit.\nnamespace AstralOath.Client.Mail { internal static class MailEffectParticleData { internal static readonly float[][] Emitters = {\n'+''.join('new float[]{'+','.join(format(float(e[k]),'.9g')+'f' for k in fields)+'},\n'for e in j['emitters'])+'}; } }\n';(ROOT/'Assets/Game/Modules/Mail/Scripts/MailEffectParticleData.Generated.cs').write_text(code);(OUT/'reward-particles.json').write_text(json.dumps(j,indent=2)+'\n');rows.append(dict(id='reward-particles',source='effect/'+particle.name,sha256=sha(particle)))
 src=RAW/'story/bg/img_event_3000days_1.webp';dst=OUT/'Textures/cover_gm_gift.png';Image.open(src).convert('RGBA').save(dst)
 meta=re.sub(r'^guid: .*$','guid: '+guid(dst),template,flags=re.M);meta=re.sub(r'^  spriteBorder: .*$', '  spriteBorder: {x: 0, y: 0, z: 0, w: 0}',meta,flags=re.M);meta=meta.replace('maxTextureSize: 2048','maxTextureSize: 4096').replace('    textureFormat: -1','    textureFormat: 4');meta=re.sub(r'^  userData:.*$', '  userData: AstralOath.ImportOverride',meta,flags=re.M);Path(str(dst)+'.meta').write_text(meta,newline='\n');rows.append(dict(id='cover_gm_gift',source='story/bg/img_event_3000days_1.webp',sha256=sha(src),path=dst.relative_to(ROOT).as_posix(),size=[2400,1080],outputSha256=sha(dst)))
 # CFX explicitly selects these two Spine3.8 sources and intro/loop animations.
 single_names=['item_get_panel_1','item_get_panel_eff_1'];pages=[]
 for stem in single_names:
  document,_=decode_scsp((source/(stem+'.scsp')).read_bytes());(scratch/(stem+'.json')).write_text(json.dumps(document),encoding='utf8');page,_=decode_sct((source/(stem+'.sct')).read_bytes());pages.append(np.asarray(page.convert('RGBA'),np.float32)/255)
 single_program=r"""using System.Text.Json;
using AstralOath.ThirdParty.Spine38;
var all=new List<(Skeleton sk,Animation intro,Animation loop)>();
for(int i=0;i<2;i++){var at=new Atlas(args[i*2+1],new NoTexture());var data=new SkeletonJson(at){Scale=.51f}.ReadSkeletonData(args[i*2]);all.Add((new Skeleton(data),data.FindAnimation("intro"),data.FindAnimation("loop")));}
var output=new List<object>();for(int frame=0;frame<64;frame++){var layers=new List<object>();for(int page=0;page<all.Count;page++){var item=all[page];var sk=item.sk;float t=frame/30f;sk.SetToSetupPose();if(item.intro!=null)item.intro.Apply(sk,-1,Math.Min(t,item.intro.Duration),false,null,1,MixBlend.Setup,MixDirection.In);if(item.loop!=null&&t>item.intro.Duration)item.loop.Apply(sk,-1,t-item.intro.Duration,true,null,1,MixBlend.Replace,MixDirection.In);sk.UpdateWorldTransform();var clipper=new SkeletonClipping();foreach(var slot in sk.DrawOrder){if(slot.Attachment is ClippingAttachment mask){clipper.ClipStart(slot,mask);continue;}float[] xy,uv;int[] tri;float r,g,b,a;if(slot.Attachment==null)continue;if(slot.Attachment is RegionAttachment region){xy=new float[8];uv=region.UVs;tri=new[]{0,1,2,2,3,0};region.ComputeWorldVertices(slot.Bone,xy,0);r=region.R;g=region.G;b=region.B;a=region.A;}else if(slot.Attachment is MeshAttachment mesh){uv=mesh.UVs;xy=new float[uv.Length];tri=mesh.Triangles;mesh.ComputeWorldVertices(slot,0,mesh.WorldVerticesLength,xy,0,2);r=mesh.R;g=mesh.G;b=mesh.B;a=mesh.A;}else throw new InvalidDataException("Unknown native banner attachment");if(clipper.IsClipping){clipper.ClipTriangles(xy,xy.Length,tri,tri.Length,uv);xy=clipper.ClippedVertices.Items.Take(clipper.ClippedVertices.Count).ToArray();uv=clipper.ClippedUVs.Items.Take(clipper.ClippedUVs.Count).ToArray();tri=clipper.ClippedTriangles.Items.Take(clipper.ClippedTriangles.Count).ToArray();}layers.Add(new{xy,uv,tri,color=new[]{slot.R*r,slot.G*g,slot.B*b,slot.A*a},add=slot.Data.BlendMode==BlendMode.Additive,page});clipper.ClipEnd(slot);}}output.Add(layers);}File.WriteAllText(args[4],JsonSerializer.Serialize(output));
class NoTexture:TextureLoader{public void Load(AtlasPage p,string path){}public void Unload(object t){}}
"""
 (scratch/'Single.cs').write_text(single_program,encoding='utf8');(scratch/'Single.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup><Compile Include="Single.cs"/><Compile Include="Skin.cs"/><Compile Include="../../../Packages/com.astraloath.spine38/Runtime/**/*.cs" Exclude="../../../Packages/com.astraloath.spine38/Runtime/Skin.cs"/></ItemGroup></Project>')
 arguments=[str(ROOT/'.tools/dotnet/dotnet.exe'),'run','--project',str(scratch/'Single.csproj'),'--']
 for stem in single_names:arguments.extend([str(scratch/(stem+'.json')),str(source/(stem+'.atlas'))])
 arguments.append(str(scratch/'single-frames.json'));subprocess.run(arguments,check=True,stdout=subprocess.DEVNULL)
 path=OUT/'Textures/SingleRewardFrames.png';raster_effect_frames(json.loads((scratch/'single-frames.json').read_text()),pages,768,192).save(path)
 single_meta=re.sub(r'^guid: .*$','guid: '+guid(path),pulse,flags=re.M);Path(str(path)+'.meta').write_text(single_meta,newline='\n')
 rows.append(dict(id='SingleRewardFrames',sources=[dict(path='effect/'+stem+ext,sha256=sha(source/(stem+ext)))for stem in single_names for ext in ['.scsp','.sct','.atlas']]+[dict(path='effect/'+stem+'.cfx',sha256=sha(source/(stem+'.cfx')))for stem in ['item_get_panel','item_get_panel_eff']],path=path.relative_to(ROOT).as_posix(),sha256=sha(path),frames=64,fps=30,cell=[768,192],layout=[4,16],sourceScale=.51,sampleTimes=[i/30 for i in range(64)]))
 return rows
def main():
 template=(ROOT/'Assets/Game/Common/Res/Textures/InventoryButtons/_btn_green.png.meta').read_text()
 rows=[]
 for name,relative in ART.items():
  src=RAW/relative;dst=OUT/'Textures'/(name+'.png');dst.parent.mkdir(parents=True,exist_ok=True)
  if not src.is_file(): raise FileNotFoundError(src)
  shutil.copyfile(src,dst)
  w,h=struct.unpack('>II',src.read_bytes()[16:24]);border=BORDERS.get(name,(0,0,0,0))
  meta=template;mp=Path(str(dst)+'.meta')
  identifier=re.search(r'^guid: (\w+)',mp.read_text(),re.M)[1] if mp.exists() else guid(dst)
  meta=re.sub(r'^guid: .*$', 'guid: '+identifier,meta,flags=re.M)
  meta=re.sub(r'^  spriteBorder: .*$', '  spriteBorder: {x: %s, y: %s, z: %s, w: %s}'%border,meta,flags=re.M)
  meta=re.sub(r'^  userData:.*$', '  userData: AstralOath.ImportOverride',meta,flags=re.M)
  meta=re.sub(r'    textureFormat: -1','    textureFormat: 4',meta)
  mp.write_text(meta,newline='\n')
  rows.append(dict(id=name,source=relative,sha256=sha(src),path=dst.relative_to(ROOT).as_posix(),size=[w,h],border=border))
 audio_template=(ROOT/'Assets/Game/Common/Res/Sounds/UiRosterScroll.wav.meta').read_text()
 for name,source in AUDIO.items():
  src=ROOT/'.artifacts/mail-reference/1122-audio/sources'/source;dst=OUT/'Sounds'/(name+'.wav');dst.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(src,dst)
  meta=re.sub(r'^guid: .*$', 'guid: '+guid(dst),audio_template,flags=re.M)
  meta=re.sub(r'^  userData:.*$', '  userData: AstralOath.ImportOverride',meta,flags=re.M)
  Path(str(dst)+'.meta').write_text(meta,newline='\n')
  rows.append(dict(id=name,source='sound/ui1.bank event '+source,bankSha256=sha(RAW/'sound/ui1.bank'),sha256=sha(src),path=dst.relative_to(ROOT).as_posix()))
 rows.extend(import_effects(template))
 (OUT/'mail-source.json').write_text(json.dumps(dict(schemaVersion=1,assets=rows,notes='Native mail CSB caps and source hashes. Cover identity: img_event_3000days_1.webp and _frame_s.png, presentation only. Title source Spine sampled without runtime dependency; source particles use local deterministic randomness and calibrated units. Audio actual auditory review pending.'),ensure_ascii=False,indent=2)+'\n',encoding='utf8')
 print(f'Imported {len(ART)} PNG + {len(AUDIO)} WAV, source hashes preserved.')
if __name__=='__main__':main()
