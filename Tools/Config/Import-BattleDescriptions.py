"""Import official zh-CN battle text, or verify Luban's localization input."""

import argparse
import csv
import hashlib
import json
import re
from decimal import Decimal
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
CONFIG = ROOT / "Config"
TABLES = CONFIG / "Tables"
SOURCE = CONFIG / "BattleText" / "original-zhs.json"
MESSAGES = TABLES / "battle_messages.csv"
STATUS_ASSETS = ROOT / "Assets/Game/Modules/BattleScene/Res/Textures/Hud/Statuses/source.json"
PERCENT = re.compile(r"\{per(?:_s)?\((\w+)\)\}")
COLOR = re.compile(r"<#([\da-fA-F]{6})>")
TEXT_PATH = "text/zhs/text.db"
STATUS_TEXT_IDS = {"stic_debuf_impossible": "stic_res_up", "stic_spiritlord_keep": "stic_spiritlord"}


def read_json(path):
    return json.loads(path.read_text(encoding="utf-8"))


def read_csv(path):
    with path.open(encoding="utf-8", newline="") as source:
        return list(csv.DictReader(source))


def require(condition, message):
    if not condition:
        raise ValueError(message)


def records(export, name):
    columns = read_json(export / f"{name}.columns.json")
    return {
        key: {field: values[index] for field, index in columns.items()}
        for key, values in read_json(export / f"{name}.source.json").items()
    }


def build_source(export, release, manifest_sha256):
    text = read_json(export / "text.zhs.json")
    skills = records(export, "skill_player")
    statuses = records(export, "cs_player")
    entries = {}

    def add(key, text_id, source_id, row=None, section_prefix=None):
        raw = text[text_id]
        require(bool(raw), f"Empty original value: {text_id}")
        entry = {"sourceId": source_id, "textId": text_id, "text": raw}
        if section_prefix is not None:
            entry["sectionPrefix"] = section_prefix
        fields = {}
        for name in PERCENT.findall(raw):
            if name == "ps_refer":
                passive = statuses[row["sk_passive"]]
                fields[name] = {"sourceField": passive["id"] + ".cs_refer", "value": passive["cs_refer"]}
            else:
                fields[name] = {"sourceField": row["id"] + "." + name, "value": row[name]}
        if fields:
            entry["parameters"] = fields
        entries[key] = entry

    for skill in read_csv(TABLES / "battle_skills.csv"):
        base_id = f"sk_{skill['heroId']}_{skill['slot']}"
        base = skills[base_id]
        upgraded = skills.get(base_id + "u")
        awakened = upgraded and upgraded["base_skill"] == base_id and upgraded["parent_skill"] == upgraded["id"]
        selected = upgraded if awakened else base
        prefix = f"battle.skill.{skill['id']}"
        add(skill["nameKey"], selected["name"], selected["id"])
        add(skill["descriptionKey"], selected["sk_description"], selected["id"], selected)
        if awakened:
            add(prefix + ".unawakened.description", base["sk_description"], base_id, base)
        burn_id = selected["soulburn_skill"]
        require(bool(burn_id) == (int(skill["soulBurnCost"]) > 0), f"Soulburn coverage differs for {base_id}")
        if burn_id:
            burn = skills[burn_id]
            add(prefix + ".burn.description", burn["sk_description"], burn_id, burn)
        if skill["speedUpDescriptionKey"]:
            options = selected["skillset"].split(",")
            require(len(options) == 2, f"Review original speed-up variant selection for {base_id}")
            variant = skills[options[1]]
            add(skill["speedUpNameKey"], variant["name"], variant["id"])
            variant_title = text[variant["name"]]
            description_id = variant["sk_description"]
            if description_id in text:
                add(skill["speedUpDescriptionKey"], description_id, variant["id"], variant)
            else:
                add(skill["speedUpDescriptionKey"], selected["sk_description"], selected["id"], selected,
                    section_prefix=variant_title + "：")

    for status in read_json(STATUS_ASSETS)["statuses"]:
        texture_id = Path(status["asset"]).stem
        text_id = STATUS_TEXT_IDS.get(texture_id, texture_id)
        prefix = "battle.status." + status["kind"].lower()
        add(prefix + ".name", text_id + "_skeffnm", texture_id)
        add(prefix + ".description", text_id + "_de", texture_id)

    add("battle.inspect.dual_attack.name", "stic_coop_skeffnm", "stic_coop")
    add("battle.inspect.dual_attack.description", "stic_coop_de", "stic_coop")
    return {
        "schemaVersion": 1,
        "locale": "zh-CN",
        "release": release,
        "manifestSha256": manifest_sha256,
        "textPath": TEXT_PATH,
        "textSha256": hashlib.sha256((export / "text.zhs.raw").read_bytes()).hexdigest(),
        "entries": dict(sorted(entries.items())),
    }


def render(entry):
    raw = entry["text"]
    if prefix := entry.get("sectionPrefix"):
        matches = [line[len(prefix):] for line in raw.split(r"\n") if line.startswith(prefix)]
        require(len(matches) == 1, f"Cannot find unique original section in {entry['textId']}")
        raw = matches[0]

    def percent(match):
        parameter = entry["parameters"][match[1]]
        value = Decimal(parameter["value"]) * 100
        return format(value.normalize(), "f")

    value = PERCENT.sub(percent, raw)
    value = COLOR.sub(r"<color=#\1>", value).replace("</>", "</color>").replace(r"\n", "\n")
    require(not any(marker in value for marker in ("{", "}", "<#", "</>")),
            f"Unsupported display syntax in {entry['textId']}")
    return value


def validate_coverage(entries):
    expected = {"battle.inspect.dual_attack.name", "battle.inspect.dual_attack.description"}
    for skill in read_csv(TABLES / "battle_skills.csv"):
        expected.update((skill["nameKey"], skill["descriptionKey"]))
        if int(skill["soulBurnCost"]):
            expected.add(f"battle.skill.{skill['id']}.burn.description")
        if skill["speedUpNameKey"]:
            expected.update((skill["speedUpNameKey"], skill["speedUpDescriptionKey"]))
    for status in read_json(STATUS_ASSETS)["statuses"]:
        prefix = "battle.status." + status["kind"].lower()
        expected.update((prefix + ".name", prefix + ".description"))
    missing = expected - entries.keys()
    require(not missing, "Missing original text: " + ", ".join(sorted(missing)))


def sync_messages(entries, check):
    rows = read_csv(MESSAGES)
    index = {row["key"]: row for row in rows}
    require(len(index) == len(rows), "Duplicate keys in battle_messages.csv")
    for key, entry in entries.items():
        value = render(entry)
        if check:
            require(key in index and index[key]["zhCn"] == value, f"Original text has drifted: {key}")
            continue
        if key not in index:
            row = {"##": "", "key": key, "zhCn": value}
            index[key] = row
            rows.append(row)
        else:
            index[key]["zhCn"] = value
    if not check:
        with MESSAGES.open("w", encoding="utf-8", newline="") as target:
            writer = csv.DictWriter(target, fieldnames=("##", "key", "zhCn"), lineterminator="\n")
            writer.writeheader()
            writer.writerows(rows)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--check", action="store_true", help="Validate the current table against the checked-in source.")
    mode.add_argument("--sync", action="store_true", help="Refresh the current table from the checked-in source.")
    mode.add_argument("--import-export", type=Path, help="Import selected rows from a locally decoded game-data export.")
    parser.add_argument("--release", help="Official source release ID, required with --import-export.")
    parser.add_argument("--manifest-sha256", help="SHA-256 of the official release manifest, required with --import-export.")
    options = parser.parse_args()
    if options.import_export:
        if not options.release or not re.fullmatch(r"[\da-fA-F]{64}", options.manifest_sha256 or ""):
            parser.error("--import-export requires --release and a valid --manifest-sha256")
        source = build_source(options.import_export, options.release, options.manifest_sha256.lower())
    else:
        source = read_json(SOURCE)
    entries = source["entries"]
    validate_coverage(entries)
    for key, entry in entries.items():
        display_hash = hashlib.sha256(render(entry).encode("utf-8")).hexdigest()
        if options.import_export:
            entry["displaySha256"] = display_hash
        else:
            require(entry["displaySha256"] == display_hash, f"Original text checksum differs: {key}")
    if options.import_export:
        SOURCE.parent.mkdir(exist_ok=True)
        SOURCE.write_text(json.dumps(source, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
    sync_messages(entries, options.check)
    print(f"Verified {len(entries)} original {source['locale']} battle text entries ({source['release']}).")


if __name__ == "__main__":
    main()
