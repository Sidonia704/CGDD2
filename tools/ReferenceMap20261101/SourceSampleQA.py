"""Compare source triangle and voxel-sample world bounds independently."""

import json
import shutil
from pathlib import Path

import numpy as np


root = Path("Documentation/ReferenceMap-2026-11-01")
meshes = root / "source-meshes"
manifest = json.loads((meshes / "source-meshes.json").read_text(encoding="utf-8"))
cal = json.loads((root / "source-calibration.json").read_text(encoding="utf-8"))
source = next(x for x in manifest["objects"] if x["name"] == "Cylinder.018")
data = np.load(meshes / source["npz"])
labels = np.load(meshes / source["npz"].replace(".npz", "-components.npz"))["vertex_component"]
source_matrix = np.array(source["matrix_world"])
cal_matrix = np.array(cal["sourceWorldToCanonicalUnityMatrixRows"])


def source_bounds(ids):
    vertices = data["vertices"][np.isin(labels, ids)].astype(float)
    blender = vertices @ source_matrix[:3, :3].T + source_matrix[:3, 3]
    unity = blender @ cal_matrix[:3, :3].T + cal_matrix[:3, 3]
    return np.min(unity, axis=0), np.max(unity, axis=0)


def voxel_bounds(path):
    document = json.loads((path / "geometry-manifest.json").read_text(encoding="utf-8"))
    mins, maxs = [], []
    for asset in document["assets"]:
        cells = np.load(path / asset["npz"])["position"].astype(float)
        rotation = np.array(asset["rotationMatrix"]).reshape(3, 3)
        center = np.array(asset["position"])
        cell_size = float(asset["cellSize"])
        world = (cells * cell_size) @ rotation.T + center
        # Each occupancy record covers one unit cube. Its rotated half extent
        # is the absolute rotation support, independent of the visual model.
        half = np.sum(np.abs(rotation), axis=1) * cell_size / 2
        mins.append(np.min(world, axis=0) - half)
        maxs.append(np.max(world, axis=0) + half)
    return np.min(mins, axis=0), np.max(maxs, axis=0), document


cases = [
    ("hex-deck", list(range(497, 570)) + list(range(593, 601)),
     Path("Temp/ReferenceMap20261101/GeometryHexDeck")),
    ("truss", [127], Path("Temp/ReferenceMap20261101/GeometryTruss")),
    ("left-angled-truss", [0], Path("Temp/ReferenceMap20261101/LeftAngledTruss")),
    ("right-truss-extension", [134], Path("Temp/ReferenceMap20261101/RightTrussExtension")),
    ("five-parallel-tanks", list(range(570, 575)), Path("Temp/ReferenceMap20261101/DeckTanksV2")),
    ("left-upper-equipment", list(range(2, 19)), Path("Temp/ReferenceMap20261101/LeftUpperEquipment")),
]
result = {}
for name, ids, path in cases:
    if not (path / "geometry-manifest.json").exists():
        continue
    smn, smx = source_bounds(ids)
    vmn, vmx, doc = voxel_bounds(path)
    result[name] = {
        "sourceShellIds": ids,
        "sourceCanonicalBounds": {"min": smn.tolist(), "max": smx.tolist()},
        "voxelCanonicalBounds": {"min": vmn.tolist(), "max": vmx.tolist()},
        "voxelMinusSourceBoundaryMetres": {
            "min": (vmn - smn).tolist(), "max": (vmx - smx).tolist()},
        "cellRecords": sum(a["cells"] for a in doc["assets"]),
        "assetCount": len(doc["assets"]),
        "maxAssetCells": max(a["cells"] for a in doc["assets"]),
        "initialConnectedComponents": [a["connectedComponents"] for a in doc["assets"]],
    }
    print(name, "source", np.round(smn, 3), np.round(smx, 3),
          "voxel", np.round(vmn, 3), np.round(vmx, 3),
          "boundaryDelta", np.round(vmn-smn, 3), np.round(vmx-smx, 3))
    src_overlay = path.parent / f"{name}-overlay.png"
    if src_overlay.exists():
        shutil.copyfile(src_overlay, root / f"source-qa-{name}-overlay.png")
    src_metrics = root / f"source-qa-{name}-overlay.json"
    if not src_metrics.exists():
        src_metrics = path.parent / f"{name}-overlay.json"
    if src_metrics.exists():
        result[name]["silhouette"] = json.loads(src_metrics.read_text(encoding="utf-8"))
output = root / "source-qa-samples.json"
output.write_text(json.dumps(result, indent=2), encoding="utf-8")
print("WROTE", output)
