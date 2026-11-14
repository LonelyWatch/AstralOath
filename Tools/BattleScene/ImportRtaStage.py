"""Compose the World Arena duel stage from the native atlas and Spine setup meshes."""

import argparse
import io
import math
import re
import sys
from pathlib import Path

import numpy as np
from PIL import Image

from ImportHudEffects import MODULE, RAW_DIRECTORY, TOOLS, json_bytes

sys.path.insert(0, str(TOOLS / "Arena"))
from ImportArenaArt import digest, metadata, read_source, texture_importer, write
from convert import decode_scsp, decode_sct

OUTPUT = MODULE / "Res/Textures/Backgrounds/Rta021"
LAYERS = """
    bg wall_2 wall_1 b_torso ground_3 ground_2 ground_1 light_1
    m_3 m_5 m_7 m_6 m_6_1 m_8 m_9 m_10 m_4 m_2 m_1
    light_2 light_3 f_1 f_2 f_3 f_4 f_5 f_7 f_8
    f_light_1 f_light_2 f_light_3 f_light_4 f_light_5
""".split()
VIEW = {"width": 2240, "height": 1080, "sourceCrop": [160, 0, 2400, 1008]}
TEXT_IMPORTER = "TextScriptImporter:\n  externalObjects: {}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n"
FOLDER_IMPORTER = "folderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n"


def atlas_regions(atlas, image):
    pattern = re.compile(
        r"(?m)^([^:\r\n ]+)\r?\n +rotate: (true|false)\r?\n +xy: (\d+), (\d+)\r?\n +size: (\d+), (\d+)"
    )
    regions = {}
    for name, rotate, x, y, width, height in pattern.findall(atlas.decode("utf-8-sig")):
        x, y, width, height = map(int, (x, y, width, height))
        packed_w, packed_h = (height, width) if rotate == "true" else (width, height)
        texture = image.crop((x, y, x + packed_w, y + packed_h))
        if rotate == "true":
            texture = texture.transpose(Image.Transpose.ROTATE_270)
        regions[name] = np.array(texture, dtype=np.float32) / 255
    return regions


def local_matrix(transform):
    radians = math.radians(transform.get("rotation", 0))
    shear_x = math.radians(transform.get("shearX", 0))
    shear_y = math.radians(transform.get("shearY", 0) + 90)
    scale_x, scale_y = transform.get("scaleX", 1), transform.get("scaleY", 1)
    return np.array((
        (math.cos(radians + shear_x) * scale_x, math.cos(radians + shear_y) * scale_y, transform.get("x", 0)),
        (math.sin(radians + shear_x) * scale_x, math.sin(radians + shear_y) * scale_y, transform.get("y", 0)),
        (0, 0, 1),
    ))


def bone_matrices(bones):
    matrices = {}
    for bone in bones:
        mode = bone.get("transform", "normal")
        if mode != "normal":
            raise ValueError(f"Unsupported stage bone transform: {mode}")
        parent = matrices.get(bone.get("parent"))
        matrix = local_matrix(bone)
        matrices[bone["name"]] = parent @ matrix if parent is not None else matrix
    return matrices


def mesh_vertices(attachment, bone, matrices):
    values = attachment["vertices"]
    uvs = np.asarray(attachment["uvs"]).reshape(-1, 2)
    if len(values) == uvs.size:
        points = [(bone @ (x, y, 1))[:2] for x, y in np.asarray(values).reshape(-1, 2)]
    else:
        points = []
        offset = 0
        for _ in uvs:
            weight_count = int(values[offset])
            offset += 1
            result = np.zeros(2)
            for _ in range(weight_count):
                index, x, y, weight = values[offset:offset + 4]
                result += (matrices[int(index)] @ (x, y, 1))[:2] * weight
                offset += 4
            points.append(result)
        if offset != len(values):
            raise ValueError("Unexpected stage mesh vertex encoding.")
    return np.asarray(points), uvs, np.asarray(attachment["triangles"]).reshape(-1, 3)


def region_vertices(attachment, bone):
    w, h = attachment["width"] / 2, attachment["height"] / 2
    matrix = bone @ local_matrix(attachment)
    points = np.array([(matrix @ (x, y, 1))[:2] for x, y in ((-w, h), (w, h), (w, -h), (-w, -h))])
    return points, np.array(((0, 0), (1, 0), (1, 1), (0, 1))), ((0, 1, 2), (0, 2, 3))


def sample(texture, uv):
    height, width = texture.shape[:2]
    u = np.clip(uv[:, 0] * width - .5, 0, width - 1)
    v = np.clip(uv[:, 1] * height - .5, 0, height - 1)
    x, y = u.astype(int), v.astype(int)
    next_x, next_y = np.minimum(x + 1, width - 1), np.minimum(y + 1, height - 1)
    fx, fy = (u - x)[:, None], (v - y)[:, None]
    return (texture[y, x] * (1 - fx) + texture[y, next_x] * fx) * (1 - fy) + (
        texture[next_y, x] * (1 - fx) + texture[next_y, next_x] * fx) * fy


def draw(canvas, texture, points, uvs, triangles, tint, blend):
    height, width = canvas.shape[:2]
    min_x, min_y = np.maximum(np.floor(points.min(axis=0)).astype(int), 0)
    max_x, max_y = np.minimum(np.ceil(points.max(axis=0)).astype(int), (width, height))
    if min_x >= max_x or min_y >= max_y:
        return
    target = np.zeros((max_y - min_y, max_x - min_x, 4), dtype=np.float32)
    for triangle in triangles:
        pos = points[list(triangle)]
        x0, y0 = np.maximum(np.floor(pos.min(axis=0)).astype(int), (min_x, min_y))
        x1, y1 = np.minimum(np.ceil(pos.max(axis=0)).astype(int), (max_x, max_y))
        if x0 >= x1 or y0 >= y1:
            continue
        a, b, c = pos
        ba, ca = b - a, c - a
        determinant = ba[0] * ca[1] - ba[1] * ca[0]
        if abs(determinant) < 1e-6:
            continue
        y, x = np.ogrid[y0:y1, x0:x1]
        dx, dy = x + .5 - a[0], y + .5 - a[1]
        wb = (dx * ca[1] - dy * ca[0]) / determinant
        wc = (dy * ba[0] - dx * ba[1]) / determinant
        wa = 1 - wb - wc
        inside = (wa >= -1e-6) & (wb >= -1e-6) & (wc >= -1e-6)
        uv = np.stack((wa[inside], wb[inside], wc[inside]), axis=1) @ uvs[list(triangle)]
        target[y0 - min_y:y1 - min_y, x0 - min_x:x1 - min_x][inside] = sample(texture, uv)
    opacity = target[:, :, 3:4] * tint[3]
    color = target[:, :, :3] * tint[:3] * tint[3]
    dest = canvas[min_y:max_y, min_x:max_x]
    if blend == "normal":
        dest[:] = color + dest * (1 - opacity)
    elif blend == "additive":
        dest[:] = np.minimum(1, color + dest)
    elif blend == "screen":
        dest[:] = color + dest * (1 - color)
    elif blend == "multiply":
        dest[:] *= color + 1 - opacity
    else:
        raise ValueError(f"Unsupported stage blend: {blend}")


def rgba(hex_color):
    return np.frombuffer(bytes.fromhex(hex_color), dtype=np.uint8).astype(np.float32) / 255


def render(root):
    data = {}
    for suffix in (".atlas", ".sca", ".sct"):
        name = "bg/rta_021" + suffix
        data[name] = read_source(root, name)
    page, info = decode_sct(data["bg/rta_021.sct"])
    regions = atlas_regions(data["bg/rta_021.atlas"], page)
    width, height = VIEW["width"], VIEW["height"]
    left, top, right, bottom = VIEW["sourceCrop"]
    canvas = np.zeros((height, width, 3), dtype=np.float32)
    canvas[:] = (0.035, .04, .06)
    for layer in LAYERS:
        name = f"bg/rta_021_{layer}.scsp"
        data[name] = read_source(root, name)
        skeleton, report = decode_scsp(data[name])
        if report["spineVersion"] != "3.8.99":
            raise ValueError(f"Unexpected Spine version: {name}")
        bones = bone_matrices(skeleton["bones"])
        transforms = list(bones.values())
        skin = skeleton["skins"][0]["attachments"]
        for slot in skeleton["slots"]:
            if "attachment" not in slot:
                continue
            attachment_name = slot["attachment"]
            attachment = skin[slot["name"]][attachment_name]
            texture = regions[attachment.get("path", attachment_name)]
            bone = bones[slot["bone"]]
            if attachment.get("type") == "mesh":
                points, uvs, triangles = mesh_vertices(attachment, bone, transforms)
            else:
                points, uvs, triangles = region_vertices(attachment, bone)
            points[:, 0] = (points[:, 0] - left) * width / (right - left)
            points[:, 1] = (-points[:, 1] - top) * height / (bottom - top)
            tint = rgba(slot.get("color", "FFFFFFFF")) * rgba(attachment.get("color", "FFFFFFFF"))
            draw(canvas, texture, points, uvs, triangles, tint, slot.get("blend", "normal"))
    result = Image.fromarray(np.rint(np.clip(canvas, 0, 1) * 255).astype(np.uint8), "RGB")
    output = io.BytesIO()
    result.save(output, format="PNG")
    contents = output.getvalue()
    manifest = {
        "schemaVersion": 1,
        "asset": "rta_021.png",
        "assetSha256": digest(contents),
        "method": "Spine 3.8.99 setup-pose meshes and slots, viewport aligned static stage",
        "texturePage": info,
        "viewport": VIEW,
        "layerOrder": LAYERS,
        "sources": [{"source": name, "sha256": digest(value)} for name, value in data.items()],
    }
    return contents, json_bytes(manifest)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=Path("D:/sevenepic"))
    parser.add_argument("--check", action="store_true")
    parser.add_argument("--preview", type=Path, help="Write only one PNG to an existing artifact directory.")
    args = parser.parse_args()
    source = (args.source / RAW_DIRECTORY).resolve(strict=True)
    destination = (args.preview.parent if args.preview else OUTPUT).resolve()
    if destination.is_relative_to(source):
        raise ValueError("Stage assets must stay outside the read-only source directory.")
    contents, manifest = render(source)
    if args.preview:
        if args.check:
            parser.error("--preview and --check cannot be combined")
        args.preview.write_bytes(contents)
        return
    if not args.check:
        OUTPUT.mkdir(parents=True, exist_ok=True)
    metadata(OUTPUT, args.check, FOLDER_IMPORTER)
    png = OUTPUT / "rta_021.png"
    index = OUTPUT / "source.json"
    write(png, contents, args.check)
    write(index, manifest, args.check)
    importer = texture_importer(png, sprite=True)
    importer = re.sub(r"(?m)^(\s+maxTextureSize:) \d+$", r"\1 4096", importer)
    importer = re.sub(r"(?m)^(\s+textureCompression:) \d+$", r"\1 0", importer)
    importer = re.sub(r"(?m)^  userData:.*$", "  userData: AstralOath.ImportOverride", importer)
    metadata(png, args.check, importer)
    metadata(index, args.check, TEXT_IMPORTER)
    print(f"{'Verified' if args.check else 'Imported'} {len(LAYERS)} duel stage layers and 1 atlas page.")


if __name__ == "__main__":
    main()
