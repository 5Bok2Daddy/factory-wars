"""Import the selected local Cafe assets and prepare the Asphalt 02 normal map."""

import argparse
from pathlib import Path
import shutil
import subprocess
import tarfile
import tempfile
import zipfile


CAFE_FILES = {
    "Prefabs/SM_Bld_Cafe_01.prefab",
    "Models/SM_Bld_Cafe_01.fbx",
    "Materials/PolygonCoffeeShop_01.mat",
    "Materials/PolygonCoffeeShop_01_Glass.mat",
    "Texture/Polygon_Texture_01_A.png",
}
CAFE_ROOT = "Assets/Synty/PolygonCoffeeShop/"


def import_cafe(project, package):
    selected = {}
    with tarfile.open(package, "r|gz") as archive:
        for member in archive:
            if member.isfile() and member.name.endswith("/pathname"):
                path = archive.extractfile(member).read().decode("utf-8").strip()
                if path.startswith(CAFE_ROOT) and path[len(CAFE_ROOT):] in CAFE_FILES:
                    selected[member.name.rsplit("/", 1)[0]] = path[len(CAFE_ROOT):]
    if set(selected.values()) != CAFE_FILES:
        raise ValueError("The Cafe package does not match the required files")

    files = {}
    with tarfile.open(package, "r|gz") as archive:
        for member in archive:
            group, _, kind = member.name.rpartition("/")
            if member.isfile() and group in selected and kind in {"asset", "asset.meta"}:
                relative = selected[group] + (".meta" if kind == "asset.meta" else "")
                target = project / "Assets/ExternalAssets/Synty/PolygonCoffeeShop" / relative
                files[target] = archive.extractfile(member).read()
    if len(files) != 2 * len(CAFE_FILES):
        raise ValueError("The Cafe package is missing a file or Unity metadata")
    for target, data in files.items():
        if target.exists() and target.read_bytes() != data:
            raise ValueError(f"Existing asset differs: {target}")
    for target, data in files.items():
        if not target.exists():
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(data)
    print(f"Cafe: {len(CAFE_FILES)} assets with original Unity GUIDs")


def prepare_asphalt(project, archive, ffmpeg):
    target = project / "Assets/QuantumUser/Resources/FactoryWars/Art/Asphalt02Normal.png"
    with tempfile.TemporaryDirectory() as temporary:
        source = Path(temporary) / "asphalt_normal.exr"
        output = Path(temporary) / target.name
        with zipfile.ZipFile(archive) as zipped:
            with zipped.open("textures/asphalt_02_nor_gl_4k.exr") as image, source.open("wb") as unpacked:
                shutil.copyfileobj(image, unpacked)
        subprocess.run([
            ffmpeg, "-v", "error", "-i", str(source), "-frames:v", "1", "-vf", "scale=1024:1024:flags=area",
            "-pix_fmt", "rgb24", "-y", str(output),
        ], check=True)
        if target.exists() and target.read_bytes() != output.read_bytes():
            raise ValueError(f"Existing texture differs: {target}")
        if not target.exists():
            shutil.copyfile(output, target)
    print("Asphalt 02: 1024 x 1024 tangent-space normal map")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--ffmpeg", default="ffmpeg", help="Path to the local FFmpeg executable")
    args = parser.parse_args()
    project = Path(__file__).resolve().parents[1]
    source = project.parent / "temp-assets"
    import_cafe(project, source / "Synty/POLYGON_Coffee_Shop_Unity_2022_3_v1_2_2.unitypackage")
    prepare_asphalt(project, source / "asphalt_02_4k.blend.zip", args.ffmpeg)


if __name__ == "__main__":
    main()
