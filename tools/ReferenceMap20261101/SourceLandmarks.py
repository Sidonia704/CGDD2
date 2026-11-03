"""Export source-coordinate landmarks for station silhouette and layout QA."""

import json
from pathlib import Path

import numpy as np


folder = Path("Documentation/ReferenceMap-2026-11-01")
meshdir = folder / "source-meshes"
manifest = json.loads((meshdir / "source-meshes.json").read_text(encoding="utf-8"))
calibration = json.loads((folder / "source-calibration.json").read_text(encoding="utf-8"))
matrix = np.array(calibration["sourceWorldToCanonicalUnityMatrixRows"])
landmarks = {
    "left_rounded_mass": ("Cylinder.018", [1]),
    "right_rounded_mass": ("Cylinder.018", [128]),
    "left_angled_truss": ("Cylinder.018", [0]),
    "central_bridge_truss": ("Cylinder.018", [127]),
    "right_truss_extension": ("Cylinder.018", [134]),
    "hex_deck": ("Cylinder.018", list(range(497, 570)) + list(range(593, 601))),
    "left_upper_equipment": ("Cylinder.018", list(range(2, 19))),
    "five_parallel_tanks": ("Cylinder.018", list(range(570, 575))),
    "left_radiator_bank": ("Cylinder.018", list(range(200, 214)) + list(range(243, 271))),
    "right_radiator_bank": ("Cylinder.018", list(range(214, 243)) + list(range(271, 285))),
    "placed_field_all_geometry": ("Cube.001", None),
    "wide_debris": ("tyFlow_node_515.347", None),
    "local_debris": ("tyFlow_node_515.521", None),
}
data_cache = {}
result = {}
for label, (name, ids) in landmarks.items():
    if name not in data_cache:
        obj = next(o for o in manifest["objects"] if o["name"] == name)
        mesh = np.load(meshdir / obj["npz"])
        local = mesh["vertices"].astype(float)
        object_matrix = np.array(obj["matrix_world"])
        world = local @ object_matrix[:3, :3].T + object_matrix[:3, 3]
        components = np.load(meshdir / obj["npz"].replace(".npz", "-components.npz"))
        data_cache[name] = (world, components["vertex_component"])
    world, component_labels = data_cache[name]
    selected = world if ids is None else world[np.isin(component_labels, ids)]
    unity = selected @ matrix[:3, :3].T + matrix[:3, 3]
    result[label] = {
        "sourceObject": name,
        "sourceShellIds": ids,
        "sourceWorldBounds": {"min": selected.min(axis=0).tolist(),
                              "max": selected.max(axis=0).tolist()},
        "canonicalUnityBoundsMetres": {"min": unity.min(axis=0).tolist(),
                                       "max": unity.max(axis=0).tolist()},
        "canonicalUnityVertexMeanMetres": unity.mean(axis=0).tolist(),
    }
    print(label, "unity center", np.round(unity.mean(axis=0), 1),
          "span", np.round(unity.max(axis=0)-unity.min(axis=0), 1))
output = folder / "source-landmarks.json"
output.write_text(json.dumps(result, indent=2), encoding="utf-8")
print("WROTE", output)
