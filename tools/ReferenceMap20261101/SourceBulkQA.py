"""Compare per-shell source bounds with a large collection of voxel assets."""

import argparse
import json
from collections import Counter, defaultdict
from pathlib import Path

import numpy as np


parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("source_object")
parser.add_argument("geometry_dir", type=Path)
parser.add_argument("output", type=Path)
args = parser.parse_args()
root = Path("Documentation/ReferenceMap-2026-11-01")
component_file = root / "source-meshes/source-components.json"
components = json.loads(component_file.read_text(encoding="utf-8"))
source = next(o for o in components["objects"] if o["name"] == args.source_object)
source_bounds = {c["id"]: c["world_bounds"] for c in source["components"]}
calibration = json.loads((root / "source-calibration.json").read_text(encoding="utf-8"))
matrix = np.array(calibration["sourceWorldToCanonicalUnityMatrixRows"])
geometry = json.loads((args.geometry_dir / "geometry-manifest.json").read_text(encoding="utf-8"))


def canonical_aabb(bounds):
    mn, mx = np.array(bounds["min"]), np.array(bounds["max"])
    corners = np.array([[x, y, z] for x in (mn[0], mx[0])
                        for y in (mn[1], mx[1]) for z in (mn[2], mx[2])])
    transformed = corners @ matrix[:3, :3].T + matrix[:3, 3]
    return transformed.min(0), transformed.max(0)


def asset_aabb(asset):
    with np.load(args.geometry_dir / asset["npz"]) as data:
        cells = data["position"].astype(float)
    if "rotationMatrix" in asset:
        rotation = np.array(asset["rotationMatrix"]).reshape(3, 3)
    else:
        assert asset["eulerAngles"] == [0, 0, 0], "Unsupported non-identity Euler without rotationMatrix"
        rotation = np.eye(3)
    position = np.array(asset["position"])
    size = float(asset["cellSize"])
    centers = (cells * size) @ rotation.T + position
    half = np.sum(np.abs(rotation), axis=1) * size / 2
    return centers.min(0) - half, centers.max(0) + half


asset_groups = defaultdict(list)
for asset in geometry["assets"]:
    if asset["source"]["object"] == args.source_object:
        asset_groups[asset["source"]["sourceShell"]].append(asset)
records = []
for shell_id, assets in sorted(asset_groups.items()):
    if shell_id not in source_bounds:
        raise ValueError(f"No source bounds for shell {shell_id}")
    smn, smx = canonical_aabb(source_bounds[shell_id])
    vox_bounds = [asset_aabb(asset) for asset in assets]
    vmn = np.min([x[0] for x in vox_bounds], axis=0)
    vmx = np.max([x[1] for x in vox_bounds], axis=0)
    error = np.max(np.abs(np.r_[vmn - smn, vmx - smx]))
    cell_size = max(float(a["cellSize"]) for a in assets)
    records.append({"sourceShell": shell_id, "assetCount": len(assets),
                    "cells": sum(a["cells"] for a in assets),
                    "cellSizeMetres": cell_size,
                    "maxAbsoluteBoundaryErrorMetres": float(error),
                    "maxBoundaryErrorInCells": float(error / cell_size),
                    "boundaryDeltaMinMetres": (vmn-smn).tolist(),
                    "boundaryDeltaMaxMetres": (vmx-smx).tolist()})
errors = np.array([r["maxAbsoluteBoundaryErrorMetres"] for r in records])
normalized = np.array([r["maxBoundaryErrorInCells"] for r in records])
sizes = Counter(r["cellSizeMetres"] for r in records)
summary = {
    "sourceObject": args.source_object,
    "sourceShellsWithAssets": len(records),
    "assets": sum(len(v) for v in asset_groups.values()),
    "cells": sum(r["cells"] for r in records),
    "maxAssetCells": max(a["cells"] for a in geometry["assets"]),
    "allAssetsInitiallyConnected": all(a["connectedComponents"] == 1 for a in geometry["assets"]),
    "maxBoundaryErrorMetres": float(errors.max()),
    "p50BoundaryErrorMetres": float(np.percentile(errors, 50)),
    "p95BoundaryErrorMetres": float(np.percentile(errors, 95)),
    "shellsOverTwoMetres": int((errors > 2).sum()),
    "cellSizeCounts": {str(k): v for k, v in sorted(sizes.items())},
    "maxBoundaryErrorInCells": float(normalized.max()),
    "p95BoundaryErrorInCells": float(np.percentile(normalized, 95)),
    "shellsOverOneCellBoundaryError": int((normalized > 1).sum()),
    "records": records,
}
args.output.parent.mkdir(parents=True, exist_ok=True)
args.output.write_text(json.dumps(summary, separators=(",", ":")), encoding="utf-8")
print(json.dumps({k: v for k, v in summary.items() if k != "records"}, indent=2))
