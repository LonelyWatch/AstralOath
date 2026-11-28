"""Compare actual camera-space UI bounds with the 34 s, 206 s and 269 s source frames."""
import argparse
import json
from pathlib import Path


class Frame:
    def __init__(self, directory, stem):
        source = json.loads((directory / (stem + ".bounds.json")).read_text(encoding="utf-8-sig"))
        scale = 600 / source["height"]
        self.width = source["width"] * scale
        self.elements = [(v["path"], tuple(v[k] * scale for k in ("x", "y", "width", "height")))
                         for v in source["elements"]]

    def matches(self, suffix):
        return [rect for path, rect in self.elements if path.endswith("/" + suffix)]

    def one(self, suffix):
        hits = self.matches(suffix)
        if len(hits) != 1:
            raise ValueError(f"{suffix}: expected one visible element, found {len(hits)}")
        return hits[0]


def compare(directory):
    listing = Frame(directory, "01-tower-stage-55")
    history = Frame(directory, "02-tower-clear-team")
    formation = Frame(directory, "03-tower-formation")
    metrics = []

    def add(name, measured, target, tolerance, source):
        metrics.append(dict(name=name, actual=round(measured, 2), target=round(target, 2),
                            tolerance=tolerance, delta=round(measured-target, 2),
                            passed=abs(measured-target) <= tolerance + .01, source=source))

    # Height-scaled, centered list and pop-up. Outer rails stretch with the game viewport.
    w = listing.width
    center_offset = (w-1232) / 2
    header = listing.one("Header")
    profile = listing.one("Season profile")
    navigation = listing.one("Mode navigation")
    emblem = listing.one("Rank emblem")
    add("shell.header_height", header[3], 83*600/925, 4, "directory")
    add("shell.profile_edge", profile[0]+profile[2], w*310/1644, 9, "directory")
    add("shell.navigation_edge", navigation[0], w*1339/1644, 7, "directory")
    add("shell.rank_emblem_center_x", emblem[0]+emblem[2]/2, w*151/1644, 7, "directory")
    add("shell.rank_emblem_center_y", emblem[1]+emblem[3]/2, 306*600/925, 7, "directory")
    viewport = listing.one("Stage list/Viewport")
    rows = listing.matches("Stage row(Clone)")
    complete = sorted([r for r in rows if r[1] >= viewport[1]-.8
                       and r[1]+r[3] <= viewport[1]+viewport[3]+.8], key=lambda r: r[1])
    add("list.complete_opponents", len(complete), 3, 0, "34s")
    for index, row in enumerate(complete):
        prefix = f"list.row{index+1}"
        add(prefix+".x", row[0], 330+center_offset, 5, "34s")
        add(prefix+".y", row[1], 141+index*107, 5, "34s")
        add(prefix+".width", row[2], 575, 5, "34s")
        add(prefix+".height", row[3], 90, 4, "34s")
    faces = [r for r in listing.matches("Defender 0") if viewport[1] < r[1]+r[3]/2 < viewport[1]+viewport[3]]
    add("list.framed_first_heroes", len(faces), 3, 0, "34s")
    for i, r in enumerate(sorted(faces, key=lambda v: v[1])):
        add(f"list.hero{i+1}.diameter", r[2], 63, 4, "34s")
        add(f"list.hero{i+1}.center_x", r[0]+r[2]/2, 595+center_offset, 5, "34s")

    f = formation.width
    add("formation.squad_edge", formation.one("Squad navigation")[2], f*118/1232, 6, "206s")
    add("formation.roster_edge", formation.one("Owned heroes")[0], f*1005/1232, 6, "206s")
    for name, path, x, y in [("ally", "ally position 1",405,316), ("enemy","enemy position 1",726,320),
                              ("versus","Versus",574,336)]:
        r = formation.one(path)
        add("formation."+name+".center_x", r[0]+r[2]/2, f*x/1232, 12, "206s")
        add("formation."+name+".center_y", r[1]+r[3]/2, y, 12, "206s")
    versus=formation.one("Versus")
    add("formation.versus.source_width",versus[2],207*600/720,3,"206s / pvp_team_formation.csb")
    add("formation.versus.source_height",versus[3],207*600/720,3,"206s / pvp_team_formation.csb")

    hcenter = (history.width-1232)/2
    for path, name, goal in [("First clear overview","panel",(284,63,662,479)),
                             ("Defense strip","defense",(308,180,615,87))]:
        r = history.one(path)
        for axis, actual, expected in zip(("x","y","width","height"),r,goal):
            add("history."+name+"."+axis, actual, expected+(hcenter if axis=="x" else 0), 5, "269s")
    add("history.attack.height", history.one("Winning formation strip")[3], 54, 4, "269s")
    return metrics


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("captures", type=Path, help="directory containing the three .bounds.json files")
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    metrics = compare(args.captures)
    passed = sum(m["passed"] for m in metrics)
    summary = {"basis": "600 high active game pixels", "passed": passed, "total": len(metrics), "metrics": metrics}
    result = json.dumps(summary,ensure_ascii=False,indent=2)
    if args.output:
        args.output.write_text(result+"\n", encoding="utf-8")
    else:
        print(result)
    print(f"{passed}/{len(metrics)} tower geometry measurements within tolerance")
    raise SystemExit(0 if passed == len(metrics) else 1)
