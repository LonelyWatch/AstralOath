"""Minimal authorized PlayerProfile assets; D:/sevenepic remains read only."""
from pathlib import Path
import hashlib,io,json,re,uuid
from PIL import Image,ImageDraw

ROOT=Path(__file__).resolve().parents[2]
RAW=Path('D:/sevenepic/原始资源')
OUT=ROOT/'Assets/Game/Modules/PlayerProfile/Res'
# Source cap rectangles are x,y,width,height; Unity border is left,bottom,right,top.
ENTRIES={
 'ProfileFrame':('img/renew_popup_bg_alpha.png',(54,54,258,470)),
 'ProfileDim':('img/_dim.png',None),
 'Forest':('profile/bg/profile_bg13.png',None),
 'NicknameFrame':('img/_box_black_white.png',(44,44,24,24)),
 'Letter':('img/_box_w4.png',(16,16,5,3)),
 'SmallBlack':('img/_btn_small_black.png',(14,12,8,6)),
 'SmallBlackPressed':('img/_btn_small_black_p.png',(14,12,8,6)),
 'SmallWhite':('img/_btn_small_white.png',(14,12,8,6)),
 'SmallWhitePressed':('img/_btn_small_white_p.png',(14,12,8,6)),
 'NameInput':('img/_btn_small_white.png',(14,14,4,2)),
 'Divider':('img/_bar_23.png',(30,4,14,4)),
 'GreetingDivider':('img/_bar_23.png',(32,4,10,4)),
 'RecordWhite':('img/_white.png',None),
 'RecordColumnRule':('img/z_tile_bar.png',(32,1,1,2)),
 'RecordShade':('img/_grow_s_white.png',None),
 'Eye':('img/icon_menu_blind.png',None),
 'Clan':('img/icon_clans.png',None),
 'Pencil':('img/icon_menu_write.png',None),
 'Close':('img/cm_icon_etcclose.png',None),
 'PortraitFrame':('img/_hero_s_frame_ally.png',None),
 'PortraitBackground':('img/_hero_s_bg_ally.png',None),
}
for role in ['main','warrior','knight','assassin','ranger','mage','manauser']:
 ENTRIES['Role_'+role]=('img/cm_icon_role_'+role+'.png',None)

def sha(data):return hashlib.sha256(data).hexdigest()
NODE_OPACITY={'Divider':127,'GreetingDivider':127,'RecordColumnRule':76}
def linear_coverage(alpha):
 v=alpha/255
 return (v/12.92 if v<=.04045 else ((v+.055)/1.055)**2.4)*255

def dark_coverage(r,g,b,alpha):
 # Cocos blends dark UI in sRGB. Preserve its white-reference transmission in
 # Unity Linear while retaining the authored RGB; direct alpha230 exposes a
 # large portion of the homepage hero through the source blue profile backing.
 gray=(r+g+b)/(3*255);a=alpha/255
 return round(255*max(0,min(1,(255-linear_coverage(255*(1-a+a*gray)))/(255-linear_coverage(255*gray)))))
def layout_metadata():
 layouts=[{'source':'wnd/'+name+'.csb','sha256':sha((RAW/'wnd'/(name+'.csb')).read_bytes())} for name in ['user_profile','user_profile_nickname','_z_input']]
 return {'layouts':layouts,
  'geometry':'user_profile main980x631 ->1470x946.5 fixed design; forest378x558 at .95*1.5. Nickname573x400: source frame364/558.382, title282/331, buttons85/57/153x65 and238/57/248x65; original PNG white rule is ~32px inside its transparent116px rectangle, cap unchanged. Greeting uses _z_input frame556x406 at642/137, n_topY26, both buttons192x61 at89/30 and278/30; top-left input text. Source record bg537x205 tint0/0/0 opacity63; lowerline537x1 opacity25; rotated z_tile_bar185x4 opacity76 cap32/1/1/2; source_grow_s_white20x113 scale30.89/4.73 tint8/14/23 opacity76 rotation-90.',
  'sourceOpacityCoverage':{'Divider':{'sourceNodeOpacity':127,'sourceCapXywh':[30,4,14,4]},'GreetingDivider':{'sourceNodeOpacity':127,'sourceCapXywh':[32,4,10,4]},'RecordColumnRule':{'sourceNodeOpacity':76,'sourceCapXywh':[32,1,1,2]},'method':'White pixels: effectiveAlpha=(PNG alpha)*(source node opacity)/255, then IEC sRGB transfer to linear coverage, rounded to8bit. Other RGB shadow pixels retain RGB and effectiveAlpha. Runtime Image opacity=1; never apply source node opacity again. Pure white record lower line uses GammaToLinearSpace(25/255) in Image.color because its source PNG is fully opaque.'},
  'darkLayerCoverage':{'method':'White-reference translation aLinear=(1-L(1-a+a*g))/(1-L(g)), where g=mean(source RGB)/255 and a includes source node opacity. Source RGB and geometry retained. This fixed-alpha translation approximates colored source compositing; it is not exact gamma blending against all backdrops.','ProfileFrame':'Only continuous blue pixels r<=g<=b,b>r and pure-black shadow pixels use dark coverage; original white-rule conversion and neutral-gray rules stay unchanged. Source blue alpha230 ->251/249/245 at x183,y60/289/518.','ProfileDim':{'source':'user_profile.csb dim_img','sourceNodeOpacity':255,'sourceNodeTintRgb':[255,255,255],'runtimeImageColor':'white; source radial texture fills the viewport, replacing the flat black.60 substitute'},'RecordShade':{'source':'user_profile.csb _grow_0','sourceNodeOpacity':76,'sourceNodeTintRgb':[8,14,23],'runtimeImageColor':'white; tint and node opacity folded into purpose PNG before dark coverage'},'RecordWhite':'Record bg keeps source black tint; Image opacity=1-L(1-63/255). Lower white rule is separately unchanged.'},
  'portraitComposition':{'reference':'11/28 recording 00:13; compared with profile-candidate/01-self-default.png at1080-height normalization','basis':'Measured runtime composition; actor is not stored in user_profile.csb. Not an original CSB actor constant.','squareSideRelativeToForestHeight':1.3,'topDesignUnits':31,'horizontalAlignment':'centered; square RenderTexture enlarged and clipped by forest RectMask2D, no nonuniform stretch'}}
def main():
 (OUT/'Textures').mkdir(parents=True,exist_ok=True)
 template=(ROOT/'Assets/Game/Modules/Mail/Res/Textures/_box_popup.png.meta').read_text(encoding='utf-8')
 records=[]
 for name,(rel,cap) in ENTRIES.items():
  source=RAW/rel;data=source.read_bytes();dest=OUT/'Textures'/(name+'.png');output=data
  conversion='byte-identical PNG; RGBA32 explicit UI import'
  if name=='ProfileFrame':
   im=Image.open(io.BytesIO(data)).convert('RGBA')
   pixels=[]
   for r,g,b,a in im.getdata():
    if min(r,g,b)>230:
     v=a/255;v=v/12.92 if v<=.04045 else ((v+.055)/1.055)**2.4;a=round(v*255)
    elif (r<=g<=b and b>r) or r==g==b==0:
     a=dark_coverage(r,g,b,a)
    pixels.append((r,g,b,a))
   im.putdata(pixels);encoded=io.BytesIO();im.save(encoded,format='PNG');output=encoded.getvalue()
   conversion='Original RGB/gradient and neutral-gray rules preserved. Continuous blue backing and pure-black shadows use white-reference sRGB-to-Linear dark coverage (blue230->251/249/245 at native183,60/289/518); existing white frame-rule conversion unchanged (34->4,115->44). Point sampling avoids alpha seam.'
  elif name in ['ProfileDim','RecordShade']:
   im=Image.open(io.BytesIO(data)).convert('RGBA');pixels=[]
   for r,g,b,a in im.getdata():
    if name=='RecordShade':
     r,g,b=round(r*8/255),round(g*14/255),round(b*23/255);a=a*76/255
    pixels.append((r,g,b,dark_coverage(r,g,b,a)))
   im.putdata(pixels);encoded=io.BytesIO();im.save(encoded,format='PNG');output=encoded.getvalue()
   conversion='Source radial RGB/geometry preserved; node255 folded into white-reference sRGB-to-Linear dark coverage. Runtime Image color white.' if name=='ProfileDim' else 'Source _grow_s_white geometry retained; original tint8/14/23 and node opacity76 folded before white-reference sRGB-to-Linear dark coverage. Runtime Image color white; never multiply authored tint/opacity again.'
  elif name in NODE_OPACITY:
   opacity=NODE_OPACITY[name]
   im=Image.open(io.BytesIO(data)).convert('RGBA')
   im.putdata([(r,g,b,round(linear_coverage(a*opacity/255) if min(r,g,b)>230 else a*opacity/255)) for r,g,b,a in im.getdata()])
   encoded=io.BytesIO();im.save(encoded,format='PNG');output=encoded.getvalue()
   conversion=f'Purpose copy: source node opacity{opacity} folded into PNG alpha before white sRGB-to-linear coverage conversion; original RGB preserved, nonwhite shadow effective alpha preserved. Runtime opacity1. Source PNG is untouched.'
  dest.write_bytes(output)
  width,height=Image.open(source).size
  border=(cap[0],height-cap[1]-cap[3],width-cap[0]-cap[2],cap[1]) if cap else (0,0,0,0)
  meta=Path(str(dest)+'.meta');guid=re.search(r'^guid: (.+)$',meta.read_text(encoding='utf-8'),re.M).group(1) if meta.exists() else uuid.uuid5(uuid.NAMESPACE_URL,'AstralOath/'+dest.relative_to(ROOT).as_posix()).hex
  text=re.sub(r'^guid: .*$', 'guid: '+guid,template,flags=re.M)
  text=re.sub(r'^  spriteBorder: .*$', '  spriteBorder: {x: %s, y: %s, z: %s, w: %s}'%border,text,flags=re.M)
  text=re.sub(r'^  userData:.*$', '  userData: AstralOath.ImportOverride',text,flags=re.M)
  text=text.replace('textureFormat: -1','textureFormat: 4').replace('textureCompression: 1','textureCompression: 0')
  # Original fine panel edges must not gain an extra bilinear alpha seam.
  if name in ['ProfileFrame','NicknameFrame']:text=re.sub(r'filterMode: 1','filterMode: 0',text)
  meta.write_text(text,encoding='utf-8',newline='\n')
  record={'source':rel,'sourceSha256':sha(data),'output':dest.relative_to(ROOT).as_posix(),'outputSha256':sha(dest.read_bytes()),'size':[width,height],'sourceCapXywh':cap,'unityBorder':border,'conversion':conversion}
  if name in NODE_OPACITY:record['sourceNodeOpacity']=NODE_OPACITY[name]
  records.append(record)
 (OUT/'profile-source.json').write_text(json.dumps({'schemaVersion':1,'sourceRoot':str(RAW),**layout_metadata(),'assets':records,'reference':'11/28 recording 00:13 own profile, 00:04 nickname, 00:18 greeting; game version/publish date/speed unknown','limits':'No unviewed representative/support/frame/custom-card selector UI imported. Forest is presentation default, not a persisted chosen custom card.'},ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
 contact=ROOT/'.artifacts/identity-social-followup/profile/profile-minimal-assets.jpg';contact.parent.mkdir(parents=True,exist_ok=True)
 page=Image.new('RGB',(1200,((len(records)+5)//6)*170),(24,28,36));draw=ImageDraw.Draw(page)
 for i,row in enumerate(records):
  im=Image.open(ROOT/row['output']).convert('RGBA');im.thumbnail((180,140));x=(i%6)*200;y=(i//6)*170
  page.paste(im,(x+10,y+25),im);draw.text((x+8,y+5),Path(row['output']).stem,fill='white')
 page.save(contact,quality=94)
 print('Imported',len(records),'original Profile textures; all GUIDs preserved on rerun.')
if __name__=='__main__':main()
