"""Read-only conservative physics-cell overlap audit around the two solid domes.

It reports overlapping occupied cell boxes and separates near-surface
quantization contacts from cells embedded deeper than one coarse dome cell.
It does not move, shrink, merge or delete any authored geometry.
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path

import numpy as np
from scipy.spatial import cKDTree

from Geometry import Limits, calibrated, object_arrays, raster_mesh, unity_basis, world_vertices


def instance_bounds(asset: dict, instance: dict) -> tuple[np.ndarray, np.ndarray]:
    lo, hi = [np.asarray(v, dtype=np.float64) for v in asset["boundsCells"]]
    corners = np.asarray([(x, y, z) for x in (lo[0] - .5, hi[0] + .5)
                                  for y in (lo[1] - .5, hi[1] + .5)
                                  for z in (lo[2] - .5, hi[2] + .5)])
    matrix = np.asarray(instance["rotationMatrix"]).reshape(3, 3)
    world = corners * asset["cellSize"] * instance["scale"] @ matrix.T + instance["position"]
    return world.min(axis=0), world.max(axis=0)


def cells_world(project: Path, asset: dict, instance: dict) -> tuple[np.ndarray, np.ndarray]:
    rows = json.loads((project / asset["cellRowsPath"]).read_text(encoding="utf-8"))["cells"]
    local = np.asarray([row[:3] for row in rows], dtype=np.float64)
    matrix = np.asarray(instance["rotationMatrix"]).reshape(3, 3)
    cell_size = asset["cellSize"] * instance["scale"]
    centers = local * cell_size @ matrix.T + instance["position"]
    half_extents = np.abs(matrix).sum(axis=1) * cell_size * .5
    return centers, half_extents


def source_surface(source_manifest: Path, source_calibration: Path, shell_id: int) -> np.ndarray:
    source = json.loads(source_manifest.read_text(encoding="utf-8"))
    item = next(obj for obj in source["objects"] if obj["name"] == "Cylinder.018")
    vertices, triangles = object_arrays(item, source_manifest.parent)
    cal = json.loads(source_calibration.read_text(encoding="utf-8"))
    matrix = np.asarray(cal["sourceWorldToCanonicalUnityMatrixRows"], dtype=np.float64)
    geometry_cal = {"scale": float(cal["metresPerSourceUnit"]),
                    "source_origin": (-matrix[:3, 3] / float(cal["metresPerSourceUnit"])).tolist(),
                    "cell_size_m": 1}
    vertices = calibrated(unity_basis(world_vertices(vertices, item)), geometry_cal)
    labels_file = source_manifest.parent / (Path(item["npz"]).stem + "-components.npz")
    with np.load(labels_file) as data:
        labels = data["triangle_component"]
    tri = triangles[labels == shell_id]
    unique, inverse = np.unique(tri, return_inverse=True)
    return raster_mesh(vertices[unique], inverse.reshape(-1, 3),
                       Limits(max_triangle_tests=60_000_000, max_object_cells=1_000_000), fill=False)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("catalog", type=Path)
    parser.add_argument("source_manifest", type=Path)
    parser.add_argument("source_calibration", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--all-pairs", action="store_true", help="Also test every station AABB pair's occupied cell boxes")
    args = parser.parse_args()
    catalog = json.loads(args.catalog.read_text(encoding="utf-8"))
    project = Path(__file__).resolve().parents[2]
    assets = {asset["assetKey"]: asset for asset in catalog["assets"]}
    station = [instance for instance in catalog["instances"] if instance["source"]["object"] == "Cylinder.018"]
    bounds = {instance["id"]: instance_bounds(assets[instance["assetKey"]], instance) for instance in station}
    broadphase_pairs = []
    for i, first in enumerate(station):
        first_lo, first_hi = bounds[first["id"]]
        for other in station[i + 1:]:
            other_lo, other_hi = bounds[other["id"]]
            if np.all(first_lo < other_hi) and np.all(other_lo < first_hi):
                broadphase_pairs.append((first, other))
    exact_pairs = []
    if args.all_pairs:
        cache = {}
        for first, other in broadphase_pairs:
            first_asset = assets[first["assetKey"]]
            other_asset = assets[other["assetKey"]]
            if first["id"] not in cache:
                centers, half = cells_world(project, first_asset, first)
                cache[first["id"]] = (centers, half, cKDTree(centers))
            if other["id"] not in cache:
                centers, half = cells_world(project, other_asset, other)
                cache[other["id"]] = (centers, half, cKDTree(centers))
            a_cells, a_half, a_tree = cache[first["id"]]
            b_cells, b_half, b_tree = cache[other["id"]]
            if len(a_cells) > len(b_cells):
                a_cells, b_cells = b_cells, a_cells
                a_half, b_half = b_half, a_half
                a_tree, b_tree = b_tree, a_tree
            radius = float(np.linalg.norm(a_half + b_half))
            affected_a = 0
            pair_count = 0
            for start in range(0, len(a_cells), 5000):
                chunk = a_cells[start:start + 5000]
                possible = b_tree.query_ball_point(chunk, radius)
                for index, neighbors in enumerate(possible):
                    hit = False
                    for j in neighbors:
                        if np.all(a_half + b_half - np.abs(chunk[index] - b_cells[j]) > 1e-7):
                            pair_count += 1
                            hit = True
                    affected_a += int(hit)
            if pair_count:
                exact_pairs.append({"firstAsset": first["assetKey"], "secondAsset": other["assetKey"],
                                    "firstSource": first["source"], "secondSource": other["source"],
                                    "overlappingCellPairs": pair_count, "affectedSmallerAssetCells": affected_a})
    reports = []
    for dome_shell in (1, 128):
        dome = next(instance for instance in station if instance["source"].get("sourceShell") == dome_shell)
        dome_asset = assets[dome["assetKey"]]
        dome_cells, dome_half = cells_world(project, dome_asset, dome)
        dome_tree = cKDTree(dome_cells)
        source_tree = cKDTree(source_surface(args.source_manifest, args.source_calibration, dome_shell))
        dome_lo, dome_hi = bounds[dome["id"]]
        contacts = []
        for other in station:
            if other["id"] == dome["id"]:
                continue
            other_lo, other_hi = bounds[other["id"]]
            if np.any(other_lo >= dome_hi) or np.any(dome_lo >= other_hi):
                continue
            asset = assets[other["assetKey"]]
            other_cells, other_half = cells_world(project, asset, other)
            radius = float(np.linalg.norm(dome_half + other_half))
            affected = set()
            dome_affected = set()
            pair_count = 0
            max_depth = 0.0
            for start in range(0, len(other_cells), 5000):
                chunk = other_cells[start:start + 5000]
                candidates = dome_tree.query_ball_point(chunk, radius)
                for local_index, possible in enumerate(candidates):
                    center = chunk[local_index]
                    for dome_index in possible:
                        depth_axes = dome_half + other_half - np.abs(center - dome_cells[dome_index])
                        if np.all(depth_axes > 1e-7):
                            pair_count += 1
                            affected.add(start + local_index)
                            dome_affected.add(dome_index)
                            max_depth = max(max_depth, float(np.min(depth_axes)))
            if not affected:
                continue
            affected_points = other_cells[np.asarray(sorted(affected), dtype=np.int32)]
            source_distances, _ = source_tree.query(affected_points, workers=-1)
            coarse_boundary = dome_asset["cellSize"] * .8660254
            contacts.append({"otherAsset": other["assetKey"], "otherSource": other["source"],
                             "otherCells": len(other_cells), "overlappingCellPairs": pair_count,
                             "overlappingOtherCells": len(affected), "overlappingDomeCells": len(dome_affected),
                             "maxCellBoxPenetrationMetres": round(max_depth, 3),
                             "nearestExactSourceSurfaceDistanceP95Metres": round(float(np.percentile(source_distances, 95)), 3),
                             "withinOneCoarseCellOfSourceSurface": int(np.sum(source_distances <= coarse_boundary)),
                             "deeperThanOneCoarseCell": int(np.sum(source_distances > coarse_boundary))})
        reports.append({"domeSourceShell": dome_shell, "domeAsset": dome["assetKey"],
                        "domeCellSize": dome_asset["cellSize"], "domeCells": len(dome_cells),
                        "contactAssets": len(contacts), "contacts": sorted(contacts, key=lambda x: -x["overlappingOtherCells"])})
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps({"description": "Conservative full-cell AABB overlap; includes intended embedded equipment and possible coarse quantization bleed",
                                       "stationInstanceCount": len(station), "stationBroadphasePairs": len(broadphase_pairs),
                                       "stationExactOverlapPairs": len(exact_pairs) if args.all_pairs else None,
                                       "exactPairs": sorted(exact_pairs, key=lambda x: -x["overlappingCellPairs"]),
                                       "domes": reports}, indent=2), encoding="utf-8")
    for report in reports:
        print(f"dome {report['domeSourceShell']}: {report['contactAssets']} other station assets overlap; top " +
              str([(c["otherAsset"], c["overlappingOtherCells"], c["deeperThanOneCoarseCell"])
                   for c in report["contacts"][:8]]))


if __name__ == "__main__":
    main()
