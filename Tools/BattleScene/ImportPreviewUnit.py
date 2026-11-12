"""Import the explicit slime preview dependency from read-only SevenEpic."""
import argparse
import hashlib
import json
import shutil
import sys
from pathlib import Path

TOOLS = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(TOOLS / "CharacterAssets"))
from convert import convert_scsp, convert_sct

MODULE = TOOLS.parent / "Assets/Game/Modules/BattleScene"
OUTPUT = MODULE / "Res/Units/Slime"


def sha256(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", default=r"D:\sevenepic", type=Path)
    args = parser.parse_args()
    root = args.source.resolve(strict=True)
    model = root / "原始资源/model"
    OUTPUT.mkdir(parents=True, exist_ok=True)
    entries = []
    for name, output, converter in (
        ("slime.scsp", "slime.spine.json", convert_scsp),
        ("slime.sct", "slime.png", convert_sct),
        ("slime.atlas", "slime.atlas.txt", shutil.copyfile),
    ):
        source = model / name
        target = OUTPUT / output
        converter(source, target)
        entries.append({
            "source": source.relative_to(root).as_posix(),
            "sourceSha256": sha256(source),
            "asset": target.relative_to(MODULE).as_posix(),
            "assetSha256": sha256(target),
        })

    converter_path = TOOLS / "CharacterAssets/convert.py"
    manifest = {"converterSha256": sha256(converter_path), "files": entries}
    (OUTPUT / "source.json").write_text(json.dumps(manifest, indent=2, ensure_ascii=False) + "\n",
                                      encoding="utf-8", newline="\n")
    skeleton = json.loads((OUTPUT / "slime.spine.json").read_text(encoding="utf-8"))
    print("Slime Spine", skeleton["skeleton"]["spine"], "skins", list(skeleton["skins"]),
          "idle", [name for name in skeleton["animations"] if "idle" in name])


if __name__ == "__main__":
    main()
