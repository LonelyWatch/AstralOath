"""Extract only the 100 anonymous defense observations from the July 2020 Global ladder."""
import argparse
from collections import Counter
from hashlib import sha256
from html.parser import HTMLParser
import json
from pathlib import Path
import re
from urllib.request import urlopen


URL = "https://m.hungryapp.co.kr/bbs/bbs_view.php?bcode=epicseven&pid=125785"
ROOT = Path(__file__).resolve().parents[2]
DEST = ROOT / "Config/ArenaTower/defenses-global-2020-07-11.json"


class LeaderboardPage(HTMLParser):
    def __init__(self):
        super().__init__()
        self.blocks = []
        self.current = None

    def handle_starttag(self, tag, attrs):
        if tag == "pre":
            self.current = []

    def handle_data(self, data):
        if self.current is not None:
            self.current.append(data)

    def handle_endtag(self, tag):
        if tag == "pre" and self.current is not None:
            self.blocks.append("".join(self.current))
            self.current = None


def extract(page):
    parser = LeaderboardPage()
    parser.feed(page.decode("utf-8"))
    blocks = [b for b in parser.blocks if re.search(r"(?m)^1 - \[5821\]", b)]
    if len(blocks) != 1:
        raise ValueError("Expected one July 11 defense report")
    block = blocks[0].replace("\r\n", "\n")
    counts = Counter()
    rows = []
    for rank, score, team in re.findall(r"(?m)^(\d+) - \[(\d+)\][^\n]*\n([^\n]+)", block):
        names = [name.strip() for name in team.split(",")]
        if len(names) != 4 or len(set(names)) != 4:
            raise ValueError(f"Incomplete defense at rank {rank}")
        counts.update(names)
        rows.append(dict(row=len(rows) + 1, rank=int(rank), score=int(score), heroes=names))
    reported = {hero: int(n) for hero, n in re.findall(r"(?m)^([가-힣 ][가-힣 ]*) - (\d+)$", block)}
    if len(rows) != 100 or rows[-1]["rank"] != 100 or counts != reported:
        raise ValueError("Row count or per-hero counts disagree with the published report")
    return dict(sourceUrl=URL, published="2020-07-11", server="global", mode="arena_defense",
                # Hash the anonymous facts so a changing sidebar, ads, etc. cannot change the result.
                rowsSha256=sha256(json.dumps(rows, ensure_ascii=False, separators=(",", ":")).encode()).hexdigest(),
                observations=rows)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--html", type=Path, help="Read a previously downloaded public page")
    parser.add_argument("--output", type=Path, default=DEST)
    args = parser.parse_args()
    if args.html:
        page = args.html.read_bytes()
    else:
        with urlopen(URL, timeout=30) as response:
            page = response.read()
    data = extract(page)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    lines = [json.dumps(row, ensure_ascii=False) for row in data.pop("observations")]
    header = json.dumps(data, ensure_ascii=False, indent=2)[:-2]
    args.output.write_text(header + ',\n  "observations": [\n    ' + ",\n    ".join(lines) + "\n  ]\n}\n", encoding="utf-8")
    print(f"100 defenses / 400 slots / {data['rowsSha256']}")


if __name__ == "__main__":
    main()
