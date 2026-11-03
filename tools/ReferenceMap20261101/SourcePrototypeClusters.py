"""Find scale/rotation-invariant duplicate closed rock shells by edge spectrum."""

import hashlib
import json
from collections import defaultdict
from pathlib import Path

import numpy as np


folder = Path("Documentation/ReferenceMap-2026-11-01/source-meshes")
manifest = json.loads((folder / "source-meshes.json").read_text(encoding="utf-8"))
topology = json.loads((folder / "source-topology.json").read_text(encoding="utf-8"))
source_components = json.loads((folder / "source-components.json").read_text(encoding="utf-8"))
records = []
for obj in manifest["objects"]:
    if obj["name"] != "Cube.001" and "scatter" not in obj["collections"]:
        continue
    mesh = np.load(folder / obj["npz"])
    vertices, triangles = mesh["vertices"].astype(np.float64), mesh["triangles"]
    comp = next(x for x in source_components["objects"] if x["name"] == obj["name"])
    shell_lookup = {x["id"]: x for x in comp["components"]}
    labels = np.load(folder / comp["labels_npz"])["triangle_component"]
    topo = next(x for x in topology["objects"] if x["name"] == obj["name"])
    for shell in topo["components"]:
        if not shell["watertight"]:
            continue
        shell_id = shell["id"]
        tri = triangles[labels == shell_id]
        used = np.unique(tri)
        edges = np.sort(np.concatenate((tri[:, [0, 1]], tri[:, [1, 2]], tri[:, [2, 0]])), axis=1)
        edges = np.unique(edges, axis=0)
        lengths = np.linalg.norm(vertices[edges[:, 1]] - vertices[edges[:, 0]], axis=1)
        mid = np.median(lengths)
        if mid <= 0:
            continue
        signature = np.round(np.sort(lengths / mid), 3).astype(np.float32)
        digest = hashlib.sha1(np.array([len(used), len(tri)], dtype=np.int32).tobytes()
                              + signature.tobytes()).hexdigest()
        world_bounds = shell_lookup[shell_id]["world_bounds"]
        records.append({"object": obj["name"], "shell": shell_id,
                        "vertices": len(used), "triangles": len(tri),
                        "signature": digest, "world_bounds": world_bounds})
    print("SOURCE_PROTOTYPES", obj["name"], "closed", sum(x["object"] == obj["name"] for x in records))

groups = defaultdict(list)
for rec in records:
    groups[rec["signature"]].append({"object": rec["object"], "shell": rec["shell"]})
ranked = sorted(groups.items(), key=lambda pair: len(pair[1]), reverse=True)
print("FIELD closed shell count", sum(x["object"] == "Cube.001" for x in records))
print("FIELD unique signatures", len({x["signature"] for x in records if x["object"] == "Cube.001"}))
print("top signature groups", [(len(items), items[:3]) for _, items in ranked[:20]])
cross = [items for _, items in ranked if any(x["object"] == "Cube.001" for x in items)
         and any(x["object"] != "Cube.001" for x in items)]
print("field/scatter exact normalized edge matches", len(cross))
output = folder / "source-prototype-groups.json"
output.write_text(json.dumps({"method": "closed-shell sorted unique-edge lengths divided by median, rounded to 0.001; rotation/translation/uniform-scale invariant but not guaranteed unique", "records": records,
                              "duplicateGroups": [items for _, items in ranked if len(items) > 1]},
                             separators=(",", ":")), encoding="utf-8")
print("WROTE", output)
