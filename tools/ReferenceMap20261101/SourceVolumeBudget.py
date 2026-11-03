"""Estimate unavoidable 1 m occupancy from closed source shell volumes."""

import json
from pathlib import Path

import numpy as np


folder = Path("Documentation/ReferenceMap-2026-11-01")
meshdir = folder / "source-meshes"
source = json.loads((meshdir / "source-meshes.json").read_text(encoding="utf-8"))
components = json.loads((meshdir / "source-components.json").read_text(encoding="utf-8"))
topology = json.loads((meshdir / "source-topology.json").read_text(encoding="utf-8"))
scale = 186.78845424372102
records = []
for obj in source["objects"]:
    if obj["name"] not in {"Cube.001", "Cylinder.018", "tyFlow_node_515.347", "tyFlow_node_515.521"}:
        continue
    mesh = np.load(meshdir / obj["npz"])
    local_vertices, triangles = mesh["vertices"].astype(float), mesh["triangles"]
    matrix = np.array(obj["matrix_world"])
    world_vertices = local_vertices @ matrix[:3, :3].T + matrix[:3, 3]
    comps = next(x for x in components["objects"] if x["name"] == obj["name"])
    labels = np.load(meshdir / comps["labels_npz"])["triangle_component"]
    topo = next(x for x in topology["objects"] if x["name"] == obj["name"])
    closed = {x["id"] for x in topo["components"] if x["watertight"]}
    by_id = []
    for comp in comps["components"]:
        shell_id = comp["id"]
        if shell_id not in closed:
            continue
        tri = triangles[labels == shell_id]
        verts = world_vertices[tri]
        origin = verts.mean(axis=(0, 1))
        e1 = verts[:, 1] - verts[:, 0]
        e2 = verts[:, 2] - verts[:, 0]
        area = np.linalg.norm(np.cross(e1, e2), axis=1).sum() / 2
        face = verts[:, 0] - origin
        signed_volume = np.einsum("ij,ij->i", face, np.cross(e1, e2)).sum() / 6
        by_id.append({"id": shell_id, "volumeSourceUnits3": float(abs(signed_volume)),
                      "volumeMetres3": float(abs(signed_volume) * scale ** 3),
                      "surfaceAreaMetres2": float(area * scale ** 2)})
    volumes = np.array([x["volumeMetres3"] for x in by_id])
    stats = {"name": obj["name"], "closedShells": len(by_id),
             "sumClosedShellVolumeMetres3": float(volumes.sum()),
             "medianShellVolumeMetres3": float(np.median(volumes)),
             "p90ShellVolumeMetres3": float(np.percentile(volumes, 90)),
             "maxShellVolumeMetres3": float(volumes.max()),
             "shells": by_id,
             "note": "Sum assumes separate shells; overlapping station/debris shells can double count. Field rocks are mostly disjoint. Volume is a lower bound on 1m occupancy record count before boundary dilation and open-card substitutes."}
    records.append(stats)
    print(obj["name"], "closed", len(by_id), "sum m3", round(volumes.sum()),
          "median", round(np.median(volumes)), "p90", round(np.percentile(volumes, 90)),
          "max", round(volumes.max()))
output = folder / "source-volume-budget.json"
output.write_text(json.dumps(records, separators=(",", ":")), encoding="utf-8")
print("WROTE", output)
