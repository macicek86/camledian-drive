"""Build the pinned rclone engine with the reviewed, opt-in VFS write preflight.
Requires Go and git. No token or private repository is used.
"""
import argparse
import os
from pathlib import Path
import shutil
import subprocess

parser = argparse.ArgumentParser()
parser.add_argument("--output", default="artifacts/win-x64/tools/rclone.exe")
parser.add_argument("--source", default="artifacts/rclone-source")
args = parser.parse_args()
root = Path(__file__).resolve().parent.parent
source = (root / args.source).resolve()
output = (root / args.output).resolve()
revision = "687d264b689b8c49a67e2e52a8a5e0caa01c04ce"  # rclone v1.75.1
patch = root / "vendor/rclone/v1.75.1-write-guard.patch"

def run(*command, **kwargs):
    return subprocess.run(command, check=True, **kwargs)

if source.exists():
    raise SystemExit(f"Build source directory already exists: {source}. Choose a fresh --source directory.")
run("git", "clone", "--depth", "1", "--branch", "v1.75.1", "https://github.com/rclone/rclone.git", str(source))
head = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=source, text=True).strip()
if head != revision:
    raise SystemExit("Upstream tag no longer matches pinned revision")
run("git", "apply", "--check", str(patch), cwd=source)
run("git", "apply", str(patch), cwd=source)
env = dict(os.environ, CGO_ENABLED="0", GOWORK="off")
run("go", "test", "./backend/webdav", "-run", "^TestVFSWriteGuard", cwd=source, env=env)
output.parent.mkdir(parents=True, exist_ok=True)
command = ["go", "build", "-trimpath", "-ldflags", "-s -w -X github.com/rclone/rclone/fs.Version=v1.75.1-camledian.1"]
if os.name == "nt":
    # cgofuse supports native WinFsp loading without a C toolchain on Windows.
    command += ["-tags", "cmount"]
run(*command, "-o", str(output), ".", cwd=source, env=env)
shutil.copyfile(source / "COPYING", output.parent / "rclone-LICENSE.txt")
(output.parent / "rclone-source.txt").write_text(
    f"rclone v1.75.1-camledian.1\nUpstream: https://github.com/rclone/rclone/tree/{revision}\n"
    "Patch: https://github.com/macicek86/camledian-drive/tree/main/vendor/rclone\n", encoding="utf-8")
run(str(output), "version")
if os.name == "nt":
    run(str(output), "mount", "--help", stdout=subprocess.DEVNULL)
