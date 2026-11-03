"""Classify generated station overlap pairs against original source triangles."""

import json
from pathlib import Path

import numpy as np
from scipy.spatial import cKDTree


ROOT = Path("Documentation/ReferenceMap-2026-11-01")
ITEM = next(o for o in json.loads((ROOT / "source-meshes/source-meshes.json").read_text(encoding="utf-8"))["objects"] if o["name"] == "Cylinder.018")
CAL = np.asarray(json.loads((ROOT / "source-calibration.json").read_text(encoding="utf-8"))["sourceWorldToCanonicalUnityMatrixRows"])
with np.load(ROOT / "source-meshes" / ITEM["npz"]) as mesh:
    vertices = mesh["vertices"].astype(np.float64)
    triangles = mesh["triangles"]
with np.load(ROOT / "source-meshes" / (Path(ITEM["npz"]).stem + "-components.npz")) as mesh:
    labels = mesh["triangle_component"]
world = np.asarray(ITEM["matrix_world"])
vertices = (vertices @ world[:3, :3].T + world[:3, 3]) @ CAL[:3, :3].T + CAL[:3, 3]
cache = {}


def shell_ids(source):
    return tuple(sorted(source.get("sourceShells", source.get("sourceGroupShells", [source.get("sourceShell")] ))))


def geometry(ids):
    if ids not in cache:
        faces = vertices[triangles[np.isin(labels, ids)]]
        low, high = faces.reshape(-1, 3).min(0), faces.reshape(-1, 3).max(0)
        tri_low, tri_high = faces.min(axis=1), faces.max(axis=1)
        center = (tri_low + tri_high) * .5
        radius = np.linalg.norm((tri_high - tri_low) * .5, axis=1)
        cache[ids] = (faces, low, high, tri_low, tri_high, center, radius)
    return cache[ids]


def intersect_triangles(a, b, tolerance=0.002):
    ea, eb = np.roll(a, -1, axis=0) - a, np.roll(b, -1, axis=0) - b
    na, nb = np.cross(ea[0], ea[1]), np.cross(eb[0], eb[1])
    axes = [na, nb] + [np.cross(x, y) for x in ea for y in eb]
    axes += [np.cross(na, x) for x in ea] + [np.cross(nb, x) for x in eb]
    for axis in axes:
        norm = np.linalg.norm(axis)
        if norm < 1e-12:
            continue
        axis /= norm
        pa, pb = a @ axis, b @ axis
        if pa.max() < pb.min() - tolerance or pb.max() < pa.min() - tolerance:
            return False
    return True


def exact_triangle_hit(first, second):
    a, b = first, second
    if len(a[0]) > len(b[0]):
        a, b = b, a
    fa, _, _, al, ah, ac, ar = a
    fb, _, _, bl, bh, bc, br = b
    tree = cKDTree(bc)
    max_radius_b = float(br.max())
    tested = 0
    aabb_candidates = 0
    for i in range(len(fa)):
        candidates = tree.query_ball_point(ac[i], ar[i] + max_radius_b + 0.002)
        for j in candidates:
            if np.any(al[i] > bh[j] + 0.002) or np.any(bl[j] > ah[i] + 0.002):
                continue
            aabb_candidates += 1
            tested += 1
            if intersect_triangles(fa[i], fb[j]):
                return True, tested, aabb_candidates
    return False, tested, aabb_candidates


def inside_by_ray(points, surface):
    tri = surface
    ay, az = tri[:, 0, 1], tri[:, 0, 2]
    by, bz = tri[:, 1, 1], tri[:, 1, 2]
    cy, cz = tri[:, 2, 1], tri[:, 2, 2]
    det = (by - ay) * (cz - az) - (bz - az) * (cy - ay)
    ymin = np.minimum.reduce((ay, by, cy)); ymax = np.maximum.reduce((ay, by, cy))
    zmin = np.minimum.reduce((az, bz, cz)); zmax = np.maximum.reduce((az, bz, cz))
    found = 0
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
            found += 1
            if found >= 3:
                return found
    return found


pairs = json.loads(Path("Temp/ReferenceMap20261101/geometry-station-overlap-all.json").read_text(encoding="utf-8"))["exactPairs"]
results = []
for index, pair in enumerate(pairs):
    ids_a, ids_b = shell_ids(pair["firstSource"]), shell_ids(pair["secondSource"])
    first, second = geometry(ids_a), geometry(ids_b)
    gap = np.maximum(first[1] - second[2], second[1] - first[2])
    aabb_gap = float(max(0, gap.max()))
    result = {"assetPair": sorted([pair["firstAsset"], pair["secondAsset"]]),
              "firstShells": ids_a, "secondShells": ids_b,
              "voxelOverlappingCellPairs": pair["overlappingCellPairs"],
              "sourceAabbGapMetres": aabb_gap,
              "sourceTriangleIntersection": False,
              "sourceVertexContainedInClosedShell": False,
              "trianglePairsTested": 0}
    if aabb_gap > 0.002:
        result["status"] = "source_separated_aabb"
    else:
        hit, tested, candidates = exact_triangle_hit(first, second)
        result["trianglePairsTested"] = tested
        result["sourceTriangleIntersection"] = hit
        if hit:
            result["status"] = "source_triangle_intersection"
        else:
            # The two large source rounded masses are closed. Detect even when
            # another part is wholly inside one, with no intersecting surface.
            closed_a = ids_a in ((1,), (128,))
            closed_b = ids_b in ((1,), (128,))
            if closed_a or closed_b:
                surface, points = (first, second) if closed_a else (second, first)
                unique = np.unique(points[0].reshape(-1, 3), axis=0)
                if len(unique) > 500:
                    unique = unique[np.linspace(0, len(unique) - 1, 500, dtype=int)]
                count = inside_by_ray(unique, surface[0])
                result["sourceVertexContainedInClosedShell"] = count > 0
                result["containedSourceVerticesSampled"] = count
            result["status"] = "source_vertex_containment" if result["sourceVertexContainedInClosedShell"] else "no_source_contact_detected"
    results.append(result)
    if (index + 1) % 25 == 0:
        print(f"{index + 1}/{len(pairs)}", flush=True)

counts = {status: sum(r["status"] == status for r in results) for status in sorted({r["status"] for r in results})}
report = {"method": "Source evaluated triangles, canonical transform, exact AABB exclusion, triangle SAT with 2 mm tolerance, and source vertex containment for large closed domes.",
          "caveat": "No source contact detected is not proof of rigid-body separation: one non-dome watertight shell could fully contain another without triangle intersection. Only triangle intersection/containment rows establish geometric contact; source-separated AABB rows establish no contact.",
          "pairCount": len(results), "statusCounts": counts, "pairs": results}
(ROOT / "source-qa-station-contact-pairs.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
print(counts)
