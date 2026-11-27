"""Import only the lobby base portrait frame and booster symbol from the authorized archive."""
from pathlib import Path
import hashlib,json,re,uuid
ROOT=Path(__file__).resolve().parents[2];RAW=Path('D:/sevenepic/原始资源');OUT=ROOT/'Assets/Game/Modules/Home/Res/Textures/Header'
def main():
 OUT.mkdir(parents=True,exist_ok=True);template=(ROOT/'Assets/Game/Modules/PlayerProfile/Res/Textures/PortraitFrame.png.meta').read_text(encoding='utf-8');records=[]
 for name,rel in {'ProfileBorderBase':'item/border/icon_border_base.png','BoosterIcon':'img/cm_icon_booster_arti.png'}.items():
  data=(RAW/rel).read_bytes();dest=OUT/(name+'.png');dest.write_bytes(data);meta=Path(str(dest)+'.meta')
  guid=re.search(r'^guid: (.+)$',meta.read_text(encoding='utf-8'),re.M).group(1) if meta.exists() else uuid.uuid5(uuid.NAMESPACE_URL,'AstralOath/'+dest.relative_to(ROOT).as_posix()).hex
  meta.write_text(re.sub(r'^guid: .*$','guid: '+guid,template,flags=re.M),encoding='utf-8',newline='\n');records.append({'source':rel,'sourceSha256':hashlib.sha256(data).hexdigest(),'output':dest.relative_to(ROOT).as_posix(),'outputSha256':hashlib.sha256(dest.read_bytes()).hexdigest()})
 (OUT/'header-source.json').write_text(json.dumps({'assets':records,'sourceLayout':'wnd/lobby_ui_renew.csb','sourceLayoutSha256':hashlib.sha256((RAW/'wnd/lobby_ui_renew.csb').read_bytes()).hexdigest(),'notes':'Byte-identical original PNG. Base ring is presentation default; no earned season frame, booster count, Rank or experience is fabricated.'},ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
 print('Imported 2 original lobby header textures; GUIDs retained.')
if __name__=='__main__':main()
