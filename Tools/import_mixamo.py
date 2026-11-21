"""Stage the user's Mixamo archives and copy only clips used by the View controllers."""

import hashlib
import json
import shutil
import zipfile
from pathlib import Path, PurePosixPath


ROLES = {
    "Workers_Logistics": {
        "WheelbarrowWalk": ("Wheelbarrow Walk", True),
        "BoxJog": ("Jogging With Box", True),
        "BoxIdle": ("Box Idle", True),
        "HoldingWalk": ("Holding Walk", True),
        "HoldingIdle": ("Holding Idle", True),
        "Lift": ("Lifting Object", False),
        "PickUp": ("Picking Up Object", False),
        "PutDown": ("Putting Down", False),
    },
    "Engineers_Harvesting": {
        "Device": ("Working On Device", True),
        "Button": ("Button Pushing", False),
        "Lever": ("Pulling Lever", False),
        "Inspect": ("Kneeling Inspecting", True),
        "OpenLid": ("Opening A Lid", False),
        "CloseLid": ("Closing A Lid", False),
        "Dig": ("Digging", True),
        "Push": ("Push", True),
        "Pull": ("Pull Heavy Object", True),
        "Chop": ("standing melee attack downward", True),
    },
    "Infantry_Combat": {
        "CoverIdle": ("Cover Idle", True),
        "EnterCover": ("Stand To Cover", False),
        "LeaveCover": ("Emerge From Cover", False),
        "Reload": ("Reloading", False),
        "Hit": ("Hit Reaction", False),
        "DeathFront": ("Death From The Front", False),
        "DeathBack": ("Death From The Back", False),
        "DeathSide": ("Death From Right", False),
        "DeathCrouch": ("Crouch Death", False),
        "DeathProne": ("Prone Death", False),
    },
    "Medic_Support": {
        "Aid": ("Administering Cpr", True),
        "InjuredIdle": ("Injured Idle", True),
        "InjuredRun": ("Injured Run", True),
    },
    "Vehicle_Crew": {
        "Drive": ("Driving", True),
        "ExitVehicle": ("Exiting Car", False),
        "Pilot": ("Piloting", True),
    },
}


def copy(source, target):
    target.parent.mkdir(parents=True, exist_ok=True)
    if target.exists():
        if hashlib.file_digest(source, "sha256").digest() != hashlib.sha256(target.read_bytes()).digest():
            raise ValueError(f"Existing file differs: {target}")
        return
    with target.open("xb") as output:
        shutil.copyfileobj(source, output)


def unpack(archive, destination):
    with zipfile.ZipFile(archive) as files:
        for entry in files.infolist():
            path = PurePosixPath(entry.filename)
            if path.is_absolute() or any(part == ".." or ":" in part or "|" in part or "\\" in part for part in path.parts):
                raise ValueError(f"Invalid archive path: {entry.filename}")
            if not entry.is_dir():
                with files.open(entry) as source:
                    copy(source, destination.joinpath(*path.parts))


def main():
    game = Path(__file__).resolve().parents[1]
    roots = [game.parent / "temp-asset", game / "temp-asset", game.parent / "temp-assets", game / "temp-assets"]
    archives = [root / "mixamo.zip" for root in roots if (root / "mixamo.zip").is_file()]
    staging = roots[0] / "mixamo" / "_extracted"
    if archives:
        unpack(archives[0], staging)
    axe_archives = [root / "Pro Melee Axe Pack.zip" for root in [staging, *roots]
                    if (root / "Pro Melee Axe Pack.zip").is_file()]
    if axe_archives:
        unpack(axe_archives[0], staging / "axe")
    output = game / "Assets/ThirdParty/MixamoAnimations"
    clips = []
    for role, motions in ROLES.items():
        for key, (name, loop) in motions.items():
            filename = f"X Bot@{name}.fbx"
            candidates = [root / filename for root in [staging, *roots]]
            if key == "Chop":
                candidates.insert(0, staging / "axe" / (name + ".fbx"))
            source = next((path for path in candidates if path.is_file()), None)
            if source is None:
                raise FileNotFoundError(filename)
            relative = f"{role}/{key}.fbx"
            with source.open("rb") as data:
                copy(data, output / relative)
            clips.append({"Path": relative, "Role": role, "Key": key, "Loop": loop})
    (output / "clips.json").write_text(json.dumps({"Clips": clips}, indent=2), encoding="utf-8")
    print(f"Staged {len(list(staging.rglob('*.fbx')))} FBX files. Selected {len(clips)} across {len(ROLES)} roles.")


if __name__ == "__main__":
    main()
