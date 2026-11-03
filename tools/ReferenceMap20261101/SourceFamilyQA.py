"""Check station remainder families against exact source shell bounds and IDs."""

import json
from collections import defaultdict
from pathlib import Path

import numpy as np


root = Path("Documentation/ReferenceMap-2026-11-01")
directory = Path("Temp/ReferenceMap20261101/StationRemainder")
manifest = json.loads((directory / "geometry-manifest.json").read_text(encoding="utf-8"))
components = json.loads((root / "source-meshes/source-components.json").read_text(encoding="utf-8"))
station = next(o for o in components["objects"] if o["name"] == "Cylinder.018")
source_bounds = {x["id"]: x["world_bounds"] for x in station["components"]}
calibration = json.loads((root / "source-calibration.json").read_text(encoding="utf-8"))
matrix = np.array(calibration["sourceWorldToCanonicalUnityMatrixRows"])
assets_by_family = defaultdict(list)
for asset in manifest["assets"]:
    assets_by_family[asset["source"]["family"]].append(asset)


def source_aabb(shell_ids):
    all_min = np.min([source_bounds[i]["min"] for i in shell_ids], axis=0)
    all_max = np.max([source_bounds[i]["max"] for i in shell_ids], axis=0)
    corners = np.array([[x, y, z] for x in (all_min[0], all_max[0])
                        for y in (all_min[1], all_max[1]) for z in (all_min[2], all_max[2])])
    world = corners @ matrix[:3, :3].T + matrix[:3, 3]
    return world.min(0), world.max(0)


def voxel_aabb(assets):
    mins, maxs = [], []
    for asset in assets:
        with np.load(directory / asset["npz"]) as data:
            local = data["position"].astype(float)
        rotation = np.array(asset["rotationMatrix"]).reshape(3, 3)
        center = np.array(asset["position"])
        size = float(asset["cellSize"])
        world = (local * size) @ rotation.T + center
        half = np.abs(rotation).sum(axis=1) * size / 2
        mins.append(world.min(0)-half)
        maxs.append(world.max(0)+half)
    return np.min(mins, axis=0), np.max(maxs, axis=0)


records = []
for family in manifest["families"]:
    name = family["family"]
    assets = assets_by_family[name]
    assert len(assets) == family["assets"]
    assert sum(x["cells"] for x in assets) == family["cells"]
    smn, smx = source_aabb(family["sourceShells"])
    vmn, vmx = voxel_aabb(assets)
    delta = np.r_[vmn-smn, vmx-smx]
    record = {"family": name, "sourceShells": len(family["sourceShells"]),
              "assets": len(assets), "cells": family["cells"],
              "maxAssetCells": max(x["cells"] for x in assets),
              "maxBoundaryErrorMetres": float(np.max(np.abs(delta))),
              "boundaryDeltaMinMetres": (vmn-smn).tolist(),
              "boundaryDeltaMaxMetres": (vmx-smx).tolist(),
              "allAssetsInitiallyConnected": all(x["connectedComponents"] == 1 for x in assets)}
    records.append(record)
    print(name, "shells", record["sourceShells"], "assets", record["assets"],
          "cells", record["cells"], "maxError", round(record["maxBoundaryErrorMetres"], 3))

covered = set(manifest["coveredSourceShells"])
already = set(manifest["alreadyAuthoredSourceShells"])
summary = {"sourceObject": "Cylinder.018", "occupancyMode": manifest["occupancyMode"],
           "families": records, "coveredShellCount": len(covered),
           "alreadyAuthoredShellCount": len(already),
           "coveredAndAlreadyDisjoint": not bool(covered & already),
           "all602ShellsAccountedFor": (covered | already) == set(range(602)),
           "assetCount": len(manifest["assets"]), "cellRecords": manifest["totalCells"],
           "allAssetsInitiallyConnected": all(x["connectedComponents"] == 1 for x in manifest["assets"]),
           "maxBoundaryErrorMetres": max(x["maxBoundaryErrorMetres"] for x in records)}
output = root / "source-qa-station-remainder.json"
output.write_text(json.dumps(summary, indent=2), encoding="utf-8")
print("SUMMARY", {k: v for k, v in summary.items() if k != "families"})
