"""Import the explicitly selected original death-effect dependency closure (never the whole source tree)."""
import argparse
from hashlib import sha256
from io import BytesIO
import json
from pathlib import Path, PurePosixPath
import re
import sys
import uuid

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / "Tools/CharacterAssets"))
from convert import decode_sct
from import_characters import Closure, safe_source, hash_file
OUTPUT = REPO / "Assets/Game/Modules/BattleScene/Res/Effects/Death"

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source",type=Path,default=Path("D:/sevenepic"))
    parser.add_argument("--effect",choices=("death_eff_01","death_eff_02"),default="death_eff_01")
    parser.add_argument("--check",action="store_true")
    args=parser.parse_args()
    source=(args.source/"原始资源").resolve(strict=True)
    if OUTPUT.resolve().is_relative_to(source): raise ValueError("Read-only sources cannot be overwritten")
    root="effect/"+args.effect+".cfx"
    closure=Closure(source);closure.include(root,"battle-death")
    files=[];payloads={}
    for name in sorted(closure.dependencies):
        raw=safe_source(source,name);suffix=raw.suffix
        if suffix==".sct":
            image,encoding=decode_sct(raw.read_bytes());stream=BytesIO();image.save(stream,format="PNG")
            data=stream.getvalue();relative="Particles/"+raw.stem+".png"
        elif suffix in (".particle",".cfx"):
            data=(json.dumps(closure.structures[name],ensure_ascii=False,indent=2,allow_nan=False)+"\n").encode("utf-8")
            relative=("Particles/" if suffix==".particle" else "")+raw.name+".json"
        else: raise ValueError("Unsupported death dependency: "+name)
        payloads[relative]=data
        files.append(dict(source="原始资源/"+name,sourceSha256=hash_file(raw),asset=relative,assetSha256=sha256(data).hexdigest(),dependencies=sorted(closure.dependencies[name])))
    manifest=dict(schemaVersion=1,root=root,selection="Project default; source-client default variant mapping is unconfirmed.",premultipliedAlpha=True,converterSha256=hash_file(REPO/"Tools/CharacterAssets/convert.py"),files=files)
    payloads["source.json"]=(json.dumps(manifest,ensure_ascii=False,indent=2)+"\n").encode("utf-8")
    missing=[]
    png_template=(REPO/"Assets/Game/Modules/BattleScene/Res/Effects/Hud/Particles/ticle_02.png.meta").read_text(encoding="utf-8-sig")
    for name,data in payloads.items():
        path=OUTPUT/name
        if not path.exists() or path.read_bytes()!=data:
            if args.check: missing.append(name);continue
            path.parent.mkdir(parents=True,exist_ok=True);path.write_bytes(data)
        if not args.check:
            meta=Path(str(path)+".meta")
            if not meta.exists():
                guid=uuid.uuid5(uuid.NAMESPACE_URL,"astraloath:"+path.relative_to(REPO).as_posix()).hex
                content=re.sub(r"guid: [a-f0-9]+","guid: "+guid,png_template,count=1) if path.suffix==".png" else "fileFormatVersion: 2\nguid: "+guid+"\nTextScriptImporter:\n  externalObjects: {}\n"
                meta.write_text(content,encoding="utf-8")
    if not args.check:
        for folder in (OUTPUT,OUTPUT/"Particles"):
            meta=Path(str(folder)+".meta")
            if not meta.exists(): meta.write_text("fileFormatVersion: 2\nguid: "+uuid.uuid5(uuid.NAMESPACE_URL,"astraloath:"+folder.relative_to(REPO).as_posix()).hex+"\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n",encoding="utf-8")
    if missing: parser.exit(1,"Missing or changed death assets: "+", ".join(missing)+"\n")
    print(("Verified" if args.check else "Imported")+f" {len(files)} death assets from {root}.")
if __name__=="__main__":main()
