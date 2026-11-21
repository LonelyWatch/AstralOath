"""Import the RTA victory/defeat emblem, its shared animation, and the level-one heart."""
import argparse
from pathlib import Path
from ImportHudEffects import MODULE, RAW_DIRECTORY, collect

ROOTS = ("effect/eff_pvp_outro_victory.cfx", "effect/eff_pvp_outro_defeat.cfx",
         "img/hero_love_icon_1.png", "img/_dim.png", "img/_gradient.png", "img/_bar_6.png")
OUTPUT = MODULE / "Res/Online/AstralOathBattleResult"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=Path("D:/sevenepic"))
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    payloads = collect((args.source / RAW_DIRECTORY).resolve(strict=True), ROOTS)
    mismatches = []
    for name, data in payloads.items():
        path = OUTPUT / name
        if path.is_file() and path.read_bytes() == data:
            continue
        if args.check:
            mismatches.append(name)
        else:
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(data)
    if mismatches:
        parser.exit(1, "Missing or changed battle result assets: " + ", ".join(mismatches) + "\n")
    print(f"{'Verified' if args.check else 'Imported'} {len(payloads) - 1} battle result assets and provenance manifest.")


if __name__ == "__main__":
    main()
