"""
V2 (Spine 2.1.27) SCSP reading functions.
"""

from __future__ import annotations

import logging

import math

import struct

import numpy as np

from .scsp_common import (
    SpineBinaryReader, SkeletonData, AnimationData,
    BoneData, SlotData, IKConstraintData, SkinData, EventData,
    Attachment, VertexAttachment, RegionAttachment, MeshAttachment,
    BoundingBoxAttachment, SkinnedMeshAttachment,
    BlendMode, CurveType, AttachmentType, V2AttachmentType, V2TimelineType,
    get_pool_string, read_f32_array, read_u8_array,
    read_u16_array, read_u32_array,
    f32_color, color_to_string,
)

from collections import defaultdict

from typing import Any, Dict, List, Optional, Tuple, Set

def read_skeleton_info_v2(r: SpineBinaryReader, sk: SkeletonData) -> None:
    """Two V2 sub-layouts exist:

    WITH magic ("scsp" at spine_data[0:4], version u32 at [4:8]):
      [8]   _unknown (88)
      [12]  width        f32
      [16]  height       f32
      [20]  extra_floats (4 × f32, 16 bytes)
      [36]  bone_count … anim_count (6 × u32, 24 bytes)
      [60]  (40 bytes reserved)
      [100] hash_off     u32
      [104] ver_off      u32
      [108] bone data begins

    WITHOUT magic (data starts directly):
      [0]   bone_count … anim_count (6 × u32, 24 bytes)
      [24]  (40 bytes reserved)
      [64]  width        f32
      [68]  height       f32
      [72]  hash_off     u32
      [76]  ver_off      u32
      [80]  bone data begins
    """
    has_magic = r.data[:4] == b"scsp"
    sk.v2_has_magic = has_magic

    if has_magic:
        r.reset_pos(8)
        _unknown = r.read_u32()
        sk.width = r.read_f32()
        sk.height = r.read_f32()
        r.skip(16)  # 4 extra floats
        sk.v2_bone_count = r.read_u32()
        sk.v2_ik_count = r.read_u32()
        sk.v2_slot_count = r.read_u32()
        sk.v2_skin_count = r.read_u32()
        sk.v2_event_count = r.read_u32()
        sk.v2_anim_count = r.read_u32()
        r.skip(40)
        hash_off = r.read_u32()
        ver_off = r.read_u32()
    else:
        r.reset_pos(0)
        sk.v2_bone_count = r.read_u32()
        sk.v2_ik_count = r.read_u32()
        sk.v2_slot_count = r.read_u32()
        sk.v2_skin_count = r.read_u32()
        sk.v2_event_count = r.read_u32()
        sk.v2_anim_count = r.read_u32()
        r.skip(40)
        sk.width = r.read_f32()
        sk.height = r.read_f32()
        hash_off = r.read_u32()
        ver_off = r.read_u32()

    sk._v2_used_pool_offsets.update((hash_off, ver_off))
    sk.hashString = get_pool_string(hash_off, sk)
    ver_str = get_pool_string(ver_off, sk)
    if ver_str and ".scsp" in ver_str:
        sk.version = ver_str.replace(".scsp", "")
    else:
        sk.version = ver_str

def read_bones_v2(r: SpineBinaryReader, sk: SkeletonData) -> None:
    bones: List[BoneData] = []
    for _ in range(sk.v2_bone_count):
        bone = BoneData()
        bone.length = r.read_f32()
        bone.x = r.read_f32()
        bone.y = r.read_f32()
        bone.rotation = r.read_f32()
        bone.scaleX = r.read_f32()
        bone.scaleY = r.read_f32()
        bone.flipX = r.read_u32() > 0
        bone.flipY = r.read_u32() > 0
        bone.inheritScale = r.read_u32() > 0
        bone.inheritRotation = r.read_u32() > 0
        name_off = r.read_u32()
        parent_idx = r.read_u16()
        sk._v2_used_pool_offsets.add(name_off)
        bone.name = get_pool_string(name_off, sk)
        if parent_idx != 0xffff and parent_idx >= len(bones):
            raise ValueError("Invalid V2 parent index")
        if parent_idx < len(bones):
            bone.parent = bones[parent_idx].name
        bones.append(bone)
    sk.bones = bones

def read_slots_v2(r: SpineBinaryReader, sk: SkeletonData) -> None:
    slots: List[SlotData] = []
    for _ in range(sk.v2_slot_count):
        slot = SlotData()
        name_off = r.read_u32()
        bone_idx = r.read_u16()
        att_off = r.read_u32()
        cr, cg, cb, ca = r.read_f32(), r.read_f32(), r.read_f32(), r.read_f32()
        blend = r.read_u32()
        sk._v2_used_pool_offsets.add(name_off)
        if att_off != 0xFFFFFFFF:
            sk._v2_used_pool_offsets.add(att_off)
        slot.name = get_pool_string(name_off, sk)
        slot.bone = sk.bones[bone_idx].name if 0 <= bone_idx < len(sk.bones) else (_ for _ in ()).throw(ValueError("Invalid slot bone index"))
        slot.color = f32_color(cr, cg, cb, ca)
        slot.attachmentName = get_pool_string(att_off, sk) if att_off != 0xFFFFFFFF else None
        if blend not in (0,1,2,3): raise ValueError(f"Unsupported Spine 2.1 blend mode {blend}")
        slot.blendMode = BlendMode(blend)
        slots.append(slot)
    sk.slots = slots

def read_iks_v2(r: SpineBinaryReader, sk: SkeletonData) -> None:
    iks: List[IKConstraintData] = []
    for _ in range(sk.v2_ik_count):
        ik = IKConstraintData()
        name_off = r.read_u32()
        bone_count = r.read_u32() if not sk.v2_has_magic else r.read_u16()
        bone_idxs = read_u16_array(r, bone_count)
        target_idx = r.read_u16()
        ik.mix = r.read_f32()
        bend = r.read_i32()
        sk._v2_used_pool_offsets.add(name_off)
        ik.name = get_pool_string(name_off, sk)
        ik.bendPositive = bend > 0
        ik.target = sk.bones[target_idx].name if 0 <= target_idx < len(sk.bones) else (_ for _ in ()).throw(ValueError("Invalid IK target index"))
        for bi in bone_idxs:
            if not 0 <= bi < len(sk.bones): raise ValueError("Invalid IK bone index")
            ik.bones.append(sk.bones[bi].name)
        iks.append(ik)
    sk.ikConstraints = iks

def read_skins_v2(r: SpineBinaryReader, sk: SkeletonData) -> None:
    skins: List[SkinData] = []
    skin_records: List[Dict] = []

    for _ in range(sk.v2_skin_count):
        skin = SkinData()
        skin_name_off = r.read_u32()
        part_count = r.read_u16()
        sk._v2_used_pool_offsets.add(skin_name_off)
        skin.name = get_pool_string(skin_name_off, sk)
        attachments: Dict[str, Dict[str, Attachment]] = defaultdict(dict)

        for _ in range(part_count):
            att_name_off = r.read_u32()
            slot_idx = r.read_u32()
            data_type = r.read_u32()

            sk._v2_used_pool_offsets.add(att_name_off)
            att_name = get_pool_string(att_name_off, sk)
            slot_name = sk.slots[slot_idx].name if 0 <= slot_idx < len(sk.slots) else (_ for _ in ()).throw(ValueError("Invalid skin slot index"))

            skin_records.append({
                'skin': skin.name,
                'skin_slot': slot_name,
                'skin_attachment': att_name
            })

            if data_type == V2AttachmentType.BoundingBox:
                item_name_off = r.read_u32()
                vert_count = r.read_u32()
                verts = read_f32_array(r, vert_count)
                att = BoundingBoxAttachment()
                att.vertices = verts
                att.vertexCount = vert_count // 2
                sk._v2_used_pool_offsets.add(item_name_off)
                att.name = get_pool_string(item_name_off, sk)
                att.type = AttachmentType.Boundingbox
                attachments[slot_name][att_name] = att
                continue

            item_name_off = r.read_u32()
            item_path_off = r.read_u32()
            sk._v2_used_pool_offsets.update((item_name_off, item_path_off))
            item_name = get_pool_string(item_name_off, sk)
            item_path = get_pool_string(item_path_off, sk)

            if data_type == V2AttachmentType.Region:
                att = RegionAttachment()
                att.x = r.read_f32()
                att.y = r.read_f32()
                att.scaleX = r.read_f32()
                att.scaleY = r.read_f32()
                att.rotation = r.read_f32()
                orig_width = r.read_f32()
                orig_height = r.read_f32()
                att.color = f32_color(r.read_f32(), r.read_f32(), r.read_f32(), r.read_f32())
                r.skip(8)
                _atlas_width = r.read_u32()
                _atlas_height = r.read_u32()
                r.skip(72)
                att.width = orig_width
                att.height = orig_height
                att.name = item_name
                att.path = item_path
                att.type = AttachmentType.Region
                attachments[slot_name][att_name] = att

            elif data_type == V2AttachmentType.Mesh:
                att = MeshAttachment()
                vert_count = r.read_u32()
                verts = read_f32_array(r, vert_count)
                hull = r.read_u32()
                # Mesh stores atlas UV first, then region UV (reversed vs SkinnedMesh)
                _uvs_atlas = read_f32_array(r, vert_count)
                uvs_region = read_f32_array(r, vert_count)
                tri_count = r.read_u32()
                tris = read_u32_array(r, tri_count)
                att.color = f32_color(r.read_f32(), r.read_f32(), r.read_f32(), r.read_f32())
                r.skip(48)
                att.width = r.read_f32()
                att.height = r.read_f32()
                att.vertices = verts
                att.vertexCount = vert_count // 2
                att.hullLength = hull
                att.uvs = uvs_region
                att.triangles = tris
                att.name = item_name
                att.path = item_path
                att.type = AttachmentType.Mesh
                attachments[slot_name][att_name] = att

            elif data_type == V2AttachmentType.SkinnedMesh:
                att = SkinnedMeshAttachment()
                bone_cnt = r.read_u32()
                mbones = read_u32_array(r, bone_cnt)
                weight_cnt = r.read_u32()
                weights = read_f32_array(r, weight_cnt)
                tri_cnt = r.read_u32()
                tris = read_u32_array(r, tri_cnt)
                uv_cnt = r.read_u32()
                # SkinnedMesh stores region UV first, then atlas UV (reversed vs Mesh)
                uvs_region = read_f32_array(r, uv_cnt)
                _uvs_atlas = read_f32_array(r, uv_cnt)
                hull = r.read_u32()
                att.color = f32_color(r.read_f32(), r.read_f32(), r.read_f32(), r.read_f32())
                r.skip(48)
                att.width = r.read_f32()
                att.height = r.read_f32()
                att.bones = mbones
                att.weights = weights
                att.triangles = tris
                att.uvs = uvs_region
                att.hullLength = hull
                att.name = item_name
                att.path = item_path
                att.type = AttachmentType.Mesh
                attachments[slot_name][att_name] = att
            else:
                raise ValueError(f"Unknown V2 attachment type: {data_type}")

        skin.attachments = attachments
        skins.append(skin)

    sk.skins = skins
    sk.v2_skin_records = skin_records

_TOGGLE_THRESHOLD = 130

_OFFSET_THRESHOLD = 120

_COST_RATIO = 0.5

_MAX_SEGMENTS = 16

_rotation_fix_enabled = True

def read_events_v2(r: SpineBinaryReader, sk: SkeletonData) -> None:
    r.skip(4)  # default skin index
    events: List[EventData] = []
    for _ in range(sk.v2_event_count):
        ev = EventData()
        name_off = r.read_u32()
        ev.intValue = r.read_i32()
        ev.floatValue = r.read_f32()
        str_off = r.read_u32()
        sk._v2_used_pool_offsets.update((name_off, str_off))
        ev.name = get_pool_string(name_off, sk)
        ev.stringValue = get_pool_string(str_off, sk)
        events.append(ev)
    sk.events = events

_MAX_RAW_COUNT = 20000

def _parse_v2_timeline_entry(r: SpineBinaryReader, sk: SkeletonData,
                             anim: AnimationData, tl_type_v2: int) -> bool:
    """Parse a single V2 timeline entry. Returns True if parsed, False to break."""
    if tl_type_v2 == V2TimelineType.Scale:
        bone_idx = r.read_u32()
        raw_count = r.read_u32()
        if raw_count > _MAX_RAW_COUNT:
            logging.warning(
                f"[{sk.source_path}] Scale raw_count {raw_count} exceeds limit "
                f"in anim '{anim.name}' (pos {r.pos}), skipping timeline"
            )
            return False
        frame_count = raw_count // 3
        entries = []
        for fi in range(frame_count):
            t, x, y = r.read_f32(), r.read_f32(), r.read_f32()
            entry: Dict[str, Any] = {"time": t}
            entry["x"] = x
            entry["y"] = y
            entries.append(entry)
            anim.duration = max(anim.duration, t)
        curves = _read_v2_curves(r, frame_count)
        for ci, cv in enumerate(curves):
            if cv:
                entries[ci].update(cv)
        bone_name = sk.bones[bone_idx].name if 0 <= bone_idx < len(sk.bones) else ""
        anim.bones.setdefault(bone_name, {})["scale"] = entries

    elif tl_type_v2 == V2TimelineType.Rotate:
        bone_idx = r.read_u32()
        raw_count = r.read_u32()
        if raw_count > _MAX_RAW_COUNT:
            logging.warning(
                f"[{sk.source_path}] Rotate raw_count {raw_count} exceeds limit "
                f"in anim '{anim.name}' (pos {r.pos}), skipping timeline"
            )
            return False
        frame_count = raw_count // 2
        entries = []
        for fi in range(frame_count):
            t, angle = r.read_f32(), r.read_f32()
            if not math.isfinite(angle) or abs(angle) > 1e6:
                bone_name_dbg = sk.bones[bone_idx].name if 0 <= bone_idx < len(sk.bones) else f"?{bone_idx}"
                logging.warning(
                    f"[{sk.source_path}] Extreme rotation angle {angle} "
                    f"in anim '{anim.name}' bone '{bone_name_dbg}' "
                    f"frame {fi}/{frame_count} (pos {r.pos}, raw_count {raw_count})"
                )
            entry: Dict[str, Any] = {"time": t, "angle": angle}
            entries.append(entry)
            anim.duration = max(anim.duration, t)
        curves = _read_v2_curves(r, frame_count)
        for ci, cv in enumerate(curves):
            if cv:
                entries[ci].update(cv)


        bone_name = sk.bones[bone_idx].name if 0 <= bone_idx < len(sk.bones) else ""
        anim.bones.setdefault(bone_name, {})["rotate"] = entries

    elif tl_type_v2 == V2TimelineType.Translate:
        bone_idx = r.read_u32()
        raw_count = r.read_u32()
        if raw_count > _MAX_RAW_COUNT:
            logging.warning(
                f"[{sk.source_path}] Translate raw_count {raw_count} exceeds limit "
                f"in anim '{anim.name}' (pos {r.pos}), skipping timeline"
            )
            return False
        frame_count = raw_count // 3
        entries = []
        for fi in range(frame_count):
            t, x, y = r.read_f32(), r.read_f32(), r.read_f32()
            entry: Dict[str, Any] = {"time": t}
            if x != 0: entry["x"] = x
            if y != 0: entry["y"] = y
            entries.append(entry)
            anim.duration = max(anim.duration, t)
        curves = _read_v2_curves(r, frame_count)
        for ci, cv in enumerate(curves):
            if cv:
                entries[ci].update(cv)
        bone_name = sk.bones[bone_idx].name if 0 <= bone_idx < len(sk.bones) else ""
        anim.bones.setdefault(bone_name, {})["translate"] = entries

    elif tl_type_v2 == V2TimelineType.Color:
        slot_idx = r.read_u32()
        raw_count = r.read_u32()
        if raw_count > _MAX_RAW_COUNT:
            logging.warning(
                f"[{sk.source_path}] Color raw_count {raw_count} exceeds limit "
                f"in anim '{anim.name}' (pos {r.pos}), skipping timeline"
            )
            return False
        frame_count = raw_count // 5
        entries = []
        for fi in range(frame_count):
            t = r.read_f32()
            cr, cg, cb, ca = r.read_f32(), r.read_f32(), r.read_f32(), r.read_f32()
            color = f32_color(cr, cg, cb, ca)
            entry: Dict[str, Any] = {"time": t}
            entry["color"] = color_to_string(color, True)
            entries.append(entry)
            anim.duration = max(anim.duration, t)
        curves = _read_v2_curves(r, frame_count)
        for ci, cv in enumerate(curves):
            if cv:
                entries[ci].update(cv)
        slot_name = sk.slots[slot_idx].name if 0 <= slot_idx < len(sk.slots) else ""
        anim.slots.setdefault(slot_name, {})["color"] = entries

    elif tl_type_v2 == V2TimelineType.Attachment:
        slot_idx = r.read_u32()
        frame_count = r.read_u32()
        times = read_f32_array(r, frame_count)
        name_offs = read_u32_array(r, frame_count)
        entries = []
        for fi in range(frame_count):
            entry: Dict[str, Any] = {"time": times[fi]}
            name = get_pool_string(name_offs[fi], sk) if name_offs[fi] != 0xFFFFFFFF else None
            entry["name"] = name
            entries.append(entry)
            anim.duration = max(anim.duration, times[fi])
        slot_name = sk.slots[slot_idx].name if 0 <= slot_idx < len(sk.slots) else ""
        anim.slots.setdefault(slot_name, {})["attachment"] = entries

    elif tl_type_v2 == V2TimelineType.FFD:
        frame_count = r.read_u32()
        if frame_count > _MAX_RAW_COUNT:
            logging.warning(
                f"[{sk.source_path}] FFD frame_count {frame_count} exceeds limit "
                f"in anim '{anim.name}' (pos {r.pos}), skipping timeline"
            )
            return False
        times = read_f32_array(r, frame_count)
        r.skip(4)
        verts_per_frame = r.read_u32()
        all_verts: List[List[float]] = []
        for fi in range(frame_count):
            frame_verts = read_f32_array(r, verts_per_frame)
            all_verts.append(frame_verts)
            anim.duration = max(anim.duration, times[fi])
        curves = _read_v2_curves(r, frame_count)
        skin_record_id = r.read_u32()
        record = sk.v2_skin_records[skin_record_id] if skin_record_id < len(sk.v2_skin_records) else {}
        ffd_skin = record.get('skin', 'default')
        ffd_slot = record.get('skin_slot', '')
        ffd_att = record.get('skin_attachment', '')

        setup_verts: List[float] = []
        for s_obj in sk.skins:
            if s_obj.name == ffd_skin:
                att = s_obj.attachments.get(ffd_slot, {}).get(ffd_att)
                if isinstance(att, VertexAttachment) and not att.isWeighted:
                    setup_verts = att.vertices
                break

        entries = []
        sv_arr = np.array(setup_verts) if setup_verts else None
        for fi in range(frame_count):
            entry: Dict[str, Any] = {}
            if fi < len(curves) and curves[fi]:
                entry.update(curves[fi])
            entry["time"] = times[fi]
            fv = all_verts[fi]
            if sv_arr is not None and len(fv) == len(sv_arr):
                off_arr = np.array(fv) - sv_arr
                if np.any(off_arr != 0):
                    entry["vertices"] = np.round(off_arr, 8).tolist()
            else:
                if any(v != 0 for v in fv):
                    entry["vertices"] = [v for v in fv]
            entries.append(entry)
        anim.ffd.setdefault(ffd_skin, {}).setdefault(ffd_slot, {})[ffd_att] = entries

    elif tl_type_v2 == V2TimelineType.IkConstraint:
        ik_idx = r.read_u32()
        raw_count = r.read_u32()
        if raw_count > _MAX_RAW_COUNT:
            logging.warning(
                f"[{sk.source_path}] IkConstraint raw_count {raw_count} exceeds limit "
                f"in anim '{anim.name}' (pos {r.pos}), skipping timeline"
            )
            return False
        frame_count = raw_count // 3
        entries = []
        for fi in range(frame_count):
            t = r.read_f32()
            mix = r.read_f32()
            bend = r.read_f32()
            entry: Dict[str, Any] = {"time": t}
            if mix != 1: entry["mix"] = mix
            entry["bendPositive"] = (not math.isnan(bend)) and int(bend) >= 0
            entries.append(entry)
            anim.duration = max(anim.duration, t)
        curves = _read_v2_curves(r, frame_count)
        for ci, cv in enumerate(curves):
            if cv:
                entries[ci].update(cv)
        ik_name = sk.ikConstraints[ik_idx].name if 0 <= ik_idx < len(sk.ikConstraints) else ""
        anim.ik[ik_name] = entries

    elif tl_type_v2 in (V2TimelineType.FlipX, V2TimelineType.FlipY):
        bone_idx = r.read_u32()
        frame_count = r.read_u32()
        if frame_count > _MAX_RAW_COUNT:
            logging.warning(
                f"[{sk.source_path}] Flip frame_count {frame_count} exceeds limit "
                f"in anim '{anim.name}' (pos {r.pos}), skipping timeline"
            )
            return False

        if tl_type_v2 == V2TimelineType.FlipY and bone_idx == len(sk.slots):
            times = read_f32_array(r, frame_count)
            anim._v2_sentinel_times = [t for t in times]
            return False

        saved_pos = r.pos
        times = read_f32_array(r, frame_count)
        if frame_count > 1 and all(t < 1e-6 for t in times):
            r.reset_pos(saved_pos - 12)
            return False
        values = [r.read_byte() for _ in range(frame_count)]
        is_flip_x = (tl_type_v2 == V2TimelineType.FlipX)
        flip_key = "flipX" if is_flip_x else "flipY"
        val_key = "x" if is_flip_x else "y"
        entries = []
        for fi in range(frame_count):
            entry: Dict[str, Any] = {"time": times[fi]}
            entry[val_key] = bool(values[fi])
            entries.append(entry)
            anim.duration = max(anim.duration, times[fi])
        bone_name = sk.bones[bone_idx].name if 0 <= bone_idx < len(sk.bones) else ""
        anim.bones.setdefault(bone_name, {})[flip_key] = entries

    elif tl_type_v2 == V2TimelineType.DrawOrder:
        slot_idx = r.read_u32()
        offset = r.read_u32()
        count = r.read_u32()
        if count > _MAX_RAW_COUNT:
            logging.warning(
                f"[{sk.source_path}] DrawOrder count {count} exceeds limit "
                f"in anim '{anim.name}' (pos {r.pos}), skipping timeline"
            )
            return False
        frame_count = count // 2
        frames = []
        for _ in range(frame_count):
            t = r.read_f32()
            activation = r.read_f32()
            frames.append((t, activation != 0.0))
            anim.duration = max(anim.duration, t)
        if slot_idx < len(sk.slots):
            anim._v2_draworder_raw.append({
                'slot_idx': slot_idx,
                'offset': offset,
                'frames': frames,
            })

    elif tl_type_v2 == V2TimelineType.Event:
        _effect_bone_idx = r.read_u32()
        _reserved = r.read_u32()
        raw_count = r.read_u32()
        if raw_count > _MAX_RAW_COUNT:
            logging.warning(
                f"[{sk.source_path}] Event raw_count {raw_count} exceeds limit "
                f"in anim '{anim.name}' (pos {r.pos}), skipping timeline"
            )
            return False
        frame_count = raw_count // 2
        for fi in range(frame_count):
            t = r.read_f32()
            event_idx = int(r.read_f32())
            entry: Dict[str, Any] = {"time": t}
            if 0 <= event_idx < len(sk.events):
                entry["name"] = sk.events[event_idx].name
                ev = sk.events[event_idx]
                if ev.intValue != 0:
                    entry["int"] = ev.intValue
                if ev.floatValue != 0.0:
                    entry["float"] = ev.floatValue
                if ev.stringValue:
                    entry["string"] = ev.stringValue
            anim.events.append(entry)
            anim.duration = max(anim.duration, t)

    else:
        return False

    return True
