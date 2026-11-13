"""Import the explicitly approved character dependency closure.
The external source is read-only. Output paths are deterministic and existing
Unity .meta files are untouched. Missing dependencies abort before conversion.
"""
from __future__ import annotations
import argparse
from collections import Counter,defaultdict
import hashlib,json,plistlib,re,shutil
from pathlib import Path,PurePosixPath
from PIL import Image
from convert import convert_scsp,convert_sct,decode_scsp

CHARACTERS={
    "c1001":("ras","拉斯"),
    "c1002":("cecilia","赛西莉亚"),
    "c1003":("rose","萝季"),
    "c1004":("silk","席可"),
    "c1005":("mercedes","梅赛德丝"),
    "c1006":("kise","绮世"),
    "c1007":("wildred","维德瑞"),
    "c1008":("arming","亚敏"),
    "c1009":("charlotte","夏绿蒂"),
    "c1010":("zerato","杰拉图"),
    "c1011":("karin","卡琳"),
    "c1012":("corvus","科尔布思"),
    "c1062":("angelica","安洁莉卡"),
}
# Keep source identifiers verbatim even where the model and STG stems differ.
SKILL_STEMS={"c1002":"c_cecilia_skill_new","c1007":"c_wilderd_skill","c1008":"c_aming_skill","c1010":"c_jerato_skill"}
SKILL_VARIANTS={("c1009",3):"c_charlotte_skill_03_05"}
# Base STG omits this cue; the base skeleton marker and skin STG corroborate its timing.
ANIMATION_CUTINS={
    "stagept/c_mercedes_skill_03.stg": ("model/mercedes.scsp", "skill3", "cutin", "sk_c1005_3"),
    "stagept/c_jerato_skill_03.stg": ("model/zerato.scsp", "skill3", "cutin", "sk_c1010_3"),
    "stagept/c_karin_skill_03.stg": ("model/karin.scsp", "skill3_2", "cutin", "sk_c1011_3"),
}


def skill_ids(cid):
    stem=SKILL_STEMS.get(cid, f"c_{CHARACTERS[cid][0]}_skill")
    return [SKILL_VARIANTS.get((cid,index), f"{stem}_{index:02}") for index in range(1,4)]

BANKS=["sound/master.bank","sound/master.strings.bank","sound/sfx1.bank","sound/voc.event.bank","sound/ja/voc1.audio_ja.bank","sound/gerneal.bank"]
RES=Path("Assets/Game/Modules/CharacterPresentation/Res")
MANIFEST=Path("Assets/Game/Modules/CharacterPresentation/Configs/import-manifest.json")
WHITE_PMA_REGIONS=Path(__file__).with_name("white-pma-regions.json")
EXTENSIONS={".scsp",".atlas",".sct",".sct2",".png",".jpg",".jpeg",".webp",".cfx",".particle",".stg",".timeline",".bank"}


def select_characters(ids):
    if not ids or len(ids)!=len(set(ids)) or any(i not in CHARACTERS for i in ids):
        raise ValueError("Only approved character IDs may be imported, once each")
    return {i:CHARACTERS[i] for i in sorted(ids)}


def safe_source(root:Path,relative:str)->Path:
    if not relative or "\\" in relative or ":" in relative or relative.startswith("/"):
        raise ValueError(f"Invalid source path {relative!r}")
    path=PurePosixPath(relative)
    if any(p in ("..","") for p in path.parts): raise ValueError(f"Path traversal {relative!r}")
    target=(root/Path(*path.parts)).resolve()
    if not target.is_relative_to(root.resolve()): raise ValueError(f"Source escaped root: {relative}")
    return target


def address(source): return "characterlab/source/"+source


def resolve_atlas(root,source,seen=()):
    if source in seen:raise ValueError("Atlas alias cycle")
    text=safe_source(root,source).read_text(encoding="utf-8-sig").strip()
    if "\n" not in text and text.endswith(".atlas"):
        if "/" in text or "\\" in text or ":" in text:raise ValueError("Unsafe atlas alias")
        target=str(PurePosixPath(source).parent/text)
        return resolve_atlas(root,target,seen+(source,))
    return source


def atlas_pages(root,source):
    source=resolve_atlas(root,source)
    path=safe_source(root,source)
    lines=path.read_text(encoding="utf-8-sig").splitlines()
    names=[]
    for index,line in enumerate(lines):
        if line and not line[0].isspace() and ":" not in line and (index==0 or not lines[index-1].strip()):
            names.append(line.strip())
    if not names: raise ValueError(f"Atlas has no pages: {source}")
    pages=[]
    for name in names:
        if "/" in name or "\\" in name or ":" in name or name in (".",".."):
            raise ValueError(f"Unsafe atlas page name: {name}")
        relative=(PurePosixPath(source).parent/name).as_posix()
        if not safe_source(root,relative).is_file():raise FileNotFoundError(f"Atlas page missing: {source} -> {name}")
        resolved=relative
        pages.append({"name":name,"source":resolved})
    return pages


def load_white_pma_regions(root, sources):
    atlas_rules = json.loads(WHITE_PMA_REGIONS.read_text(encoding="utf-8"))
    result = {}
    for atlas, region_names in atlas_rules.items():
        if atlas not in sources:
            continue
        pages = atlas_pages(root, atlas)
        if len(pages) != 1:
            raise ValueError(f"White PMA restoration requires one atlas page: {atlas}")
        regions = {}
        lines = safe_source(root, atlas).read_text(encoding="utf-8-sig").splitlines()
        for name in region_names:
            indices = [index for index, line in enumerate(lines) if line == name]
            if len(indices) != 1:
                raise ValueError(f"Expected one white PMA atlas region: {atlas} -> {name}")
            fields = {}
            for line in lines[indices[0] + 1:]:
                if not line or not line[0].isspace():
                    break
                key, value = line.strip().split(":", 1)
                fields[key] = value.strip()
            x, y = map(int, fields["xy"].split(","))
            width, height = map(int, fields["size"].split(","))
            rotation = fields["rotate"]
            if rotation in ("true", "90"):
                width, height = height, width
            elif rotation not in ("false", "0"):
                raise ValueError(f"Unsupported white PMA region rotation: {atlas} -> {name}")
            regions[name] = (x, y, width, height)
        page = pages[0]["source"]
        if page in result:
            raise ValueError(f"Duplicate white PMA rules for atlas page: {page}")
        result[page] = regions
    return result


def category(source):
    suffix=PurePosixPath(source).suffix.lower()
    if suffix==".bank": return "Sounds"
    if suffix==".atlas": return "Atlas"
    if suffix in (".sct",".sct2",".png",".jpg",".jpeg"):return "Textures"
    if suffix==".scsp":return "Animations"
    if suffix in (".cfx",".particle",".webp"):return "Effects"
    return "Configs"


def asset_type(source):
    suffix=PurePosixPath(source).suffix.lower()
    return {".scsp":"skeleton",".atlas":"atlas",".cfx":"effect",".particle":"particle",".webp":"cutin",".timeline":"timeline",".stg":"graph",".bank":"bank"}.get(suffix,"texture")


def output_owner_path(source,owners,existing_owners=None):
    ids=owners[source]
    owner=(existing_owners or {}).get(source)
    if owner is None:
        owner=Path("Shared") if len(ids)>1 or source in BANKS else Path("Characters")/next(iter(ids))
    return RES/owner/category(source)/Path(*PurePosixPath(source).parts)


def load_structure(path):
    data=path.read_bytes()
    return plistlib.loads(data) if data.lstrip().startswith(b"<?xml") else json.loads(data.decode("utf-8-sig"))


def walk(value):
    if isinstance(value,dict):
        yield value
        for child in value.values(): yield from walk(child)
    elif isinstance(value,list):
        for child in value: yield from walk(child)


class Closure:
    def __init__(self,root):
        self.root=Path(root);self.dependencies={};self.structures={};self.owners=defaultdict(set);self.diagnostics=[]
        self.graph_additions={}

    def resolve(self,name,folder,suffix):
        relative=name if "/" in name else folder+"/"+name
        if not PurePosixPath(relative).suffix:relative+=suffix
        safe_source(self.root,relative)
        return relative

    def animation_cutin(self,source,entities):
        spec=ANIMATION_CUTINS.get(source)
        if spec is None or any(node.get("etty")=="CUTIN" for node in entities):
            return None
        model,animation,event,clip=spec
        if not any(node.get("etty")=="ANI" and node.get("ani")==animation for node in entities):
            raise ValueError(f"Missing cut-in animation node: {source}/{animation}")
        skeleton,_=decode_scsp(safe_source(self.root,model).read_bytes())
        frames=skeleton["animations"].get(animation,{}).get("events",[])
        if not any(frame.get("name")==event for frame in frames):
            raise ValueError(f"Missing cut-in animation marker: {model}/{animation}/{event}")
        listener=max(node["guid"] for node in entities)+1
        self.graph_additions[source]={
            "entities":[
                {"guid":listener,"etty":"ONEVENT","ani":animation,"name":event},
                {"guid":listener+1,"etty":"CUTIN","args":clip,"curtain_color":"0,0,0,255"},
            ],
            "connections":[{"from":listener,"to":listener+1}],
        }
        return self.resolve(clip,"cut/fhd",".webp")

    def direct(self,source):
        path=safe_source(self.root,source)
        if not path.is_file():raise FileNotFoundError(f"Missing dependency: {source}")
        suffix=path.suffix.lower()
        if suffix not in EXTENSIONS:raise ValueError(f"Dependency type outside allowlist: {source}")
        deps=set()
        if suffix==".scsp":
            atlas=str(PurePosixPath(source).with_suffix(".atlas"))
            if safe_source(self.root,atlas).is_file() and safe_source(self.root,atlas).stat().st_size>0:deps.add(atlas)
            elif not source.startswith(("camera/","spani/")):raise FileNotFoundError(f"Skeleton atlas missing: {source}")
        elif suffix==".atlas":
            resolved=resolve_atlas(self.root,source)
            if resolved!=source:deps.add(resolved)
            deps.update(p["source"] for p in atlas_pages(self.root,source))
        elif suffix in (".stg",".cfx",".particle",".timeline"):
            document=load_structure(path);self.structures[source]=document
            if suffix==".stg":
                for entity in document["entities"]:
                    kind=entity.get("etty");name=entity.get("source")
                    if kind=="CUTIN" and entity.get("args"):deps.add(self.resolve(entity["args"],"cut/fhd",".webp"))
                    if kind=="SHAKE" and name:deps.add(self.resolve(name,"camera",".scsp"))
                    elif kind=="NODE_ANI" and name:deps.add(self.resolve(name,"spani",".scsp"))
                    elif name:deps.add(self.resolve(name,"effect",".cfx"))
                    if entity.get("effect"):deps.add(self.resolve(entity["effect"],"effect",".cfx"))
                cutin=self.animation_cutin(source,document["entities"])
                if cutin:deps.add(cutin)
            elif suffix==".cfx":
                for item in walk(document):
                    if not item.get("source"):continue
                    fmt=item.get("format")
                    extensions={"spine":".scsp","particle":".particle","cfx":".cfx","sprite":".sct","image":".png"}
                    if fmt not in extensions:raise ValueError(f"Unsupported CFX primitive format {fmt}: {source}")
                    deps.add(self.resolve(item["source"],"effect",extensions[fmt]))
            elif suffix==".particle":
                for item in walk(document):
                    if item.get("textureFileName"):deps.add(self.resolve(item["textureFileName"],"particle",".sct"))
            elif suffix==".timeline":
                animations=None
                for skin,clips in document["timeline"].items():
                    for clip,timeline in clips.items():
                        for frame in timeline.get("frames",[]):
                            name=frame.get("param",{}).get("source")
                            if not name:continue
                            function=frame.get("func")
                            if function=="shake":
                                deps.add(self.resolve(name,"camera",".scsp"))
                                continue
                            if function!="effect":continue
                            effect=self.resolve(name,"effect",".cfx")
                            if not safe_source(self.root,effect).is_file():
                                if animations is None:
                                    model=safe_source(self.root,str(PurePosixPath(source).with_suffix(".scsp")))
                                    animations=decode_scsp(model.read_bytes())[0]["animations"]
                                if clip not in animations:
                                    diagnostic={"severity":"info","source":source,
                                        "code":"unused-timeline-dependency-missing",
                                        "message":f"{skin}/{clip} cannot play on the source model: missing {effect}"}
                                    if diagnostic not in self.diagnostics:self.diagnostics.append(diagnostic)
                                    continue
                            deps.add(effect)
        return sorted(deps)

    def include(self,source,character,trail=()):
        if source in trail:raise ValueError("Cyclic dependency: "+" -> ".join(trail+(source,)))
        if character in self.owners[source]:return
        if source not in self.dependencies:self.dependencies[source]=self.direct(source)
        self.owners[source].add(character)
        for dependency in self.dependencies[source]:self.include(dependency,character,trail+(source,))

    def descendants(self,source):
        result=set()
        def collect(path):
            for child in self.dependencies[path]:
                if child not in result:result.add(child);collect(child)
        collect(source);return sorted(result)

    def build(self,characters,include_banks=True):
        for cid,(hero,_) in characters.items():
            roots=[f"model/{hero}.scsp",f"portrait/{cid}.scsp"]
            roots += [f"stagept/{skill}.stg" for skill in skill_ids(cid)]
            timeline=f"model/{hero}.timeline"
            if safe_source(self.root,timeline).is_file():roots.append(timeline)
            else:self.diagnostics.append({"severity":"info","source":timeline,"code":"timeline-not-provided","message":"Source uses STG and skeleton events independently."})
            if include_banks:roots+=BANKS
            for source in roots:self.include(source,cid)
        return self


def hash_file(path):
    digest=hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda:stream.read(1024*1024),b""):digest.update(chunk)
    return digest.hexdigest()


def json_write(path,data):
    path.parent.mkdir(parents=True,exist_ok=True)
    text=json.dumps(data,ensure_ascii=False,indent=2,allow_nan=False)+"\n"
    if not path.exists() or path.read_text(encoding="utf-8")!=text:path.write_text(text,encoding="utf-8")


def import_assets(source_root,project,ids,include_banks=True):
    characters=select_characters(ids);project=Path(project).resolve();closure=Closure(source_root).build(characters,include_banks)
    entries=[];conversion_reports={};source_hashes={}
    # Expanding the roster can make an existing dependency shared. Keep its Unity
    # path/address (and GUID) until an explicit migration is performed.
    existing_owners={}
    if (project/MANIFEST).is_file():
        for item in json.loads((project/MANIFEST).read_text(encoding="utf-8"))["entries"]:
            parts=Path(item["path"]).relative_to(RES).parts
            if parts[0]=="Shared":
                owner=Path("Shared")
            elif len(parts)>2 and parts[0]=="Characters" and parts[1] in CHARACTERS:
                owner=Path(*parts[:2])
            else:
                raise ValueError("Invalid existing output owner: " + item["path"])
            if item["source"] in existing_owners and owner!=existing_owners[item["source"]]:
                raise ValueError("Inconsistent existing ownership for " + item["source"])
            existing_owners[item["source"]]=owner
    def entry(source,relative,asset_address,kind,deps,conversion):
        if source not in source_hashes:source_hashes[source]=hash_file(safe_source(closure.root,source))
        source_hash=source_hashes[source]
        entries.append({"source":source,"sourceSha256":source_hash,"path":relative.as_posix(),"address":asset_address,"type":kind,"dependencies":deps,"outputSha256":hash_file(project/relative),"conversion":conversion})
    white_pma = load_white_pma_regions(closure.root, closure.dependencies)
    for source in sorted(closure.dependencies):
        source_path=safe_source(closure.root,source);relative=output_owner_path(source,closure.owners,existing_owners);suffix=source_path.suffix.lower()
        deps=[address(s) for s in closure.dependencies[source]];out=project/relative
        report={"status":"converted"}
        if suffix==".scsp":
            json_relative=relative.with_suffix(".spine.json");summary=convert_scsp(source_path,project/json_relative)
            conversion_reports[source]=summary
            conversion={"status":"converted",**{key:summary[key] for key in (
                "spineVersion","animationCount","binaryBytes","consumedBytes",
                "converterVersion","boneFlipTimelines","curveTimelines")}}
            if summary["spineVersion"]=="2.1.27": conversion["curveSemantics"]="scsp-v2-sentinel/1"
            entry(source,json_relative,address(source)+"/json","spine-json",[],conversion)
            atlas=next((p for p in closure.dependencies[source] if p.endswith(".atlas")),None)
            if not atlas and summary["renderAttachments"]:raise ValueError(f"Bone-only skeleton has render slots without atlas: {source}")
            pages=atlas_pages(closure.root,atlas) if atlas else []
            descriptor={"version":summary["spineVersion"],"jsonAddress":address(source)+"/json","atlasAddress":address(atlas) if atlas else "","pages":[{"name":p["name"],"address":address(p["source"])} for p in pages],"premultipliedAlpha":True,"scale":.01}
            relative=relative.with_suffix(".skeleton.json");json_write(project/relative,descriptor);deps=[address(source)+"/json"]+deps+[address(p["source"]) for p in pages]
            report.update(spineVersion=summary["spineVersion"],animationCount=summary["animationCount"])
        elif suffix==".atlas":
            relative=relative.with_suffix(".atlas.txt");(project/relative).parent.mkdir(parents=True,exist_ok=True)
            resolved=resolve_atlas(closure.root,source);shutil.copyfile(safe_source(closure.root,resolved),project/relative)
            if resolved!=source:report["resolvedAtlas"]=resolved
        elif suffix in (".sct",".sct2"):
            relative=relative.with_suffix(".png");report.update(convert_sct(source_path,project/relative,white_pma_regions=white_pma.get(source)))
        elif suffix in (".cfx",".particle",".timeline"):
            relative=relative.with_suffix(suffix+".json");json_write(project/relative,closure.structures[source])
        elif suffix==".stg":
            relative=relative.with_suffix(".graph.json")
            document=closure.structures[source]
            graph_sources=set(closure.descendants(source))
            for cid in closure.owners[source]:
                timeline=f"model/{CHARACTERS[cid][0]}.timeline"
                if timeline in closure.dependencies:
                    graph_sources.add(timeline);graph_sources.update(closure.descendants(timeline))
            deps=sorted(set(deps)|{address(p) for p in graph_sources})
            graph={"id":PurePosixPath(source).stem,"sourceFormat":"e7-stg","source":document,"assets":[{"source":dep,"address":address(dep),"type":asset_type(dep)} for dep in sorted(graph_sources)],"diagnostics":[]}
            if source in closure.graph_additions:graph["additions"]=closure.graph_additions[source]
            json_write(project/relative,graph)
        elif suffix==".webp":
            frames=[]
            with Image.open(source_path) as image:
                width,height=image.size
                for i in range(image.n_frames):
                    image.seek(i);frame_relative=relative.parent/(relative.stem+"_frames")/f"{i:04}.png"
                    frame_path=project/frame_relative;frame_path.parent.mkdir(parents=True,exist_ok=True)
                    # Pillow returns the fully composited frame at seek(), including
                    # the WebP blend/dispose semantics and exact millisecond duration.
                    image.convert("RGBA").save(frame_path,format="PNG")
                    frame_address=address(source)+f"/frame/{i:04}";duration=image.info.get("duration")
                    if duration is None or duration<=0:raise ValueError(f"Invalid WebP duration {source} frame {i}")
                    frames.append({"address":frame_address,"durationSeconds":duration/1000})
                    entry(source,frame_relative,frame_address,"texture",[],{"status":"decoded-frame","frame":i})
            relative=relative.with_suffix(".cutin.json");json_write(project/relative,{"width":width,"height":height,"frames":frames});deps=[f["address"] for f in frames]
            report.update(frameCount=len(frames),durationSeconds=sum(f["durationSeconds"] for f in frames))
        else:
            if suffix==".bank":relative=relative.with_suffix(".bank.bytes")
            (project/relative).parent.mkdir(parents=True,exist_ok=True)
            if not (project/relative).exists() or hash_file(source_path)!=hash_file(project/relative):shutil.copyfile(source_path,project/relative)
            report={"status":"copied"}
        entry(source,relative,address(source),asset_type(source),deps,report)
        print(f"Imported {source}",flush=True)
    manifest={"schemaVersion":1,"sourceRoot":str(closure.root.resolve()),"characters":list(characters),"entries":entries,"diagnostics":closure.diagnostics}
    json_write(project/MANIFEST,manifest)
    artifacts=project/".artifacts/character-import"
    json_write(artifacts/"conversion-reports.json",conversion_reports)
    catalog = build_catalog(closure,characters,include_banks)
    json_write(artifacts/"catalog.json",catalog)
    json_write(project/"Config/CharacterAssets/catalog.generated.json",catalog)
    verify_manifest(project,manifest,source_root=closure.root)
    return manifest


def audio_events(document):
    events={}
    section=False
    event=None
    for line in document.splitlines():
        if line=="Events:":
            section=True
            continue
        if not section:continue
        if line and not line[0].isspace():break
        match=re.match(r"^    ([^ ].*?):(?: \[\])?$",line)
        if match:
            event="event:/"+match.group(1).replace("\\","/")
            events[event]=[]
        match=re.match(r"^        - (\S+)\s*$",line)
        if match and event:events[event].append(match.group(1))
    return events


def voice_keys(text):
    return set(re.findall(r"\\(voc_[^\\\s]+)\.wav",text))


def build_catalog(closure,characters,include_banks):
    voice_table=safe_source(closure.root,"sound/ja/voc1.audio_ja.yml")
    voice_text=voice_table.read_text(encoding="utf-8-sig")
    loaded_keys=voice_keys(voice_text)
    event_text=safe_source(closure.root,"sound/voc.event.yml").read_text(encoding="utf-8-sig")
    voice_events={}
    audio=audio_events(event_text)
    for event,keys in audio.items():
        for key in keys:voice_events.setdefault(key,event)
    for name in ("sfx1","gerneal"):
        document=safe_source(closure.root,f"sound/{name}.yml").read_text(encoding="utf-8-sig")
        for event,keys in audio_events(document).items():
            audio.setdefault(event,[]).extend(keys)
    records=[]
    used_events=set()
    for cid,(hero,name) in characters.items():
        keys=sorted(key for key in loaded_keys if key.startswith(f"voc_{cid}_"))
        timeline=f"model/{hero}.timeline"
        voices=[{"id":key,"audioTableKey":key,"eventPath":voice_events.get(key,""),"displayNameKey":"character.voice."+key} for key in keys]
        skills=[]
        for i,skill in enumerate(skill_ids(cid),1):
            effect=f"event:/skill/{skill}"
            voice=f"event:/voc/skill/{skill}"
            call=f"{voice}_c"
            answer=f"{voice}_a"
            skills.append({"id":skill,"displayNameKey":f"character.{hero}.skill{i}",
                "graphAddress":address(f"stagept/{skill}.stg"),"eventPath":effect if audio.get(effect) else "",
                "voiceEventPath":voice if audio.get(voice) else "",
                "dualCallVoiceEventPath":call if audio.get(call) else "",
                "dualAnswerVoiceEventPath":answer if audio.get(answer) else ""})
            used_events.update((effect,voice,call,answer))
        used_events.update(voice["eventPath"] for voice in voices)
        records.append({"id":cid,"name":name,"displayNameKey":f"character.{hero}.name",
            "battleSkeletonAddress":address(f"model/{hero}.scsp"),"portraitSkeletonAddress":address(f"portrait/{cid}.scsp"),
            "voiceBankAddress":address("sound/ja/voc1.audio_ja.bank") if include_banks else "",
            "timelineAddress":address(timeline) if timeline in closure.dependencies else "","voices":voices,"skills":skills})
    referenced_keys={re.sub(r"\((zhs|en|kr|ja|zht|zl)\)$","",key)
        for event in used_events for key in audio.get(event,()) if key.startswith("voc_")}
    missing=referenced_keys-loaded_keys
    if missing and include_banks:
        for table in sorted(voice_table.parent.glob("*.audio_ja.yml")):
            if table==voice_table:continue
            unloaded=missing & voice_keys(table.read_text(encoding="utf-8-sig"))
            if unloaded:
                raise ValueError(f"Japanese programmer keys require bank {table.name}: {', '.join(sorted(unloaded))}")
    return {"schemaVersion":1,"characters":records,"sharedBanks":[address(p) for p in BANKS] if include_banks else [],
        "missingJapaneseProgrammerKeys":sorted(missing) if include_banks else []}


def verify_manifest(project,manifest,source_root=None):
    project=Path(project).resolve();addresses=set();source_hashes={}
    for item in manifest["entries"]:
        if item["address"] in addresses:raise ValueError("Duplicate asset address "+item["address"])
        addresses.add(item["address"])
        conversion=item.get("conversion",{})
        if item.get("type")=="spine-json" and conversion.get("spineVersion")=="2.1.27":
            if conversion.get("curveSemantics")!="scsp-v2-sentinel/1" or not isinstance(conversion.get("curveTimelines"),dict):
                raise ValueError("Missing SCSP v2 curve audit; reimport source: "+item["path"])
        output=(project/item["path"]).resolve()
        if not output.is_relative_to((project/RES).resolve()):raise ValueError("Manifest output outside Res")
        if hash_file(output)!=item["outputSha256"]:raise ValueError("Output hash mismatch: "+item["path"])
        if source_root:
            if item["source"] not in source_hashes:source_hashes[item["source"]]=hash_file(safe_source(Path(source_root),item["source"]))
            if source_hashes[item["source"]]!=item["sourceSha256"]:raise ValueError("Source hash mismatch")
    for item in manifest["entries"]:
        for dep in item["dependencies"]:
            if dep not in addresses:raise ValueError("Unresolved address "+dep)


if __name__=="__main__":
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source",type=Path,default=Path("D:/sevenepic/原始资源"));parser.add_argument("--project",type=Path,default=Path(__file__).resolve().parents[2])
    parser.add_argument("--characters",nargs="+",default=list(CHARACTERS));parser.add_argument("--closure-only",action="store_true");parser.add_argument("--no-banks",action="store_true");parser.add_argument("--verify",action="store_true")
    args=parser.parse_args()
    if args.verify:
        verify_manifest(args.project,json.loads((args.project/MANIFEST).read_text(encoding="utf-8")),args.source);print("Manifest verified")
    elif args.closure_only:
        closure=Closure(args.source).build(select_characters(args.characters),not args.no_banks)
        print(json.dumps({"assets":len(closure.dependencies),"shared":sum(len(v)>1 for v in closure.owners.values()),"types":dict(Counter(PurePosixPath(p).suffix for p in closure.dependencies)),"sources":sorted(closure.dependencies),"diagnostics":closure.diagnostics},ensure_ascii=False,indent=2))
    else:import_assets(args.source,args.project,args.characters,not args.no_banks)
