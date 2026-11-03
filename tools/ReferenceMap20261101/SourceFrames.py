"""Derive local voxel frames for source truss and hex deck samples."""

import json
from pathlib import Path

import numpy as np
from scipy.spatial.transform import Rotation


folder = Path("Documentation/ReferenceMap-2026-11-01")
meshes = folder / "source-meshes"
manifest = json.loads((meshes / "source-meshes.json").read_text(encoding="utf-8"))
calibration = json.loads((folder / "source-calibration.json").read_text(encoding="utf-8"))
obj = next(x for x in manifest["objects"] if x["name"] == "Cylinder.018")
mesh = np.load(meshes / obj["npz"])
labels = np.load(meshes / obj["npz"].replace(".npz", "-components.npz"))["vertex_component"]
matrix = np.array(obj["matrix_world"])
world = mesh["vertices"].astype(float) @ matrix[:3, :3].T + matrix[:3, 3]
canonical = np.array(calibration["sourceWorldToCanonicalUnityMatrixRows"])
source_to_unity_rotation = canonical[:3, :3] / calibration["metresPerSourceUnit"]
groups = {"left_angled_truss": [0],
          "central_bridge_truss": [127],
          "right_truss_extension": [134],
          "hex_deck": list(range(497, 570)) + list(range(593, 601))}
records = {}
for name, ids in groups.items():
    points = world[np.isin(labels, ids)]
    origin = points.mean(axis=0)
    _, _, vh = np.linalg.svd(points - origin, full_matrices=False)
    if name == "hex_deck":
        up = vh[2]
        if up[2] < 0:
            up = -up
        x = vh[0]
        x -= up * np.dot(x, up)
        x /= np.linalg.norm(x)
    else:
        x = vh[0]
        if x[0] < 0:
            x = -x
        up = np.array([0.0, 0.0, 1.0])
        up -= x * np.dot(up, x)
        up /= np.linalg.norm(up)
    y = np.cross(up, x)
    y /= np.linalg.norm(y)
    x = np.cross(y, up)
    x /= np.linalg.norm(x)
    basis = np.column_stack([x, y, up])
    local = (points - origin) @ basis
    unity_rotation = source_to_unity_rotation @ basis
    unity_origin = canonical[:3, :3] @ origin + canonical[:3, 3]
    record = {
        "sourceShellIds": ids,
        "sourceWorldOrigin": origin.tolist(),
        "sourceLocalBasisColumns": basis.tolist(),
        "sourceLocalBounds": {"min": local.min(axis=0).tolist(),
                              "max": local.max(axis=0).tolist()},
        "canonicalUnityOriginMetres": unity_origin.tolist(),
        "canonicalUnityRotationRows": unity_rotation.tolist(),
        "canonicalUnityQuaternionXYZW": Rotation.from_matrix(unity_rotation).as_quat().tolist(),
        "note": "Mesh local points = (source world point - sourceWorldOrigin) @ sourceLocalBasisColumns; multiply local metres by calibration scale, then place with canonicalUnityRotationRows at canonicalUnityOriginMetres."
    }
    records[name] = record
    print(name, "source origin", origin, "local span", local.max(axis=0)-local.min(axis=0),
          "unity origin", unity_origin)
output = folder / "source-local-frames.json"
output.write_text(json.dumps(records, separators=(",", ":")), encoding="utf-8")
print("WROTE", output)
