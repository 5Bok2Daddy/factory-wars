"""Copy the map-art allowlist and its Unity GUID dependencies out of the staged bundle."""

import argparse
import hashlib
import re
import shutil
import tarfile
import tempfile
import zipfile
from pathlib import Path, PurePosixPath


PACKS = {
    "Desert": ("Desert Pack.zip", {
        "CanyonRock": "Rock_Formation_1",
        "SandRidge": "Rock_Formation_3",
        "DesertBoulder": "Rock_11",
    }),
    "Bridges": ("RoadsideTalesBackroadBridgesStylizedModularKit-*.unitypackage", {
        "BridgeBase": "EA_ModBridge_8X10Base_01b_PRE",
        "BridgePier": "EA_arch_Pier3_Med_3x10_02d_PRE",
        "BridgeRail": "EA_arch_Barrier2_8x10_01b_PRE",
    }),
    "Woodland": ("RemakeStylizedWoodlandMegaArtPack-*.unitypackage", {
        "WoodlandTree": "EA01_Env_Tree_04a_PRE",
        "WoodlandTallTree": "EA01_Env_Tree_11a_PRE",
        "WoodlandStump": "EA01_Prop_Tool_Stump_01a_PRE",
    }),
    "Meadow": ("NatureForgeLITEStylizedMeadowFarmKit-*.unitypackage", {
        "MeadowShrub": "EA_M_Env_Shrub_01a_PRE",
        "MeadowRock": "EA_Env_Rock_19_PRE",
    }),
}

GUID = re.compile(rb"\bguid:\s*([0-9a-f]{32})\b")
ASSET_TYPES = {
    ".prefab", ".fbx", ".obj", ".mat", ".asset", ".png", ".tga", ".bmp",
    ".jpg", ".jpeg", ".psd", ".dds", ".shader", ".shadergraph", ".shadersubgraph",
    ".hlsl", ".cginc", ".giparams",
}
REFERENCE_TYPES = {".prefab", ".mat", ".asset", ".shadergraph", ".shadersubgraph"}


def unity_files(package):
    groups = {}
    with tarfile.open(package, "r:gz") as archive:
        for item in archive:
            key, _, field = item.name.rpartition("/")
            if item.isfile() and re.fullmatch(r"[0-9a-f]{32}", key) and field in {"asset", "asset.meta", "pathname"}:
                groups.setdefault(key.encode(), {})[field] = archive.extractfile(item).read()
    return {key: (PurePosixPath(fields["pathname"].decode("utf-8").strip()), fields)
            for key, fields in groups.items() if {"asset", "asset.meta", "pathname"} <= fields.keys()}


def select_assets(entries, aliases, destination):
    roots = {}
    for alias, name in aliases.items():
        matches = [key for key, (path, _) in entries.items() if path.name == name + ".prefab"]
        if len(matches) != 1:
            raise ValueError(f"Expected one prefab {name}, found {len(matches)}")
        roots[matches[0]] = alias
    selected = {}
    queue = list(roots)
    while queue:
        key = queue.pop()
        if key in selected:
            continue
        path, files = entries[key]
        if (path.parts[0] != "Assets" or ".." in path.parts or
                any(":" in part or "\\" in part for part in path.parts) or path.suffix.lower() not in ASSET_TYPES):
            raise ValueError(f"Unexpected dependency: {path}")
        out = (destination / "Selected" / (roots[key] + ".prefab") if key in roots
               else destination.joinpath("Source", *path.parts[1:]))
        selected[key] = out, files
        for source in (files["asset.meta"], files["asset"] if path.suffix.lower() in REFERENCE_TYPES else b""):
            queue.extend(ref for ref in GUID.findall(source) if ref in entries and ref not in selected)
    return selected


def main():
    repo = Path(__file__).resolve().parents[1]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("staging", type=Path, nargs="?",
                        default=repo.parent / "temp-asset/new bundle/_extracted")
    args = parser.parse_args()
    output = repo / "Assets/ThirdParty/NewBundle"
    chosen = []
    for pack, (pattern, aliases) in PACKS.items():
        sources = list(args.staging.glob(pattern))
        if len(sources) != 1:
            raise ValueError(f"Expected one {pattern}, found {len(sources)}")
        source = sources[0]
        if source.suffix == ".zip":
            with tempfile.TemporaryDirectory(dir=args.staging) as temporary, zipfile.ZipFile(source) as zipped:
                names = [name for name in zipped.namelist() if name.endswith(".unitypackage")]
                if len(names) != 1:
                    raise ValueError(f"Expected one Unity package in {source.name}")
                package = Path(temporary) / "desert.unitypackage"
                with zipped.open(names[0]) as src, package.open("wb") as dst:
                    shutil.copyfileobj(src, dst)
                selected = select_assets(unity_files(package), aliases, output / pack)
        else:
            selected = select_assets(unity_files(source), aliases, output / pack)
        # Two vendor packages ship different materials under the same GUID.
        remap = {key: hashlib.md5(b"FactoryWars/NewBundle/" + pack.encode() + b"/" + key,
                                  usedforsecurity=False).hexdigest().encode() for key in selected}
        def rewrite(data):
            return GUID.sub(lambda m: m.group().replace(m[1], remap.get(m[1], m[1])), data)
        for path, files in selected.values():
            data = rewrite(files["asset"]) if path.suffix.lower() in REFERENCE_TYPES else files["asset"]
            chosen.append((path, {"asset": data, "asset.meta": rewrite(files["asset.meta"])}))
        print(f"{pack}: {len(aliases)} selected prefabs, {len(selected)-len(aliases)} dependencies")
    for path, files in chosen:
        meta = Path(str(path) + ".meta")
        expected_guid = GUID.search(files["asset.meta"])
        installed_guid = GUID.search(meta.read_bytes()) if meta.is_file() else None
        same_asset = installed_guid is not None and expected_guid is not None and installed_guid[1] == expected_guid[1]
        for suffix in ("", ".meta"):
            target = Path(str(path) + suffix)
            source = files["asset" + suffix]
            if target.exists() and target.read_bytes() != source:
                # Unity can upgrade material YAML and importer settings on first import.
                if same_asset and (suffix == ".meta" or path.suffix == ".mat"):
                    continue
                raise ValueError(f"Existing file differs: {target}")
    for path, files in chosen:
        path.parent.mkdir(parents=True, exist_ok=True)
        for suffix in ("", ".meta"):
            target = Path(str(path) + suffix)
            if not target.exists():
                target.write_bytes(files["asset" + suffix])
    print(f"{len(chosen)} unique assets in {output}")


if __name__ == "__main__":
    main()
