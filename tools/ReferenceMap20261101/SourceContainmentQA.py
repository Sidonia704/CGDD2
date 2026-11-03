"""Resolve whole-shell containment among station pairs with no surface crossing."""

import json
from pathlib import Path

import numpy as np

ROOT = Path("Documentation/ReferenceMap-2026-11-01")
report_path = ROOT / "source-qa-station-contact-pairs.json"
report = json.loads(report_path.read_text(encoding="utf-8"))
topology = json.loads((ROOT / "source-meshes/source-topology.json").read_text(encoding="utf-8"))
station_topology = next(o for o in topology["objects"] if o["name"] == "Cylinder.018")
closed = {c["id"] for c in station_topology["components"] if c["watertight"]}
item = next(o for o in json.loads((ROOT / "source-meshes/source-meshes.json").read_text(encoding="utf-8"))["objects"] if o["name"] == "Cylinder.018")
transform = np.asarray(json.loads((ROOT / "source-calibration.json").read_text(encoding="utf-8"))["sourceWorldToCanonicalUnityMatrixRows"])
with np.load(ROOT / "source-meshes" / item["npz"]) as mesh:
    vertices = mesh["vertices"].astype(np.float64)
    triangles = mesh["triangles"]
with np.load(ROOT / "source-meshes" / (Path(item["npz"]).stem + "-components.npz")) as mesh:
    labels = mesh["triangle_component"]
world = np.asarray(item["matrix_world"])
vertices = (vertices @ world[:3, :3].T + world[:3, 3]) @ transform[:3, :3].T + transform[:3, 3]


def inside_any(points, surface):
    tri = surface
    ay, az = tri[:, 0, 1], tri[:, 0, 2]
    by, bz = tri[:, 1, 1], tri[:, 1, 2]
    cy, cz = tri[:, 2, 1], tri[:, 2, 2]
    det = (by - ay) * (cz - az) - (bz - az) * (cy - ay)
    ymin = np.minimum.reduce((ay, by, cy)); ymax = np.maximum.reduce((ay, by, cy))
    zmin = np.minimum.reduce((az, bz, cz)); zmax = np.maximum.reduce((az, bz, cz))
    for point in points:
        y, z = point[1] + .000137, point[2] + .000271
        ids = np.flatnonzero((np.abs(det) > 1e-10) & (ymin <= y) & (ymax >= y) & (zmin <= z) & (zmax >= z))
        if len(ids) == 0:
            continue
        u = ((y - ay[ids]) * (cz[ids] - az[ids]) - (z - az[ids]) * (cy[ids] - ay[ids])) / det[ids]
        v = ((by[ids] - ay[ids]) * (z - az[ids]) - (bz[ids] - az[ids]) * (y - ay[ids])) / det[ids]
        good = (u >= -1e-9) & (v >= -1e-9) & (u + v <= 1 + 1e-9)
        ids = ids[good]
        x = tri[ids, 0, 0] + u[good] * (tri[ids, 1, 0] - tri[ids, 0, 0]) + v[good] * (tri[ids, 2, 0] - tri[ids, 0, 0])
        if np.count_nonzero(x > point[0] + 1e-5) % 2:
            return True
    return False


shell_cache = {}
def shell_mesh(i):
    if i not in shell_cache:
        faces = vertices[triangles[labels == i]]
        points = np.unique(faces.reshape(-1, 3), axis=0)
        shell_cache[i] = (faces, points, points.min(0), points.max(0))
    return shell_cache[i]


for pair in report["pairs"]:
    if pair["status"] != "no_source_contact_detected":
        continue
    pair["containmentPairsChecked"] = 0
    for a in pair["firstShells"]:
        for b in pair["secondShells"]:
            mesh_a, points_a, min_a, max_a = shell_mesh(a)
            mesh_b, points_b, min_b, max_b = shell_mesh(b)
            if np.any(min_a > max_b) or np.any(min_b > max_a):
                continue
            pair["containmentPairsChecked"] += 1
            contained = (a in closed and inside_any(points_b, mesh_a)) or (b in closed and inside_any(points_a, mesh_b))
            if contained:
                pair["status"] = "source_vertex_containment"
                pair["sourceVertexContainedInClosedShell"] = True
                pair["containingShellPair"] = [a, b]
                break
        if pair["status"] == "source_vertex_containment":
            break

report["statusCounts"] = {status: sum(p["status"] == status for p in report["pairs"]) for status in sorted({p["status"] for p in report["pairs"]})}
report["method"] += " Remaining non-dome pairs checked for source vertex containment in every individual watertight shell whose exact AABBs overlap."
report_path.write_text(json.dumps(report, indent=2), encoding="utf-8")
print(report["statusCounts"])
