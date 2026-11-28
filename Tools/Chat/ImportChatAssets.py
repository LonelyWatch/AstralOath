"""Import the minimal original public-chat UI and the exact service sticker allowlist.
Read-only source; every retained PNG, cap rectangle and transformation is recorded.
"""
from pathlib import Path
import hashlib,io,json,re,uuid
from PIL import Image
ROOT=Path(__file__).resolve().parents[2]
RAW=Path('D:/sevenepic/原始资源')
OUT=ROOT/'Assets/Game/Modules/ChannelChat/Res'
ENTRIES={
 'DrawerFrame':('img/_box_big_gold_90.png',(54,54,52,48)),
 'BlackGoldFrame':('img/_box_black_gold.png',(44,44,24,24)),
 'SmallBlack':('img/_btn_small_black.png',(14,12,8,6)),
 'SmallBlackPressed':('img/_btn_small_black_p.png',(14,12,8,6)),
 'SmallWhite':('img/_btn_small_white.png',(14,12,8,6)),
 'SmallWhitePressed':('img/_btn_small_white_p.png',(14,12,8,6)),
 'InputWhite':('img/_btn_white.png',(30,30,92,1)),
 'GreenSend':('img/_btn_green.png',(40,11,72,39)),
 'GreenSendPressed':('img/_btn_green_p.png',(40,11,72,39)),
 'GreenJoin':('img/_btn_green.png',(14,12,124,35)),
 'GreenJoinPressed':('img/_btn_green_p.png',(14,12,124,35)),
 'PublicTab':('img/_tap_select.png',(15,11,41,36)),
 'SettingsTab':('img/_tap_select.png',(9,15,53,28)),
 'Divider':('img/_bar_2.png',(32,4,34,2)),
 'TitleDivider':('img/_bar_1.png',(32,4,34,6)),
 'SettingsDivider':('img/_bar_3.png',(32,4,34,9)),
 'HeaderChat':('img/cm_hud_top_btn_chatting.png',None),
 'HeaderChatPressed':('img/cm_hud_top_btn_chatting_p.png',None),
 'Favorite':('img/icon_menu_favorite.png',None),
 'Switch':('img/icon_menu_chanel.png',None),
 'Opacity':('img/icon_menu_opacity.png',None),
 'Settings':('img/icon_menu_noti_setting.png',None),
 'Emoji':('img/icon_menu_emoji.png',None),
 'Talk':('img/icon_menu_talk.png',None),
 'Guild':('img/icon_menu_knightage.png',None),
 'Keyword':('img/icon_menu_name_set.png',None),
 'CheckBackground':('img/cm_checkbg.png',None),
 'CheckMark':('img/cm_checkicon2.png',None),
 'Information':('img/cm_icon_etcinfor.png',None),
 'PortraitFrame':('img/_hero_s_frame_ally.png',None),
 'PortraitBackground':('img/_hero_s_bg_ally.png',None),
 'NoticeArrow':('img/cm_icon_etcarrow2_b.png',None),
}
def sha(v):return hashlib.sha256(v).hexdigest()
def linear(value):return value/12.92 if value<=.04045 else ((value+.055)/1.055)**2.4
def dark_coverage_variant(src,opacity=255,black=False):
 """Preserve Cocos darkening against a white reference in Unity Linear.
 Neutral dark source pixels keep RGB; only effective opacity is translated.
 Gold/colored frame pixels retain source color and the authored node opacity.
 """
 image=Image.open(src).convert('RGBA');pixels=[]
 for r,g,b,a in image.getdata():
  alpha=a/255*opacity/255
  if black:r=g=b=0
  if max(r,g,b)<=32 and max(r,g,b)-min(r,g,b)<=3:
   gray=(r+g+b)/(3*255)
   alpha=(1-linear(1-alpha+alpha*gray))/(1-linear(gray))
  pixels.append((r,g,b,round(max(0,min(1,alpha))*255)))
 image.putdata(pixels);buffer=io.BytesIO();image.save(buffer,format='PNG');return buffer.getvalue()
def identity(path):return uuid.uuid5(uuid.NAMESPACE_URL,'AstralOath/'+path.relative_to(ROOT).as_posix()).hex
def meta(path,template,border=(0,0,0,0)):
 p=Path(str(path)+'.meta');guid=re.search(r'^guid: (.+)$',p.read_text(encoding='utf-8'),re.M).group(1) if p.exists() else identity(path)
 text=re.sub(r'^guid: .*$', 'guid: '+guid,template,flags=re.M)
 text=re.sub(r'^  spriteBorder: .*$', '  spriteBorder: {x: %s, y: %s, z: %s, w: %s}'%border,text,flags=re.M)
 text=re.sub(r'^  userData:.*$', '  userData: AstralOath.ImportOverride',text,flags=re.M)
 text=text.replace('textureFormat: -1','textureFormat: 4').replace('textureCompression: 1','textureCompression: 0')
 p.write_text(text,encoding='utf-8',newline='\n')
def main():
 (OUT/'Textures').mkdir(parents=True,exist_ok=True);(OUT/'Stickers').mkdir(parents=True,exist_ok=True)
 template=(ROOT/'Assets/Game/Modules/Mail/Res/Textures/_box_popup.png.meta').read_text(encoding='utf-8')
 entries=[]
 for alias,(rel,cap) in ENTRIES.items():
  src=RAW/rel;original=src.read_bytes();data=dark_coverage_variant(src) if alias=='DrawerFrame' else original;dest=OUT/'Textures'/(alias+'.png');dest.write_bytes(data);w,h=Image.open(src).size
  border=(cap[0],h-cap[1]-cap[3],w-cap[0]-cap[2],cap[1]) if cap else (0,0,0,0);meta(dest,template,border)
  entry=dict(alias=alias,source=rel,sourceSha256=sha(original),outputSha256=sha(data),size=[w,h],sourceCapXywh=cap,unityBorderLbrt=border)
  if alias=='DrawerFrame':entry.update(nodeOpacity=255,conversion='Neutral dark pixel alpha uses (1-linear(1-a+a*g))/(1-linear(g)); original RGB and colored gold pixels retained. No neutral backdrop pixels erased.')
  entries.append(entry)
 # Reference39.0/39.5 keeps a translucent full backdrop, not a color-keyed gold rim.
 variants=[('DrawerFrameTransparent','img/_box_big_gold_90.png',127,False,ENTRIES['DrawerFrame'][1]),
           ('ChatDim','img/_dim.png',127,False,None),('FavoriteDim','img/_dim.png',255,True,None),
           ('ChatHeaderShadow','img/_grow.png',153,False,None),('FavoriteShadow','img/_grow.png',178,False,None)]
 for alias,rel,opacity,black,cap in variants:
  src=RAW/rel;p=OUT/'Textures'/(alias+'.png');p.write_bytes(dark_coverage_variant(src,opacity,black));w,h=Image.open(src).size
  border=(cap[0],h-cap[1]-cap[3],w-cap[0]-cap[2],cap[1]) if cap else (0,0,0,0);meta(p,template,border)
  entries.append(dict(alias=alias,source=rel,sourceSha256=sha(src.read_bytes()),outputSha256=sha(p.read_bytes()),size=[w,h],sourceCapXywh=cap,unityBorderLbrt=border,
    nodeOpacity=opacity,nodeTintRgb=[0,0,0] if black else [255,255,255],
    conversion='Apply node opacity, then neutral dark alpha=(1-linear(1-a+a*g))/(1-linear(g)); original geometry/RGB retained except authored black tint. Never erase neutral pixels.',
    opacityProvenance='chat.csb/chat_favorite.csb node' if alias!='DrawerFrameTransparent' else 'Runtime39.0/39.5 measured against48.5 underlying static lobby: opacity127 inference, not a CSB default.'))
 ids=(ROOT/'Server/Host/Arena/arena-chat-stickers.txt').read_text(encoding='utf-8').split();stickers=[]
 for key in ids:
  src=RAW/'emoticon_chat'/(key+'.png');data=src.read_bytes();p=OUT/'Stickers'/(key+'.png');p.write_bytes(data);meta(p,template);stickers.append(dict(id=key,source='emoticon_chat/'+key+'.png',sha256=sha(data),size=Image.open(src).size))
 manifest=dict(schemaVersion=1,sourceRoot=str(RAW),sourceSchema='https://raw.githubusercontent.com/cocos2d/cocos2d-x/v3/cocos/editor-support/cocostudio/CSParseBinary_generated.h',assets=entries,stickers=stickers,layouts=[dict(path='wnd/'+n+'.csb',sha256=sha((RAW/'wnd'/(n+'.csb')).read_bytes())) for n in ['chat','chat_section','chat_favorite','chat_filter','chat_node_text','chat_node_emoji','chat_emoji_tip','chat_emoji_tip_item']],references=['2026-11-28 user recording32,37.5,39.5,42.5,45.5s','2026-11-14 user Arena original100,102s; version/publish date/speed unknown; common source message/picker only'],limits='Switch-channel uses the already viewed favorite-input layout. The unobserved n_channel_layer popup is deliberately not reconstructed. Keyword edits use same observed row; unobserved separate input popup not invented.')
 (OUT/'chat-source.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
 print('Imported',len(entries),'chat art variants and',len(stickers),'allowlisted original stickers; existing GUIDs preserved.')
if __name__=='__main__':main()
