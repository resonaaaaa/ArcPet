"""Build and verify a portable ZIP from an already tested ArcPet runtime."""
import argparse
import hashlib
import json
import re
from pathlib import Path
from zipfile import ZIP_DEFLATED, ZipFile


ROOT = Path(__file__).resolve().parents[1]


def package(runtime, version):
    # Map distribution assets to their canonical source, so stale files and
    # local settings in the runtime directory can never enter the archive.
    assets = {"assets/manifest.json": ROOT / "manifest.json"}
    catalog = json.loads((ROOT / "manifest.json").read_text(encoding="utf-8-sig"))
    for clip in catalog["actions"].values():
        path = ROOT / clip["path"]
        assets[f"assets/{clip['path']}"] = path
        for frame in json.loads(path.read_text(encoding="utf-8-sig"))["frames"]:
            source = path.with_name(frame["file"])
            assets[f"assets/{source.relative_to(ROOT).as_posix()}"] = source
    moods = ROOT / "app/assets/moods"
    for source in moods.rglob("*"):
        if source.is_file():
            assets[f"assets/moods/{source.relative_to(moods).as_posix()}"] = source
    assets.update({"LICENSE": ROOT / "LICENSE", "NOTICE": ROOT / "NOTICE",
                   "使用说明.txt": ROOT / "app/USAGE.txt"})
    for name, source in assets.items():
        if (runtime / name).read_bytes() != source.read_bytes():
            raise ValueError(f"Runtime file differs from release source: {name}")

    names = set(assets) | {"ArcPet.Preview.exe", "ArcPet.Preview.exe.config",
                           "ArcPet.Animation.dll", "ArcPet.Behavior.dll", "ArcPet.Settings.dll"}
    package_name = f"ArcPet-v{version}"
    output = ROOT / "exports"
    output.mkdir(exist_ok=True)
    destination = output / f"{package_name}.zip"
    staging = destination.with_suffix(".tmp")
    try:
        with ZipFile(staging, "w", ZIP_DEFLATED) as archive:
            for name in sorted(names):
                archive.write(runtime / name, f"{package_name}/{name}")
        with ZipFile(staging) as archive:
            for name in names:
                if archive.read(f"{package_name}/{name}") != (runtime / name).read_bytes():
                    raise ValueError(f"ZIP member mismatch: {name}")
        digest = hashlib.sha256(staging.read_bytes()).hexdigest()
        staging.replace(destination)
    finally:
        staging.unlink(missing_ok=True)
    (output / f"{package_name}-SHA256.txt").write_text(
        f"{digest}  {destination.name}\n", encoding="utf8", newline="\n"
    )
    print(f"{destination.name}: {len(names)} verified files, {destination.stat().st_size} bytes, SHA-256 {digest}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("version")
    parser.add_argument("runtime", type=Path)
    args = parser.parse_args()
    if not re.fullmatch(r"\d+\.\d+\.\d+", args.version):
        parser.error("Use a version such as 0.21.1")
    package(args.runtime.resolve(), args.version)
