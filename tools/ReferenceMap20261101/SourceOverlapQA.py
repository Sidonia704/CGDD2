"""Check coarse station overlap pairs against the original Blender source triangles.

This is a read-only source geometry diagnostic. It classifies sampled source
vertices inside each watertight dome with a projected-triangle parity ray and
computes their Euclidean distance to the exact source surface.
"""

import json
from pathlib import Path

import numpy as np


ROOT = Path("Documentation/ReferenceMap-2026-11-01")
source = json.loads((ROOT / "source-meshes/source-meshes.json").read_text(encoding="utf-8"))
station = next(o for o in source["objects"] if o["name"] == "Cylinder.018")
cal = json.loads((ROOT / "source-calibration.json").read_text(encoding="utf-8"))
transform = np.asarray(cal["sourceWorldToCanonicalUnityMatrixRows"])
with np.load(ROOT / "source-meshes" / station["npz"]) as mesh:
    vertices = mesh["vertices"].astype(np.float64)
    triangles = mesh["triangles"]
with np.load(ROOT / "source-meshes" / (Path(station["npz"]).stem + "-components.npz")) as mesh:
    labels = mesh["triangle_component"]
world = np.asarray(station["matrix_world"])
vertices = vertices @ world[:3, :3].T + world[:3, 3]
vertices = vertices @ transform[:3, :3].T + transform[:3, 3]


def points_for(shells):
    return vertices[np.unique(triangles[np.isin(labels, shells)])]


def inside_by_ray(points, dome_tris):
    # Perturb each yz ray so source vertices exactly on polygon edges are not
    # classified by floating-point boundary coincidence.
    tri = vertices[dome_tris]
    ay, az = tri[:, 0, 1], tri[:, 0, 2]
    by, bz = tri[:, 1, 1], tri[:, 1, 2]
    cy, cz = tri[:, 2, 1], tri[:, 2, 2]
    det = (by - ay) * (cz - az) - (bz - az) * (cy - ay)
    valid = np.abs(det) > 1e-10
    result = np.zeros(len(points), dtype=bool)
    for i, point in enumerate(points):
        y, z = point[1] + 0.000137, point[2] + 0.000271
        candidates = valid & (np.minimum.reduce([ay, by, cy]) <= y) & (np.maximum.reduce([ay, by, cy]) >= y)
        candidates &= (np.minimum.reduce([az, bz, cz]) <= z) & (np.maximum.reduce([az, bz, cz]) >= z)
        ids = np.flatnonzero(candidates)
        if len(ids) == 0:
            continue
        w1 = ((y - ay[ids]) * (cz[ids] - az[ids]) - (z - az[ids]) * (cy[ids] - ay[ids])) / det[ids]
        w2 = ((by[ids] - ay[ids]) * (z - az[ids]) - (bz[ids] - az[ids]) * (y - ay[ids])) / det[ids]
        inside_tri = (w1 >= -1e-9) & (w2 >= -1e-9) & (w1 + w2 <= 1 + 1e-9)
        ids = ids[inside_tri]
        x_intersection = tri[ids, 0, 0] + w1[inside_tri] * (tri[ids, 1, 0] - tri[ids, 0, 0]) + w2[inside_tri] * (tri[ids, 2, 0] - tri[ids, 0, 0])
        result[i] = (np.count_nonzero(x_intersection > point[0] + 1e-5) % 2) == 1
    return result


def distance_to_surface(point, tri):
    # Exact point-to-triangle minimum: orthogonal projection inside a triangle,
    # or the closest point on any of its three edges.
    a, b, c = tri[:, 0], tri[:, 1], tri[:, 2]
    ab, ac = b - a, c - a
    normal = np.cross(ab, ac)
    normal_sq = np.einsum("ij,ij->i", normal, normal)
    signed = np.einsum("ij,ij->i", point - a, normal)
    projected = point - (signed / np.maximum(normal_sq, 1e-20))[:, None] * normal
    v = projected - a
    d00 = np.einsum("ij,ij->i", ab, ab)
    d01 = np.einsum("ij,ij->i", ab, ac)
    d11 = np.einsum("ij,ij->i", ac, ac)
    d20 = np.einsum("ij,ij->i", v, ab)
    d21 = np.einsum("ij,ij->i", v, ac)
    denominator = d00 * d11 - d01 * d01
    bary1 = (d11 * d20 - d01 * d21) / np.maximum(denominator, 1e-20)
    bary2 = (d00 * d21 - d01 * d20) / np.maximum(denominator, 1e-20)
    on_face = (bary1 >= 0) & (bary2 >= 0) & (bary1 + bary2 <= 1)
    face_dist_sq = np.where(on_face, signed * signed / np.maximum(normal_sq, 1e-20), np.inf)
    for start, end in ((a, b), (b, c), (c, a)):
        edge = end - start
        t = np.clip(np.einsum("ij,ij->i", point - start, edge) / np.maximum(np.einsum("ij,ij->i", edge, edge), 1e-20), 0, 1)
        delta = point - (start + t[:, None] * edge)
        face_dist_sq = np.minimum(face_dist_sq, np.einsum("ij,ij->i", delta, delta))
    return float(np.sqrt(np.min(face_dist_sq)))


overlap = json.loads(Path("Temp/ReferenceMap20261101/geometry-station-overlap.json").read_text(encoding="utf-8"))
results = []
for dome in overlap["domes"]:
    dome_id = dome["domeSourceShell"]
    dome_tris = triangles[labels == dome_id]
    dome_surface = vertices[dome_tris]
    for contact in dome["contacts"][:6]:
        source_ids = contact["otherSource"]
        shells = source_ids.get("sourceGroupShells", source_ids.get("sourceShells", [source_ids.get("sourceShell")]))
        sample = points_for(shells)
        if len(sample) > 3000:
            sample = sample[np.linspace(0, len(sample) - 1, 3000, dtype=int)]
        inside = inside_by_ray(sample, dome_tris)
        inside_points = sample[inside]
        if len(inside_points) > 120:
            inside_points = inside_points[np.linspace(0, len(inside_points) - 1, 120, dtype=int)]
        depths = [distance_to_surface(p, dome_surface) for p in inside_points]
        results.append({"domeShell": dome_id, "otherAsset": contact["otherAsset"], "otherSourceShells": shells,
                        "sampledSourceVertices": len(sample), "verticesInsideExactSourceDome": int(np.sum(inside)),
                        "insideFraction": float(np.mean(inside)) if len(sample) else 0,
                        "sampledInsideVerticesForDepth": len(depths),
                        "maxDepthInsideDomeMetres": max(depths, default=0),
                        "p95DepthInsideDomeMetres": float(np.percentile(depths, 95)) if depths else 0})

report = {"method": "Original Blender evaluated triangles; canonical transform; sampled source vertex ray parity plus exact Euclidean point-to-triangle depth. Top six voxel-overlap pairs per dome.", "pairs": results}
(ROOT / "source-qa-station-overlap.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
for row in results:
    print(row["domeShell"], row["otherAsset"], row["verticesInsideExactSourceDome"], "/", row["sampledSourceVertices"], "deepest", round(row["maxDepthInsideDomeMetres"], 3))
