"""Measure watertightness of each indexed source shell; never edits the blend."""

import json
from pathlib import Path

import numpy as np


folder = Path("Documentation/ReferenceMap-2026-11-01/source-meshes")
source = json.loads((folder / "source-components.json").read_text(encoding="utf-8"))
result = []
for record in source["objects"]:
    mesh = np.load(folder / record["npz"])
    vertices, triangles = mesh["vertices"], mesh["triangles"]
    labels = np.load(folder / record["labels_npz"])["triangle_component"]
    directed_edges = np.concatenate((triangles[:, [0, 1]], triangles[:, [1, 2]],
                                     triangles[:, [2, 0]]))
    edges = np.sort(directed_edges, axis=1)
    _, first, inverse, edge_counts = np.unique(edges, axis=0, return_index=True,
                                               return_inverse=True, return_counts=True)
    direction = np.where(directed_edges[:, 0] < directed_edges[:, 1], 1, -1)
    winding_sum = np.bincount(inverse, weights=direction, minlength=len(edge_counts))
    edge_labels = np.tile(labels, 3)[first]
    n = len(record["components"])
    boundary = np.bincount(edge_labels, weights=(edge_counts == 1).astype(np.int32), minlength=n)
    nonmanifold = np.bincount(edge_labels, weights=(edge_counts > 2).astype(np.int32), minlength=n)
    inconsistent = np.bincount(edge_labels, weights=((edge_counts == 2) & (winding_sum != 0)).astype(np.int32), minlength=n)
    comp_info = [{
        "id": int(item["id"]),
        "triangle_count": int(item["triangle_count"]),
        "boundary_edges": int(boundary[item["id"]]),
        "nonmanifold_edges": int(nonmanifold[item["id"]]),
        "orientation_inconsistent_edges": int(inconsistent[item["id"]]),
        "watertight": bool(boundary[item["id"]] == 0 and nonmanifold[item["id"]] == 0),
    } for item in record["components"]]
    result.append({"name": record["name"], "components": comp_info})
    print(record["name"], "shells", len(comp_info), "watertight", sum(x["watertight"] for x in comp_info),
          "open", sum(not x["watertight"] for x in comp_info))

(folder / "source-topology.json").write_text(
    json.dumps({"source": "source-components.json", "objects": result}, separators=(",", ":")),
    encoding="utf-8")
