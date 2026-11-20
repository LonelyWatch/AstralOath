"""Exercise the actual shipped GUI launcher against a child that reports selected inputs."""
import argparse
import json
import os
from pathlib import Path
import shutil
import struct
import subprocess
import tempfile
import time

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("bundle", type=Path, help="Extracted trial directory containing runtime/python")
options = parser.parse_args()
qa = Path(__file__).resolve().parents[2] / ".artifacts/LocalPlay/check"
qa.mkdir(parents=True, exist_ok=True)
bundle = options.bundle.resolve()
launcher = bundle / "AstralOath-Play.exe"
assert launcher.exists(), "The double-clickable game launcher is missing"
image = launcher.read_bytes()
pe = int.from_bytes(image[0x3C:0x40], "little")
assert struct.unpack_from("<H", image, pe+4)[0] == 0x8664
assert struct.unpack_from("<H", image, pe+24+68)[0] == 2, "Expected Windows GUI subsystem"
with tempfile.TemporaryDirectory(prefix="试玩 引号 ", dir=qa) as directory:
    root = Path(directory)
    player_dir = root / "client"
    shutil.copytree(bundle / "runtime/python", player_dir)
    shutil.copy2(player_dir / "python.exe", player_dir / "AstralOath.exe")
    exe = shutil.copy2(launcher, root)
    # Check child CWD independently from the parent's and quote-sensitive argument forwarding.
    probe = root / "收集 输入.py"
    probe.write_text(
        "import json, os, sys, time\nfrom pathlib import Path\n"
        "Path(sys.argv[1]).write_text(json.dumps([os.getpid(), os.getcwd(), "
        "os.environ.get('ASTRAL_FOUNDATION_URL'), sys.argv[2:]]), encoding='utf-8')\n"
        "time.sleep(0.5)\n", encoding="utf-8")
    args = ["-astral-demo-echo", "-logFile", "中文 路径", "", '嵌入"引号', "with space\\", "& literal"]
    outputs = [root / f"输出 {i}.json" for i in range(2)]
    bad_url = "http://127.0.0.1:1"
    for out in outputs:
        run = subprocess.run([exe, "-I", "-B", probe, out, *args], cwd=qa,
                             env=dict(os.environ, ASTRAL_FOUNDATION_URL=bad_url), timeout=10)
        assert run.returncode == 0
    deadline = time.monotonic() + 8
    while not all(p.exists() for p in outputs) and time.monotonic() < deadline:
        time.sleep(0.05)
    results = [json.loads(p.read_text("utf-8")) for p in outputs]
    assert len(set(r[0] for r in results)) == 2, "Each invocation must start its own game process"
    time.sleep(0.7)
    for pid, cwd, url, child_args in results:
        assert Path(cwd) == player_dir and url == "http://localhost:7728" and child_args == args
    print("PASS: x64 GUI; two child processes; correct child URL and CWD; seven argument edge cases")
