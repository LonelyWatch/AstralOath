"""Compare measured Unity shop positions against the 02:13 and 02:16 video frames."""
import argparse
import json
from pathlib import Path


def measure(file, end):
    capture = json.loads(file.read_text(encoding="utf-8-sig"))
    scale = capture["height"] / 600
    width = capture["width"] / scale
    nodes = capture["elements"]
    mismatches = []

    def check(label, observed, target, tolerance):
        value = observed / scale
        print(f"{label:32s} {value:7.2f} / {target:.2f} (+/-{tolerance})")
        if abs(value - target) > tolerance:
            mismatches.append(label)

    def node(name):
        return next(item for item in nodes if item["path"].endswith("/" + name))

    header = node("Header")
    check("header bottom", header["y"] + header["height"], 55, 4)
    for i, key in enumerate(("featured1", "featured2", "packages", "charge", "store", "exchange", "skin", "events")):
        row = node("Shop rail " + key)
        check("rail " + key, row["y"] + row["height"] / 2, [98, 164, 230, 296, 361, 427, 492, 552][i], 5)
    for i, key in enumerate(("arena", "glory", "trials", "constellation")):
        tab = node("Exchange tab " + key)
        check("tab " + key, tab["x"] + tab["width"] / 2, 334 + 147 * i, 5)
    cards = [item for item in nodes if item["path"].endswith("/Shop offers/Viewport/Content/ShopItem(Clone)")]
    if len(cards) < 3:
        raise ValueError(f"Need three visible card frames, found {len(cards)}")
    card = cards[-1] if end else cards[0]
    check("first row top", card["y"], 128, 4)
    check("card width", card["width"], 157, 3)
    check("card height", card["height"], 193, 3)
    if end:
        check("space beyond final column", width * scale - card["x"] - card["width"], 150, 8)
    else:
        check("first column left", card["x"], 256, 4)
        check("column pitch", cards[2]["x"] - card["x"], 168, 3)
        check("row pitch", cards[1]["y"] - card["y"], 204, 3)
    check("footer divider", node("Footer rule")["y"], 538, 4)
    footer = node("Catalog edition/Season")
    check("season label center", footer["y"] + footer["height"] / 2, 569, 5)
    return mismatches


def first_item_edges(file):
    import numpy as np
    from PIL import Image

    with Image.open(file) as frame:
        normalized = frame.convert("RGB").resize((round(600 * frame.width / frame.height), 600))
    rgb = np.asarray(normalized, dtype=np.float32)

    def strongest_edge(image, minimum, maximum, start, stop):
        band = image[start:stop, minimum - 1:maximum + 1]
        strength = np.abs(np.diff(band, axis=1)).mean(axis=(0, 2))
        return minimum + int(strength.argmax())

    transposed = rgb.transpose(1, 0, 2)
    frame_edges = (
        strongest_edge(rgb, 252, 267, 155, 300),
        strongest_edge(rgb, 403, 419, 155, 300),
        strongest_edge(transposed, 118, 140, 273, 400),
        strongest_edge(transposed, 313, 329, 273, 400),
    )
    # Isolate the original green exchange button from the dark card in the first offer.
    red, green, blue = rgb[273:322, 254:418].transpose(2, 0, 1)
    rows, columns = np.where((green > 1.27 * red) & (green > 1.18 * blue) & (green > 26))
    if not len(rows):
        raise ValueError(f"Could not find first exchange button in {file}")
    return frame_edges + (254 + columns.min(), 254 + columns.max(), 273 + rows.min(), 273 + rows.max())


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    parser.add_argument("--reference", type=Path, help="02:13 frame cropped to remove the recording's device gutter")
    options = parser.parse_args()
    differences = []
    for second in (133, 136):
        source = options.directory / f"shop-arena-reference-{second}.bounds.json"
        print(source)
        differences.extend(measure(source, end=second == 136))
    if options.reference:
        target_edges = first_item_edges(options.reference)
        measured_edges = first_item_edges(options.directory / "shop-arena-reference-133.png")
        edges = ("frame left", "frame right", "frame top", "frame bottom",
                 "button left", "button right", "button top", "button bottom")
        for edge, observed, target in zip(edges, measured_edges, target_edges):
            print(f"{'visible ' + edge:32s} {observed:7d} / {target:d} (+/-3)")
            if abs(observed - target) > 3:
                differences.append("visible " + edge)
    if differences:
        raise SystemExit("Outside reference tolerance: " + ", ".join(differences))
