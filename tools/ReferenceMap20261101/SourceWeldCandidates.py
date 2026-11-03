"""Report station shell proximity as candidate physical welds, not automatic unions."""

import json
from collections import Counter, defaultdict
from pathlib import Path

import numpy as np
from scipy.spatial import cKDTree


folder = Path("Documentation/ReferenceMap-2026-11-01")
meshdir = folder / "source-meshes"
manifest = json.loads((meshdir / "source-meshes.json").read_text(encoding="utf-8"))
obj = next(x for x in manifest["objects"] if x["name"] == "Cylinder.018")
mesh = np.load(meshdir / obj["npz"])
vertices = mesh["vertices"].astype(np.float64)
labels = np.load(meshdir / obj["npz"].replace(".npz", "-components.npz"))["vertex_component"]
matrix = np.array(obj["matrix_world"])
world = vertices @ matrix[:3, :3].T + matrix[:3, 3]
tree = cKDTree(world)
scale = 186.78845424372102
results = {}
for distance_metres in (0.25, 0.5, 1.0, 2.0):
    radius = distance_metres / scale
    pairs = tree.query_pairs(r=radius, output_type="ndarray")
    a, b = labels[pairs[:, 0]], labels[pairs[:, 1]]
    cross = a != b
    cross_pairs = np.sort(np.column_stack((a[cross], b[cross])), axis=1)
    edges, counts = np.unique(cross_pairs, axis=0, return_counts=True)
    adjacency = [{"a": int(edge[0]), "b": int(edge[1]), "nearVertexPairs": int(count)}
                 for edge, count in zip(edges, counts)]
    adjacency.sort(key=lambda x: -x["nearVertexPairs"])
    parent = np.arange(int(labels.max()) + 1)

    def root(x):
        while x != parent[x]:
            parent[x] = parent[parent[x]]
            x = parent[x]
        return x

    for edge in edges:
        parent[root(edge[0])] = root(edge[1])
    groups = defaultdict(list)
    for shell in np.unique(labels):
        groups[root(shell)].append(int(shell))
    ordered = sorted(groups.values(), key=len, reverse=True)
    results[str(distance_metres)] = {
        "thresholdMetres": distance_metres,
        "crossShellNearVertexPairCount": int(cross.sum()),
        "candidateWeldEdgeCount": len(adjacency),
        "candidateGroups": ordered,
        "edges": adjacency,
        "note": "Vertex proximity is a candidate, not proof of common rigid-body intent; face intersections without close vertices may be missed."
    }
    print("SOURCE_WELDS", distance_metres, "m", len(adjacency), "edges", len(ordered),
          "groups", "largest", [len(x) for x in ordered[:10]],
          "truss neighbors", [(x["b"] if x["a"] == 127 else x["a"], x["nearVertexPairs"])
                              for x in adjacency if x["a"] == 127 or x["b"] == 127][:15])
output = folder / "source-weld-candidates.json"
output.write_text(json.dumps(results, separators=(",", ":")), encoding="utf-8")
print("WROTE", output)
