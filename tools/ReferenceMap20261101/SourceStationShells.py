"""Write a compact station shell index for semantic sampling decisions."""

import csv
import json
from pathlib import Path

import numpy as np


folder = Path("Documentation/ReferenceMap-2026-11-01/source-meshes")
obj = next(x for x in json.loads((folder / "source-components.json").read_text(encoding="utf-8"))["objects"]
           if x["name"] == "Cylinder.018")
top = next(x for x in json.loads((folder / "source-topology.json").read_text(encoding="utf-8"))["objects"]
           if x["name"] == "Cylinder.018")
rows = []
for c, t in zip(obj["components"], top["components"]):
    mn, mx = np.array(c["world_bounds"]["min"]), np.array(c["world_bounds"]["max"])
    size = mx - mn
    rows.append({
        "id": c["id"], "triangles": c["triangle_count"], "watertight": t["watertight"],
        "cx": c["world_vertex_center"][0], "cy": c["world_vertex_center"][1],
        "cz": c["world_vertex_center"][2],
        "sx": size[0], "sy": size[1], "sz": size[2],
        "maxsize": np.max(size),
    })
output = folder.parent / "source-station-shells.csv"
with output.open("w", newline="", encoding="utf-8") as f:
    writer = csv.DictWriter(f, fieldnames=list(rows[0]))
    writer.writeheader()
    writer.writerows(rows)
for r in sorted(rows, key=lambda x: x["maxsize"], reverse=True)[:50]:
    print("id={id:3d} t={triangles:4d} closed={watertight} center=({cx:+.3f},{cy:+.3f},{cz:+.3f}) size=({sx:.3f},{sy:.3f},{sz:.3f})".format(**r))
print("WROTE", output)
