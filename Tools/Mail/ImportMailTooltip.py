"""Import the authorized original item-card frame, without reading or changing unrelated assets."""
from pathlib import Path
import hashlib,json,re,uuid

ROOT=Path(__file__).resolve().parents[2]
RAW=Path('D:/sevenepic/原始资源')
OUT=ROOT/'Assets/Game/Modules/Mail/Res'

def digest(data):return hashlib.sha256(data).hexdigest()

def main():
 source=RAW/'img/_box_equip_0.png'
 target=OUT/'Textures/MailItemTooltipFrame.png'
 data=source.read_bytes()
 target.write_bytes(data)
 meta=Path(str(target)+'.meta')
 guid=re.search(r'^guid: (.+)$',meta.read_text(encoding='utf8'),re.M).group(1) if meta.exists() else uuid.uuid5(uuid.NAMESPACE_URL,'AstralOath/'+target.relative_to(ROOT).as_posix()).hex
 template=(OUT/'Textures/_box_popup.png.meta').read_text(encoding='utf8')
 template=re.sub(r'^guid: .*$', 'guid: '+guid,template,flags=re.M)
 template=re.sub(r'^  spriteBorder: .*$', '  spriteBorder: {x: 65, y: 60, z: 65, w: 60}',template,flags=re.M)
 template=re.sub(r'^  userData:.*$', '  userData: AstralOath.ImportOverride',template,flags=re.M)
 template=re.sub(r'textureFormat: -1', 'textureFormat: 4',template)
 template=re.sub(r'textureCompression: 1', 'textureCompression: 0',template)
 meta.write_text(template,encoding='utf8',newline='\n')
 layout=RAW/'wnd/item_tooltip_simple.csb'
 manifest=dict(schemaVersion=1,source='img/_box_equip_0.png',sourceSha256=digest(data),outputSha256=digest(target.read_bytes()),
  output=target.relative_to(ROOT).as_posix(),layout='wnd/item_tooltip_simple.csb',layoutSha256=digest(layout.read_bytes()),
  sourceNode='Layer/frame',sourceCapXywh=[65,60,310,266],sourceSize=[440,386],unityBorder=[65,60,65,60],
  notes='Byte-identical original PNG; fixed-size item cards, no synthetic gradient or enlarged white centre frame. Original capture 2026-11-22 at 25/96/99 seconds; original app version and playback speed unconfirmed.')
 (OUT/'tooltip-source.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n',encoding='utf8')
 print('Imported original Mail item-card frame (440 x 386), exact tooltip cap(65,60,310,266).')

if __name__=='__main__':main()
