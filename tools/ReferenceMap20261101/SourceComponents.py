"""Analyze connected triangle shells in the read-only Blender NPZ export.

Run with Python + numpy + scipy: python SourceComponents.py SOURCE_MESHES_DIR
Writes component labels and bounds beside the source mesh manifest.
"""

import json
import sys
from pathlib import Path

import numpy as np
from scipy.sparse import coo_matrix
from scipy.sparse.csgraph import connected_components


folder = Path(sys.argv[1])
manifest = json.loads((folder / "source-meshes.json").read_text(encoding="utf-8"))
all_records = []
for record in manifest["objects"]:
    mesh = np.load(folder / record["npz"])
    vertices, triangles = mesh["vertices"], mesh["triangles"]
    if len(triangles):
        edges = np.concatenate((triangles[:, [0, 1]], triangles[:, [1, 2]], triangles[:, [2, 0]]))
        edges = np.concatenate((edges, edges[:, ::-1]))
        graph = coo_matrix((np.ones(len(edges), dtype=np.uint8),
                            (edges[:, 0], edges[:, 1])), shape=(len(vertices), len(vertices))).tocsr()
        ncomp, labels = connected_components(graph, directed=False)
        tri_labels = labels[triangles[:, 0]]
    else:
        ncomp = len(vertices)
        labels = np.arange(ncomp, dtype=np.int32)
        tri_labels = np.empty(0, dtype=np.int32)
    world_matrix = np.asarray(record["matrix_world"], dtype=np.float64)
    world = vertices.astype(np.float64) @ world_matrix[:3, :3].T + world_matrix[:3, 3]
    v_count = np.bincount(labels, minlength=ncomp)
    t_count = np.bincount(tri_labels, minlength=ncomp)
    mins = np.full((ncomp, 3), np.inf)
    maxs = np.full((ncomp, 3), -np.inf)
    np.minimum.at(mins, labels, world)
    np.maximum.at(maxs, labels, world)
    sums = np.zeros((ncomp, 3), dtype=np.float64)
    np.add.at(sums, labels, world)
    centers = sums / np.maximum(1, v_count)[:, None]
    components = [{
        "id": i,
        "vertex_count": int(v_count[i]),
        "triangle_count": int(t_count[i]),
        "world_bounds": {"min": mins[i].tolist(), "max": maxs[i].tolist()},
        "world_vertex_center": centers[i].tolist(),
    } for i in range(ncomp) if t_count[i] > 0]
    labels_file = record["npz"].removesuffix(".npz") + "-components.npz"
    np.savez_compressed(folder / labels_file, vertex_component=labels,
                        triangle_component=tri_labels)
    all_records.append({
        "name": record["name"],
        "npz": record["npz"],
        "labels_npz": labels_file,
        "component_count": len(components),
        "components": components,
    })
    print(f"SOURCE_COMPONENTS: {record['name']}: {len(components)} shells, max {int(t_count.max()) if len(t_count) else 0} triangles")

output = folder / "source-components.json"
output.write_text(json.dumps({"source": "source-meshes.json", "objects": all_records},
                             separators=(",", ":")), encoding="utf-8")
print(f"SOURCE_COMPONENTS_DONE: {output}")
