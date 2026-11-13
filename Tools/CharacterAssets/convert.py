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
SCSP_CONVERTER_VERSION = "astraloath-scsp/3"
ORDERED_TRANSLATE_VERSION = "astraloath-scsp/4"
ORDERED_TRANSLATE_FIELD = "astralOathTranslateTracks"


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
        self.curveBlocks = []

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
    # E7's native 2.1.27 loader uses two distinct pointer sentinels:
    # FFFF -> -1 -> linear; FFFE -> NULL -> stepped. Explicit Beziers carry
    # a float FRAME INDEX, then four control points (not cached sample data).
    # Independently documented from FUN_14032bc30 / FUN_140b71470 at:
    # https://github.com/Aikiooo/e7codex/blob/1edf249f5b8dbda734ea96432e22ab108a8e308c/convert/vendor/epic7_scsp2json_v1_0/_epic7_scsp2json_v2_1_27.py#L344-L359
    start = reader.pos
    if count < 1: raise ValueError("Empty 2.1 curve timeline")
    marker = reader.read_u16()
    curves = [{} for _ in range(count - 1)]
    if marker == 0xfffe:
        curves = [{"curve": "stepped"} for _ in curves]
    elif marker != 0xffff:
        if marker != count:
            raise ValueError(f"2.1 curve count mismatch at {start}: {marker}/{count}")
        for index in range(marker - 1):
            kind = reader.read_u8()
            if kind == 0:
                continue  # Native zero-filled cache: no write to another target.
            if kind == 1:
                curves[index] = {"curve": "stepped"}
            elif kind == 2:
                target = reader.read_f32()
                if not math.isfinite(target) or not target.is_integer() or not 0 <= target < count - 1:
                    raise ValueError(f"Invalid 2.1 curve target index at {reader.pos-4}: {target}")
                controls = [reader.read_f32() for _ in range(4)]
                if not all(math.isfinite(v) for v in controls):
                    raise ValueError("Nonfinite 2.1 curve control point")
                curves[int(target)] = {"curve": controls}
            else:
                raise ValueError(f"Unknown 2.1 curve type {kind} at {reader.pos-1}")
    # Immutable copy: writer mutations cannot alter the source evidence.
    reader.curveBlocks.append({"offset": start, "end": reader.pos, "marker": marker,
                               "frames": count, "values": tuple(_curve_value(f) for f in curves)})
    return curves


def _curve_value(frame):
    value = frame.get("curve")
    return tuple(value) if isinstance(value, list) else value


def _record_curve_source(reader, skeleton, animation, kind, start, previous_blocks):
    if kind == 4:
        return
    if len(reader.curveBlocks) != previous_blocks + 1:
        raise ValueError("Missing or duplicated source curve block")
    block = reader.curveBlocks[-1]
    index = struct.unpack_from("<I", reader.data, start + 4)[0]
    if kind in (0, 1):
        path = (animation.name, "bones", skeleton.bones[index].name, {0:"scale", 1:"rotate"}[kind])
    elif kind == 3:
        path = (animation.name, "slots", skeleton.slots[index].name, "color")
    elif kind == 8:
        path = (animation.name, "ik", skeleton.ikConstraints[index].name)
    elif kind == 7:
        record_index = struct.unpack_from("<I", reader.data, block["end"])[0]
        record = skeleton.v2_skin_records[record_index]
        path = (animation.name, "ffd", record["skin"], record["skin_slot"], record["skin_attachment"])
    else:
        raise ValueError(f"Unsupported curve timeline {kind}")
    if path in skeleton.curveSource:
        raise ValueError(f"Duplicate curve timeline: {path}")
    skeleton.curveSource[path] = block


def _read_translate_v2(reader, skeleton, animation):
    bone_index, raw_count = reader.read_u32(), reader.read_u32()
    if bone_index >= len(skeleton.bones):
        raise ValueError("Invalid 2.1 translate bone index")
    if raw_count < 3 or raw_count % 3 or raw_count * 4 + 2 > len(reader.data) - reader.pos:
        raise ValueError("Invalid 2.1 translate float count")
    bone = skeleton.bones[bone_index].name
    entries, source_keys = [], []
    previous = 0.0
    for _ in range(raw_count // 3):
        time, x, y = reader.read_f32(), reader.read_f32(), reader.read_f32()
        if time < previous or not all(map(math.isfinite, (time, x, y))):
            raise ValueError("Invalid 2.1 translate frame")
        source_keys.append((time, x, y))
        frame = {"time": time}
        if x != 0:
            frame["x"] = x
        if y != 0:
            frame["y"] = y
        entries.append(frame)
        previous = time
    for frame, curve in zip(entries, _curves_v2(reader, len(entries))):
        frame.update(curve)
    animation.duration = max(animation.duration, previous)

    # The native loader appends every record by ordinal, including equal and
    # overlapping tracks for the same bone. Standard JSON has room for one.
    timelines = animation.bones.setdefault(bone, {})
    if "translate" in timelines:
        additional = skeleton.orderedTranslate.setdefault(animation.name, {}).setdefault(bone, [])
        path = (animation.name, ORDERED_TRANSLATE_FIELD, bone, len(additional))
        additional.append(entries)
    else:
        path = (animation.name, "bones", bone, "translate")
        timelines["translate"] = entries
    skeleton.curveSource[path] = {**reader.curveBlocks[-1], "keys": tuple(source_keys)}


def _validate_curves_v2(document, skeleton):
    for path, block in skeleton.curveSource.items():
        frames = document["animations"]
        try:
            for key in path: frames = frames[key]
        except (KeyError, TypeError) as exc:
            raise ValueError(f"Missing converted curve timeline: {path}") from exc
        if len(frames) != block["frames"] or tuple(_curve_value(f) for f in frames[:-1]) != block["values"]:
            raise ValueError(f"Curve mapping mismatch between source and JSON: {path}")
        if "curve" in frames[-1]:
            raise ValueError(f"Unexpected terminal curve in JSON: {path}")
        if "keys" in block:
            keys = tuple((f.get("time", 0), f.get("x", 0), f.get("y", 0)) for f in frames)
            if keys != block["keys"]:
                raise ValueError(f"Translate key mapping mismatch between source and JSON: {path}")


def _curve_report(skeleton):
    result = {}
    for path, block in getattr(skeleton, "curveSource", {}).items():
        row = result.setdefault(path[0], {"tracks":0, "frames":0, "segments":0,
            "sourceMarkers":{"allLinear":0,"allStepped":0,"explicit":0},
            "modes":{"linear":0,"stepped":0,"bezier":0}})
        row["tracks"] += 1
        row["frames"] += block["frames"]
        row["segments"] += len(block["values"])
        mode = {0xffff:"allLinear", 0xfffe:"allStepped"}.get(block["marker"], "explicit")
        row["sourceMarkers"][mode] += 1
        for value in block["values"]:
            row["modes"]["linear" if value is None else "stepped" if value == "stepped" else "bezier"] += 1
    return result


def _draw_order(order, slots):
    if sorted(order) != list(range(len(slots))):
        raise ValueError("Draw order is not a complete permutation")
    # Explicitly move every slot. Spine accepts zero offsets; this avoids
    # heuristic inverse permutations and preserves arbitrary reordering.
    inverse = {slot: position for position, slot in enumerate(order)}
    return [{"slot": slot.name, "offset": inverse[index] - index}
            for index,slot in enumerate(slots)]


def _flip_v2(reader, skeleton, animation, kind):
    # Official spine-c 2.1: Animation.h spTimelineType 9/10 = FLIPX/FLIPY,
    # spFlipTimeline stores x, framesCount and interleaved time/flip floats.
    # https://github.com/EsotericSoftware/spine-runtimes/blob/142e770e5b4b42395a3a0a68ff3c8a21ba2774eb/spine-c/include/spine/Animation.h
    bone_index = reader.read_u32()
    is_x = reader.read_u32()
    count = reader.read_u32()
    if bone_index >= len(skeleton.bones) or is_x != int(kind == 9):
        raise ValueError("Invalid flip bone index or axis marker")
    if count == 0 or count % 2 or count > (len(reader.data) - reader.pos) // 4:
        raise ValueError("Invalid flip frame array length")
    bone = skeleton.bones[bone_index].name
    axis, field = ("flipX", "x") if kind == 9 else ("flipY", "y")
    timelines = animation.bones.setdefault(bone, {})
    if axis in timelines:
        raise ValueError(f"Duplicate flip timeline: {animation.name}/{bone}/{axis}")
    frames = []
    previous = -1.0
    for _ in range(count // 2):
        time, value = reader.read_f32(), reader.read_f32()
        if not math.isfinite(time) or time < 0 or time < previous or value not in (0.0, 1.0):
            raise ValueError("Invalid flip time or boolean value")
        frames.append({"time": time, field: bool(value)})
        previous = time
    timelines[axis] = frames
    # Keep independent immutable evidence so the writer cannot drop/mutate a track unnoticed.
    skeleton.flipSource[(animation.name, bone, axis)] = tuple((f["time"], f[field]) for f in frames)


def _validate_flips_v2(document, skeleton):
    actual = {}
    for name, animation in document.get("animations", {}).items():
        for bone, timelines in animation.get("bones", {}).items():
            for axis, field in (("flipX", "x"), ("flipY", "y")):
                if axis in timelines:
                    actual[(name, bone, axis)] = tuple((f.get("time", 0), f.get(field)) for f in timelines[axis])
    if actual != skeleton.flipSource:
        raise ValueError("Flip timeline mapping mismatch between source records and JSON")


def _flip_report(skeleton):
    result = {}
    for (animation, bone, axis), frames in getattr(skeleton, "flipSource", {}).items():
        axes = result.setdefault(animation, {key: {"tracks": 0, "frames": 0, "bones": {}}
                                             for key in ("flipX", "flipY")})
        item = axes[axis]
        item["tracks"] += 1
        item["frames"] += len(frames)
        item["bones"][bone] = len(frames)
    return result


def _animations_v2(reader, skeleton):
    result = []
    skeleton.sourceTimelineCounts = {}
    skeleton.flipSource = {}
    skeleton.curveSource = {}
    skeleton.orderedTranslate = {}
    for ai in range(skeleton.v2_anim_count):
        animation = common.AnimationData()
        animation.name = _string(reader.read_u32(), skeleton)
        animation.duration = reader.read_f32()
        elements = reader.read_u32()
        if not animation.name or elements > 100000 or not math.isfinite(animation.duration):
            raise ValueError(f"Invalid animation header #{ai} at {reader.pos-12}")
        counts = {}
        skeleton.sourceTimelineCounts[animation.name] = counts
        for ti in range(elements):
            start = reader.pos
            kind = reader.read_u32()
            counts[str(kind)] = counts.get(str(kind), 0) + 1
            if kind in (9, 10):
                _flip_v2(reader, skeleton, animation, kind)
                continue
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
            if kind == 2:
                _read_translate_v2(reader, skeleton, animation)
                continue
            # The vendored reference mislabels native 5/6/9/10. Never delegate these
            # or unknown IDs to its permissive fallback branches.
            previous_blocks = len(reader.curveBlocks)
            if kind not in (0, 1, 3, 4, 7, 8) or not v2._parse_v2_timeline_entry(reader,skeleton,animation,kind):
                raise ValueError(f"Unsupported/incomplete timeline {kind} in {animation.name} at {start}")
            _record_curve_source(reader, skeleton, animation, kind, start, previous_blocks)
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
        ordered_translate = getattr(sk, "orderedTranslate", {})
        if version != 30001:
            for name, tracks in ordered_translate.items():
                document["animations"][name][ORDERED_TRANSLATE_FIELD] = tracks
            _validate_flips_v2(document, sk)
            _validate_curves_v2(document, sk)
        _validate_json(document)
        if len(document.get("animations",{}))!=len(sk.animations): raise ValueError("Animation count mismatch")
    except (struct.error,IndexError,KeyError,OverflowError,UnicodeError) as exc:
        raise ValueError(f"Invalid SCSP record at byte {reader.pos}: {exc}") from exc
    converter_version = ORDERED_TRANSLATE_VERSION if ordered_translate else SCSP_CONVERTER_VERSION
    report={"converterVersion":converter_version,"spineVersion":sk.version,"bones":len(sk.bones),"slots":len(sk.slots),
            "renderAttachments":sum(isinstance(a,(common.RegionAttachment,common.MeshAttachment,common.SkinnedMeshAttachment,common.LinkedMeshAttachment)) for skin in sk.skins for slots in skin.attachments.values() for a in slots.values()),
            "animationCount":len(sk.animations),"animations":[{"name":a.name,"duration":a.duration} for a in sk.animations],
            "binaryBytes":binary_size,"consumedBytes":reader.pos,"opaqueFields":reader.opaque,
            "engineDrawOrder":sk.engineDrawOrder,
            "sourceTimelineCounts":getattr(sk, "sourceTimelineCounts", {}),
            "boneFlipTimelines":_flip_report(sk),
            "curveTimelines":_curve_report(sk)}
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


def restore_white_pma_regions(image: Image.Image, bounds) -> None:
    """Restore neutral PMA color from the unquantized alpha of audited atlas regions."""
    import numpy as np

    restored = []
    for x, y, width, height in bounds:
        if (min(x, y) < 0 or min(width, height) <= 0 or
                x + width > image.width or y + height > image.height):
            raise ValueError(f"Invalid white PMA region bounds: {(x, y, width, height)}")
        box = (x, y, x + width, y + height)
        rgba = np.array(image.crop(box))
        rgb = rgba[:, :, :3].astype(np.int16)
        alpha = rgba[:, :, 3].astype(np.int16)
        # The two RGB565 quantization steps can disagree by up to four codes
        # even for white, while a five-bit channel can be seven codes from A.
        if (np.ptp(alpha) == 0 or np.any(np.abs(rgb - alpha[:, :, None]) > 7) or
                np.any(np.ptp(rgb, axis=2) > 4) or np.any(rgb[alpha == 0])):
            raise ValueError(f"Region is not an authored neutral white PMA mask: {box}")
        rgba[:, :, :3] = rgba[:, :, 3:4]
        restored.append((Image.fromarray(rgba), (x, y)))
    for region, position in restored:
        image.paste(region, position)


def convert_sct(source:Path,destination:Path,*,white_pma_regions=None)->dict:
    data=Path(source).read_bytes(); image,report=decode_sct(data)
    if white_pma_regions:
        if report["encoding"] != "SCT1:RGB565+A8":
            raise ValueError(f"White PMA restoration requires RGB565+A8: {source}")
        restore_white_pma_regions(image, white_pma_regions.values())
        report["whitePmaRegions"] = list(white_pma_regions)
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
