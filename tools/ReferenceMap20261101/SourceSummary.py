"""Summarize source shell distribution and 3500 m calibration candidates."""

import collections
import json
from pathlib import Path

import numpy as np


folder = Path("Documentation/ReferenceMap-2026-11-01/source-meshes")
manifest = json.loads((folder / "source-meshes.json").read_text(encoding="utf-8"))
components = json.loads((folder / "source-components.json").read_text(encoding="utf-8"))
topology = json.loads((folder / "source-topology.json").read_text(encoding="utf-8"))
for name in ("Cube.001", "Cylinder.018", "tyFlow_node_515.347", "tyFlow_node_515.521"):
    obj = next(x for x in components["objects"] if x["name"] == name)
    items = obj["components"]
    counts = collections.Counter(x["triangle_count"] for x in items)
    sizes = np.array([np.subtract(x["world_bounds"]["max"], x["world_bounds"]["min"])
                      for x in items])
    print(name, "shells", len(items), "tri-hist", counts.most_common(15))
    print("  bounds sizes percentiles 0/25/50/75/90/99/100:")
    print(" ", np.round(np.percentile(sizes, [0, 25, 50, 75, 90, 99, 100], axis=0), 5).tolist())
    topo = next(x for x in topology["objects"] if x["name"] == name)
    for kind in ("closed", "open"):
        selected = [items[i] for i, t in enumerate(topo["components"])
                    if t["watertight"] == (kind == "closed")]
        if selected:
            dims = np.array([np.subtract(c["world_bounds"]["max"], c["world_bounds"]["min"])
                             for c in selected])
            print(" ", kind, "count", len(selected), "max-axis p50/p90/p99",
                  np.round(np.percentile(np.max(dims, axis=1), [50, 90, 99]), 5).tolist(),
                  "min-axis p50", round(float(np.median(np.min(dims, axis=1))), 7))

for obj in manifest["objects"]:
    if obj["name"].startswith("tyFlow_node_515.3") or obj["name"] == "asteroids__0.1038":
        b = obj["world_bounds_exact"]
        size = np.subtract(b["max"], b["min"])
        print(obj["name"], "bounds", b, "size", size.tolist())

groups = {
    "core_volume": {"Cube.001", "Cylinder.018", "tyFlow_node_515.347", "tyFlow_node_515.521"},
    "volume_plus_micro_debris": {"Cube.001", "Cylinder.018", "tyFlow_node_515.347",
                                 "tyFlow_node_515.376", "tyFlow_node_515.383",
                                 "tyFlow_node_515.386", "tyFlow_node_515.521"},
}
for label, names in groups.items():
    selected = [o for o in manifest["objects"] if o["name"] in names]
    mn = np.min([o["world_bounds_exact"]["min"] for o in selected], axis=0)
    mx = np.max([o["world_bounds_exact"]["max"] for o in selected], axis=0)
    span = mx - mn
    print(label, "min", mn.tolist(), "max", mx.tolist(), "extent", span.tolist(),
          "scale3500", 3500 / np.max(span), "center", ((mn + mx) / 2).tolist())
