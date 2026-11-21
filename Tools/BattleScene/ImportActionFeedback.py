"""Import the verified battle action, healing, and status gain feedback closures."""
import argparse
from pathlib import Path
from ImportHudEffects import MODULE, RAW_DIRECTORY, collect

OUTPUT = MODULE / "Res/Effects/ActionFeedback"
ROOTS = ("effect/ui_livepvp_soulburn_eff.cfx", "effect/stse_counter.cfx",
         "effect/stse_chain.cfx", "effect/stse_addturn.cfx", "effect/heal_hp_01.cfx",
         "effect/stse_attackpower_up.cfx", "effect/stse_defense_up.cfx", "effect/stse_immune_weak.cfx",
         "effect/stse_speed_up.cfx", "effect/stse_evade.cfx", "effect/stse_immortality.cfx",
         "effect/stse_defense_reflect.cfx", "effect/stse_critup.cfx")

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=Path("D:/sevenepic"))
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    source = (args.source / RAW_DIRECTORY).resolve(strict=True)
    if OUTPUT.resolve().is_relative_to(source):
        raise ValueError("Feedback assets cannot be written to the read-only source directory")
    mismatches = []
    payloads = collect(source, roots=ROOTS)
    for name, data in payloads.items():
        target = OUTPUT / name
        if target.is_file() and target.read_bytes() == data:
            continue
        if args.check:
            mismatches.append(name)
        else:
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(data)
    if mismatches:
        parser.exit(1, "Missing or changed action feedback assets: " + ", ".join(mismatches) + "\n")
    print(("Verified" if args.check else "Imported") + f" {len(payloads)-1} action feedback assets and provenance manifest.")

if __name__ == "__main__":
    main()
