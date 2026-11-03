"""Audit 3D replacements for the 3,065 planar field-card shells."""

import json
from collections import Counter
from pathlib import Path

import numpy as np


root = Path("Documentation/ReferenceMap-2026-11-01")
cards = json.loads(Path("Temp/ReferenceMap20261101/card-rock-clusters.json").read_text(encoding="utf-8"))
components = json.loads((root / "source-meshes/source-components.json").read_text(encoding="utf-8"))
field = next(o for o in components["objects"] if o["name"] == "Cube.001")
by_id = {c["id"]: c for c in field["components"]}
topology = json.loads((root / "source-meshes/source-topology.json").read_text(encoding="utf-8"))
field_topology = next(o for o in topology["objects"] if o["name"] == "Cube.001")
open_ids = {x["id"] for x in field_topology["components"] if not x["watertight"]}
cal = json.loads((root / "source-calibration.json").read_text(encoding="utf-8"))
matrix = np.array(cal["sourceWorldToCanonicalUnityMatrixRows"])
mesh_item = next(o for o in json.loads((root / "source-meshes/source-meshes.json").read_text(encoding="utf-8"))["objects"] if o["name"] == "Cube.001")
with np.load(root / "source-meshes" / mesh_item["npz"]) as mesh:
    local_vertices = mesh["vertices"].astype(np.float64)
    triangles = mesh["triangles"]
with np.load(root / "source-meshes" / (Path(mesh_item["npz"]).stem + "-components.npz")) as labels_file:
    triangle_labels = labels_file["triangle_component"]
world_matrix = np.array(mesh_item["matrix_world"])
world_vertices = local_vertices @ world_matrix[:3, :3].T + world_matrix[:3, 3]
vertices = world_vertices @ matrix[:3, :3].T + matrix[:3, 3]
shell_points = {i: vertices[np.unique(triangles[triangle_labels == i])] for i in open_ids}


def canonical_profile(ids):
    points = np.concatenate([shell_points[i] for i in ids])
    center = (points.min(0) + points.max(0)) * 0.5
    centered = points - center
    _, vectors = np.linalg.eigh(centered.T @ centered)
    major, minor = vectors[:, 2], vectors[:, 0]
    middle = np.cross(minor, major)
    middle /= np.linalg.norm(middle)
    minor = np.cross(major, middle)
    frame = np.stack((major, middle, minor), axis=1)
    span = np.sort(np.ptp(centered @ frame, axis=0))[::-1]
    return center, span


ids = []
center_errors = []
span_errors = []
records = []
for instance in cards["instances"]:
    shells = instance["source"]["sourceShells"]
    ids.extend(shells)
    center, span = canonical_profile(shells)
    actual = np.array(instance["position"])
    recorded_span = np.array(instance["source"]["sourceSpanMetres"])
    center_error = float(np.linalg.norm(actual - center))
    span_error = float(np.max(np.abs(recorded_span - span)))
    center_errors.append(center_error)
    span_errors.append(span_error)
    records.append({"sourceShells": shells, "assetKey": instance["assetKey"],
                    "centerErrorMetres": center_error,
                    "sourceSpanErrorMetres": span_error})
ids_counter = Counter(ids)
summary = {
    "cardShellCount": cards["cardShellCount"],
    "rockClusterCount": cards["rockClusterCount"],
    "actualInstanceCount": len(cards["instances"]),
    "allOpenCardShellsCoveredExactlyOnce": set(ids) == open_ids and all(v == 1 for v in ids_counter.values()),
    "closedFieldShellsIncludedByMistake": len(set(ids) - open_ids),
    "uniqueAssetKeys": len({x["assetKey"] for x in cards["instances"]}),
    "uniquePrototypeObjects": sorted({x["source"]["prototypeObject"] for x in cards["instances"]}),
    "clusterSizeHistogram": dict(Counter(len(x["source"]["sourceShells"]) for x in cards["instances"])),
    "maxCenterErrorMetres": max(center_errors),
    "p95CenterErrorMetres": float(np.percentile(center_errors, 95)),
    "maxSourceSpanErrorMetres": max(span_errors),
    "records": records,
}
output = root / "source-qa-card-substitutes.json"
output.write_text(json.dumps(summary, indent=2), encoding="utf-8")
print(json.dumps({k: v for k, v in summary.items() if k not in ("records", "uniquePrototypeObjects")}, indent=2))
print("uniquePrototypeObjects", len(summary["uniquePrototypeObjects"]))
