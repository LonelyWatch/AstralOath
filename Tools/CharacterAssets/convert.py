"""Strict, version-preserving SCSP and SCT conversion.

Unknown formats, incomplete records and duplicate animation names fail closed.
Source-only binary fields (cached atlas geometry etc.) are recorded explicitly in
opaqueFields; they are never advertised as validated Spine gameplay semantics.
"""
from __future__ import annotations
import argparse
import hashlib
import json
import math
import struct
from pathlib import Path
import lz4.block
from PIL import Image
from ThirdParty.scsp_viewer import scsp_common as common
from ThirdParty.scsp_viewer import scsp_v2 as v2
from ThirdParty.scsp_viewer import scsp_v3 as v3
from ThirdParty.scsp_viewer import scsp2json as writer

MAX_DECODED = 256 * 1024 * 1024


def _unpack_block(data: bytes) -> bytes:
    if len(data) < 8:
        raise ValueError("Truncated LZ4 container header")
    size, packed = struct.unpack_from("<II", data)
    if not 0 < size <= MAX_DECODED or packed != len(data) - 8:
        raise ValueError(f"Invalid LZ4 lengths: decoded={size}, packed={packed}, actual={len(data)-8}")
    try:
        decoded = lz4.block.decompress(data[8:], uncompressed_size=size)
    except Exception as exc:
        raise ValueError("Invalid LZ4 block") from exc
    if len(decoded) != size:
        raise ValueError("LZ4 decoded length mismatch")
    return decoded


class Reader(common.SpineBinaryReader):
    def __init__(self, data):
        super().__init__(data)
        self.opaque = []

    def reset_pos(self, pos):
        if pos < 0 or pos > len(self.data): raise ValueError("Seek outside binary")
        if pos > self.pos:
            self.opaque.append({"offset":self.pos,"size":pos-self.pos,"hex":self.data[self.pos:pos].hex()})
        super().reset_pos(pos)

    def skip(self, count):
        if count < 0 or self.pos + count > len(self.data):
            raise ValueError(f"Skip outside binary at {self.pos}: {count}")
        self.opaque.append({"offset": self.pos, "size": count,
                            "hex": self.data[self.pos:self.pos+count].hex()})
        super().skip(count)


def _string(offset, skeleton):
    if offset == 0xffffffff:
        return None
    pool = skeleton.stringPool
    if not 0 <= offset < len(pool) or (offset > 0 and pool[offset-1] != 0):
        raise ValueError(f"Invalid string pool reference {offset}")
    end = pool.find(b"\0", offset)
    if end == -1:
        raise ValueError(f"Unterminated pool string at {offset}")
    return pool[offset:end].decode("utf-8",errors="strict")


def _curves_v2(reader, count):
    marker = reader.read_u16()
    if marker in (0xffff, 0xfffe):
        return [{} for _ in range(max(0, count - 1))]
    curves = []
    for _ in range(count - 1):
        kind = reader.read_u8()
        if kind == 0: curves.append({})
        elif kind == 1: curves.append({"curve": "stepped"})
        elif kind == 2:
            reader.skip(4)  # cached curve segment count
            curves.append({"curve": [reader.read_f32() for _ in range(4)]})
        else: raise ValueError(f"Unknown 2.1 curve type {kind} at {reader.pos-1}")
    return curves


def _draw_order(order, slots):
    if sorted(order) != list(range(len(slots))):
        raise ValueError("Draw order is not a complete permutation")
    # Explicitly move every slot. Spine accepts zero offsets; this avoids
    # heuristic inverse permutations and preserves arbitrary reordering.
    inverse = {slot: position for position, slot in enumerate(order)}
    return [{"slot": slot.name, "offset": inverse[index] - index}
            for index,slot in enumerate(slots)]


def _animations_v2(reader, skeleton):
    result = []
    for ai in range(skeleton.v2_anim_count):
        animation = common.AnimationData()
        animation.name = _string(reader.read_u32(), skeleton)
        animation.duration = reader.read_f32()
        elements = reader.read_u32()
        if not animation.name or elements > 100000 or not math.isfinite(animation.duration):
            raise ValueError(f"Invalid animation header #{ai} at {reader.pos-12}")
        for ti in range(elements):
            start = reader.pos
            kind = reader.read_u32()
            # The engine's terminal type 6 is a precomputed draw-order timeline,
            # encoded as slotCount, frameCount, times, presence+permutation pairs.
            if kind == 5:
                count = reader.read_u32()
                times = common.read_f32_array(reader,count)
                for t in times:
                    integer=reader.read_i32(); floating=reader.read_f32()
                    text=_string(reader.read_u32(),skeleton); event_index=reader.read_u16()
                    event=skeleton.events[event_index]
                    animation.events.append({"time":t,"name":event.name,"int":integer,"float":floating,"string":text or ""})
                continue
            if kind == 6:
                if struct.unpack_from("<I",reader.data,reader.pos)[0] != len(skeleton.slots):
                    raise ValueError("Draw-order slot count mismatch")
                reader.read_u32()
                count = reader.read_u32()
                times = common.read_f32_array(reader,count)
                for t in times:
                    flag = reader.read_u8()
                    frame = {"time":t}
                    if flag == 1:
                        order = common.read_u32_array(reader,len(skeleton.slots))
                        frame["offsets"] = _draw_order(order,skeleton.slots)
                    elif flag != 0: raise ValueError(f"Unknown draw-order flag {flag}")
                    animation.drawOrder.append(frame)
                continue
            if not v2._parse_v2_timeline_entry(reader,skeleton,animation,kind):
                raise ValueError(f"Unsupported/incomplete timeline {kind} in {animation.name} at {start}")
        if animation._v2_draworder_raw:
            # Engine auxiliary visibility/order state is retained for diagnosis;
            # terminal permutation frames are the authoritative compiled order.
            if not animation.drawOrder:
                raise ValueError(f"Draw-order auxiliary data lacks permutation timeline: {animation.name}")
            skeleton.engineDrawOrder[animation.name] = animation._v2_draworder_raw
        result.append(animation)
    skeleton.animations = result


def _curve_v3(curves, frame):
    at = frame*19
    kind = curves[at]
    if kind == 0: return {}
    if kind == 1: return {"curve":"stepped"}
    if kind == 2:
        # Spine 3.8 caches nine points at t=.1 through .9, not the original
        # control points. Recover the two controls per axis by least squares
        # with endpoints (0,0) and (1,1), then verify every cached sample.
        samples=curves[at+1:at+19]
        if len(samples)!=18: raise ValueError("Incomplete cubic samples")
        a=[]; b=[]; endpoints=[]
        for i in range(1,10):
            t=i/10; a.append(3*(1-t)**2*t); b.append(3*(1-t)*t*t); endpoints.append(t**3)
        aa=sum(v*v for v in a); bb=sum(v*v for v in b); ab=sum(x*y for x,y in zip(a,b))
        determinant=aa*bb-ab*ab
        controls=[]
        for axis in (0,1):
            values=[samples[i*2+axis]-endpoints[i] for i in range(9)]
            av=sum(x*y for x,y in zip(a,values)); bv=sum(x*y for x,y in zip(b,values))
            first=(av*bb-bv*ab)/determinant; second=(bv*aa-av*ab)/determinant
            if any(abs(first*a[i]+second*b[i]-values[i])>0.00002 for i in range(9)):
                raise ValueError("Curve samples cannot be represented by one cubic")
            controls.append((first,second))
        return {"curve":controls[0][0],"c2":controls[1][0],"c3":controls[0][1],"c4":controls[1][1]}
    raise ValueError(f"Unknown 3.8 curve type {kind}")


# Bind strict string and curve helpers in modules that imported them directly.
for module in (common,v2,v3,writer): module.get_pool_string = _string
v2._read_v2_curves = _curves_v2
v3.write_curve_v3 = _curve_v3


def _validate_json(document):
    bones = {bone["name"] for bone in document["bones"]}
    slots = {slot["name"] for slot in document["slots"]}
    if len(bones)!=len(document["bones"]) or len(slots)!=len(document["slots"]):
        raise ValueError("Duplicate bone or slot names")
    for bone in document["bones"]:
        if bone.get("parent",next(iter(bones))) not in bones: raise ValueError("Invalid parent bone")
    for slot in document["slots"]:
        if slot.get("bone") not in bones: raise ValueError("Invalid slot bone")
    for name, animation in document.get("animations",{}).items():
        for key,names in (("bones",bones),("slots",slots)):
            if not set(animation.get(key,{})) <= names: raise ValueError(f"Invalid {key} in {name}")
    def visit(value):
        if isinstance(value,float) and not math.isfinite(value): raise ValueError("Non-finite JSON float")
        if isinstance(value,dict):
            for child in value.values(): visit(child)
        elif isinstance(value,list):
            for child in value: visit(child)
    visit(document)


def decode_scsp(data: bytes):
    unpacked = _unpack_block(data)
    if len(unpacked)<8: raise ValueError("Truncated SCSP sections")
    binary_size,pool_size = struct.unpack_from("<II",unpacked)
    if binary_size+pool_size+8 != len(unpacked): raise ValueError("SCSP section length mismatch")
    binary=unpacked[8:8+binary_size]
    sk=common.SkeletonData(); sk.stringPool=unpacked[8+binary_size:]; sk.engineDrawOrder={}
    reader=Reader(binary)
    try:
        if binary[:4]==b"scsp":
            version=struct.unpack_from("<I",binary,4)[0]
            if version not in (1,2,30001): raise ValueError(f"Unsupported SCSP format {version}")
        else: version=1
        if version==30001:
            sk.scspVersion=common.ScspVersion.V3
            v3.read_skeleton_info_v3(reader,sk); v3.read_scsp_v3(reader,sk)
            # Every source timeline must map to a complete JSON timeline.
            for animation in sk.animations:
                for timeline in animation.timelines:
                    if not timeline.times: raise ValueError(f"Empty timeline in {animation.name}")
                    if timeline.curves and len(timeline.curves)!=(len(timeline.times)-1)*19:
                        raise ValueError(f"Curve count mismatch in {animation.name}")
        else:
            sk.scspVersion=common.ScspVersion.V2
            v2.read_skeleton_info_v2(reader,sk)
            for parse in (v2.read_bones_v2,v2.read_iks_v2,v2.read_slots_v2,v2.read_skins_v2,v2.read_events_v2): parse(reader,sk)
            _animations_v2(reader,sk)
        if reader.pos!=len(binary): raise ValueError(f"Unconsumed binary bytes: {reader.pos}/{len(binary)}")
        expected_version="3.8.99" if version==30001 else "2.1.27"
        if sk.version!=expected_version: raise ValueError(f"Unsupported Spine version {sk.version}")
        if len({a.name for a in sk.animations})!=len(sk.animations): raise ValueError("Duplicate animation names")
        document=writer.write_json_data(sk)
        _validate_json(document)
        if len(document.get("animations",{}))!=len(sk.animations): raise ValueError("Animation count mismatch")
    except (struct.error,IndexError,KeyError,OverflowError,UnicodeError) as exc:
        raise ValueError(f"Invalid SCSP record at byte {reader.pos}: {exc}") from exc
    report={"spineVersion":sk.version,"bones":len(sk.bones),"slots":len(sk.slots),
            "renderAttachments":sum(isinstance(a,(common.RegionAttachment,common.MeshAttachment,common.SkinnedMeshAttachment,common.LinkedMeshAttachment)) for skin in sk.skins for slots in skin.attachments.values() for a in slots.values()),
            "animationCount":len(sk.animations),"animations":[{"name":a.name,"duration":a.duration} for a in sk.animations],
            "binaryBytes":binary_size,"consumedBytes":reader.pos,"opaqueFields":reader.opaque,
            "engineDrawOrder":sk.engineDrawOrder}
    return document,report


def decode_sct(data: bytes):
    if data[:4]==b"SCT2":
        if len(data)<72: raise ValueError("Truncated SCT2 header")
        header_size=struct.unpack_from("<I",data,12)[0]
        kind=struct.unpack_from("<I",data,20)[0]
        width,height=struct.unpack_from("<HH",data,24)
        if not 0<width<=16384 or not 0<height<=16384: raise ValueError("Invalid texture dimensions")
        if header_size!=72: raise ValueError(f"Unsupported SCT2 header length {header_size}")
        if kind not in (19,40,44,47): raise ValueError(f"Unsupported SCT2 format {kind}")
        payload=data[header_size:]
        if kind==44: expected=width*height*4
        else:
            block=8 if kind==47 else 4
            expected=((width+block-1)//block)*((height+block-1)//block)*16
        raw=payload if len(payload)==expected else _unpack_block(payload)
        if len(raw)!=expected: raise ValueError("SCT2 texture payload length mismatch")
        if kind==44: image=Image.frombytes("RGBA",(width,height),raw)
        else:
            import texture2ddecoder
            pixels=texture2ddecoder.decode_etc2a8(raw,width,height) if kind==19 else texture2ddecoder.decode_astc(raw,width,height,block,block)
            image=Image.frombytes("RGBA",(width,height),pixels,"raw","BGRA")
        encoding=f"SCT2:{kind}"
    else:
        if len(data)<17 or data[:3]!=b"SCT": raise ValueError("Invalid SCT header")
        version,kind,width,height,decoded,packed=struct.unpack_from("<BBHHII",data,3)
        if not 0<width<=16384 or not 0<height<=16384: raise ValueError("Invalid texture dimensions")
        if version!=1 or kind not in (2,4): raise ValueError(f"Unsupported SCT version/format {version}/{kind}")
        if packed!=len(data)-17 or decoded!=width*height*(4 if kind==2 else 3): raise ValueError("SCT payload length mismatch")
        raw=_unpack_block(struct.pack("<II",decoded,packed)+data[17:])
        if kind==2:
            image=Image.frombytes("RGBA",(width,height),raw)
            return image,{"width":width,"height":height,"encoding":"SCT1:RGBA8888","alpha":"source-preserved"}
        import numpy as np
        colors=np.frombuffer(raw[:width*height*2],dtype="<u2")
        red=(colors>>11)&31; green=(colors>>5)&63; blue=colors&31
        rgba=np.empty((width*height,4),dtype=np.uint8)
        rgba[:,0]=(red<<3)|(red>>2); rgba[:,1]=(green<<2)|(green>>4); rgba[:,2]=(blue<<3)|(blue>>2)
        rgba[:,3]=np.frombuffer(raw[width*height*2:],dtype=np.uint8)
        image=Image.frombytes("RGBA",(width,height),rgba.tobytes())
        encoding="SCT1:RGB565+A8"
    if not 0<width<=16384 or not 0<height<=16384: raise ValueError("Invalid texture dimensions")
    return image,{"width":width,"height":height,"encoding":encoding,"alpha":"source-preserved"}


def convert_scsp(source:Path,destination:Path)->dict:
    source=Path(source); destination=Path(destination)
    data=source.read_bytes(); document,report=decode_scsp(data)
    destination.parent.mkdir(parents=True,exist_ok=True)
    destination.write_text(json.dumps(document,ensure_ascii=False,separators=(",",":"),allow_nan=False),encoding="utf-8")
    report.update(sourceSha256=hashlib.sha256(data).hexdigest(),outputSha256=hashlib.sha256(destination.read_bytes()).hexdigest())
    return report


def convert_sct(source:Path,destination:Path)->dict:
    data=Path(source).read_bytes(); image,report=decode_sct(data)
    destination=Path(destination); destination.parent.mkdir(parents=True,exist_ok=True)
    image.save(destination,format="PNG")
    report.update(sourceSha256=hashlib.sha256(data).hexdigest(),outputSha256=hashlib.sha256(destination.read_bytes()).hexdigest())
    return report


if __name__=="__main__":
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source",type=Path); parser.add_argument("destination",type=Path)
    parser.add_argument("--report",type=Path)
    args=parser.parse_args()
    report=(convert_scsp if args.source.suffix.lower()==".scsp" else convert_sct)(args.source,args.destination)
    text=json.dumps(report,ensure_ascii=False,indent=2,allow_nan=False)
    if args.report: args.report.write_text(text,encoding="utf-8")
    else: print(text)
