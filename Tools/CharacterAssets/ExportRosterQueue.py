"""Write the fixed, locally sourced order for the first 100 battle characters."""

import argparse
import csv
import hashlib
import io
import json
from pathlib import Path
import re


REPO = Path(__file__).resolve().parents[2]
SOURCE = Path(".artifacts/roster-data/official.source")
OUTPUT = Path(".artifacts/roster-data/roster-100.json")
HEROES = Path("Config/Tables/battle_heroes.csv")
PINNED_SHA256 = "a37c7a85128deef78ab8865eec2e7da76f1992c79b571c3001eb13add4998580"
TARGET_COUNT = 100
FIRST_TEN = tuple(f"c{number}" for number in range(1001, 1010)) + ("c1062",)
HERO_ID = re.compile(r"c1[0-9]{3}")
SHA256 = re.compile(r"[0-9a-f]{64}")


def verify_source(raw: bytes, expected: str) -> str:
    expected = expected.lower()
    if not SHA256.fullmatch(expected):
        raise ValueError("Expected SHA-256 must contain 64 hexadecimal digits.")
    actual = hashlib.sha256(raw).hexdigest()
    if actual != expected:
        raise ValueError(f"Official source SHA-256 mismatch: expected {expected}, read {actual}.")
    return actual


def read_configured_ids(text: str) -> set[str]:
    rows = csv.DictReader(io.StringIO(text))
    if "id" not in (rows.fieldnames or []):
        raise ValueError("Battle hero CSV requires an id column.")
    configured = set()
    for row in rows:
        hero_id = row["id"]
        if not hero_id or not re.fullmatch(r"c[0-9]{4}", hero_id):
            raise ValueError(f"Invalid battle hero id {hero_id!r} on CSV line {rows.line_num}.")
        if hero_id in configured:
            raise ValueError(f"Duplicate battle hero id {hero_id} on CSV line {rows.line_num}.")
        configured.add(hero_id)
    return configured


def build_queue(source: dict, configured: set[str]) -> list[dict]:
    rows = source.get("zh-CN") if isinstance(source, dict) else None
    if not isinstance(rows, list):
        raise ValueError("Official source must contain a zh-CN hero list.")

    candidates = {}
    for row in rows:
        if not isinstance(row, dict) or not isinstance(row.get("code"), str):
            raise ValueError("Official zh-CN list contains a row without a hero code.")
        hero_id = row["code"]
        if not HERO_ID.fullmatch(hero_id):
            continue
        if hero_id in candidates:
            raise ValueError(f"Official zh-CN list contains duplicate hero {hero_id}.")
        candidates[hero_id] = row

    missing = [hero_id for hero_id in FIRST_TEN if hero_id not in candidates]
    if missing:
        raise ValueError(f"Missing first-ten heroes in official source: {', '.join(missing)}.")
    remaining = sorted(set(candidates) - set(FIRST_TEN), key=lambda hero_id: int(hero_id[1:]))
    selected = FIRST_TEN + tuple(remaining[:TARGET_COUNT - len(FIRST_TEN)])
    if len(selected) != TARGET_COUNT:
        raise ValueError(f"Expected {TARGET_COUNT} queue entries, selected {len(selected)}.")

    queue = []
    for ordinal, hero_id in enumerate(selected, start=1):
        name = candidates[hero_id].get("name")
        if not isinstance(name, str) or not name.strip():
            raise ValueError(f"Selected official hero {hero_id} has no zh-CN name.")
        status = "configured" if hero_id in configured else "pending"
        queue.append({"id": hero_id, "name": name, "ordinal": ordinal, "status": status})
    return queue


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=SOURCE, help="Local official hero JSON. No network request is made.")
    parser.add_argument("--expected-sha256", default=PINNED_SHA256, help="Explicit digest for an alternative pinned source.")
    parser.add_argument("--heroes", type=Path, default=HEROES, help="Current battle hero CSV.")
    parser.add_argument("--output", type=Path, default=OUTPUT, help="Output JSON under the repository .artifacts directory.")
    args = parser.parse_args()

    source = (REPO / args.source).resolve()
    output = (REPO / args.output).resolve()
    if not output.is_relative_to((REPO / ".artifacts").resolve()):
        parser.error("The queue output must be under the repository .artifacts directory.")
    if output == source:
        parser.error("The queue output cannot overwrite the source JSON.")
    try:
        raw = source.read_bytes()
        digest = verify_source(raw, args.expected_sha256)
        configured = read_configured_ids((REPO / args.heroes).read_text(encoding="utf-8-sig"))
        rows = build_queue(json.loads(raw), configured)
        output.parent.mkdir(parents=True, exist_ok=True)
        document = {"schemaVersion": 1, "sourceSha256": digest, "characters": rows}
        output.write_text(json.dumps(document, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
    except (OSError, ValueError) as error:
        parser.exit(1, f"{error}\n")

    count = sum(row["status"] == "configured" for row in rows)
    print(f"Wrote {len(rows)} entries ({count} configured, {len(rows) - count} pending) to {output}.")


if __name__ == "__main__":
    main()
