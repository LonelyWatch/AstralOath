"""Import the original per-hero arena encounter mark from the read-only asset dump."""

import argparse
from pathlib import Path
from ImportHudEffects import MODULE, RAW_DIRECTORY, collect

OUTPUT = MODULE / "Resources/AstralOathBattleArrival"
ROOTS = ("effect/encounter_01.cfx",)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=Path(r"D:\sevenepic"))
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    source = (args.source / RAW_DIRECTORY).resolve(strict=True)
    if OUTPUT.resolve().is_relative_to(source):
        raise ValueError("Cannot write battle arrival assets to the source directory.")
    mismatches = []
    for name, data in collect(source, roots=ROOTS).items():
        target = OUTPUT / name
        if target.is_file() and target.read_bytes() == data:
            continue
        if args.check:
            mismatches.append(name)
        else:
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(data)
    if mismatches:
        parser.exit(1, "Missing or changed battle arrival assets: " + ", ".join(mismatches) + "\n")
    print("Verified battle arrival assets." if args.check else "Imported battle arrival assets.")


if __name__ == "__main__":
    main()
