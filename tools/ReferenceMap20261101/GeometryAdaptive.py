"""Exact source-rock solids with fixed per-asset 1/2/4/5 metre voxel sizes.

Each watertight shell is tried at the finest allowed cell size. A candidate is
accepted only when its fully filled, six-connected occupancy is at most the
100,000-cell working budget and below Unity's 131,072-cell hard limit. This
does not change size by camera distance or move any source object.
"""

from __future__ import annotations

import argparse
from collections import Counter
import json
from pathlib import Path
import sys

import numpy as np

from Geometry import (Limits, calibrated, connected_components, export_asset,
                      hull_lookup, mesh_watertight, object_arrays, raster_mesh,
                      unity_basis, world_vertices)


def signed_volume(vertices: np.ndarray, triangles: np.ndarray) -> float:
    shifted = vertices - vertices.mean(axis=0)
    a, b, c = (shifted[triangles[:, index]] for index in range(3))
    return float(abs(np.einsum("ij,ij->", a, np.cross(b, c))) / 6)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source_manifest", type=Path)
    parser.add_argument("source_topology", type=Path)
    parser.add_argument("source_calibration", type=Path)
    parser.add_argument("hull_templates", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--object", default="Cube.001")
    parser.add_argument("--max-cells", type=int, default=100000)
    parser.add_argument("--cell-sizes", nargs="+", type=float, default=[1, 2, 4, 5])
    parser.add_argument("--size-bands", nargs=3, type=float, default=[12, 25, 50],
                        help="Maximum source rock axis in metres for 1, 2, and 4m; larger rocks start at 5m")
    parser.add_argument("--asset-prefix", default="field")
    parser.add_argument("--only-shell-ids", nargs="*", type=int)
    args = parser.parse_args()
    source = json.loads(args.source_manifest.read_text(encoding="utf-8"))
    topo = json.loads(args.source_topology.read_text(encoding="utf-8"))
    cal = json.loads(args.source_calibration.read_text(encoding="utf-8"))
    item = next(obj for obj in source["objects"] if obj["name"] == args.object)
    topo_item = next(obj for obj in topo["objects"] if obj["name"] == args.object)
    closed = {int(c["id"]) for c in topo_item["components"] if c["watertight"]}
    if args.only_shell_ids is not None:
        requested = set(args.only_shell_ids)
        missing = requested - closed
        if missing:
            raise ValueError(f"Requested shells are not watertight: {sorted(missing)}")
        closed &= requested
    vertices, triangles = object_arrays(item, args.source_manifest.parent)
    vertices = unity_basis(world_vertices(vertices, item))
    matrix = np.asarray(cal["sourceWorldToCanonicalUnityMatrixRows"], dtype=np.float64)
    basis_origin = -matrix[:3, 3] / float(cal["metresPerSourceUnit"])
    calibration = {"scale": float(cal["metresPerSourceUnit"]),
                   "source_origin": basis_origin.tolist(), "cell_size_m": 1,
                   "world_spans_m": cal["canonicalUnityExtentMetres"], "long_axis_m": 3500}
    vertices_m = calibrated(vertices, calibration)
    labels_file = args.source_manifest.parent / (Path(item["npz"]).stem + "-components.npz")
    with np.load(labels_file) as data:
        labels = np.asarray(data["triangle_component"], dtype=np.int32)
    order = np.argsort(labels, kind="stable")
    labels_sorted = labels[order]
    shell_ids, starts = np.unique(labels_sorted, return_index=True)
    ends = np.r_[starts[1:], len(order)]
    hull_fit = hull_lookup(args.hull_templates)
    limits = Limits(max_triangle_tests=20_000_000, max_object_cells=1_000_000,
                    max_fill_grid=8_000_000, max_total_cells=100_000_000)
    args.output.mkdir(parents=True, exist_ok=True)
    assets = []
    errors = []
    size_counts = Counter()
    total_cells = 0
    attempted = 0
    for shell_id, start, end in zip(shell_ids, starts, ends):
        shell_id = int(shell_id)
        if shell_id not in closed:
            continue
        attempted += 1
        shell_triangles = triangles[order[start:end]]
        unique, inverse = np.unique(shell_triangles, return_inverse=True)
        shell_vertices_m = vertices_m[unique]
        shell_triangles = inverse.reshape(-1, 3).astype(np.int32)
        if not mesh_watertight(shell_triangles):
            errors.append({"sourceShell": shell_id, "error": "Topology metadata says closed but indexed edges are open"})
            continue
        volume = signed_volume(shell_vertices_m, shell_triangles)
        max_axis = float(np.ptp(shell_vertices_m, axis=0).max())
        first_size = int(np.searchsorted(args.size_bands, max_axis, side="left"))
        failures = []
        accepted = False
        for cell_size in args.cell_sizes[first_size:]:
            if cell_size < .25 or cell_size > 10:
                raise ValueError("Cell size outside VoxelAssetDefinition range .25–10m")
            cell_vertices = shell_vertices_m / cell_size
            try:
                cells, raster = raster_mesh(cell_vertices, shell_triangles, limits, fill=True,
                                            return_stats=True)
                if len(cells) > args.max_cells:
                    raise RuntimeError(f"{len(cells):,} filled cells > working budget {args.max_cells:,}")
                components = connected_components(cells)
                if len(components) != 1:
                    raise RuntimeError(f"{len(components)} six-connected components from one closed source shell")
                asset_id = f"{args.asset_prefix}_{shell_id:04d}"
                asset = export_asset(args.output, asset_id, components[0],
                                     {"object": args.object, "sourceShell": shell_id},
                                     cell_size, cell_vertices, shell_triangles, hull_fit, True)
                asset["occupancyMode"] = "solid"
                asset["surfaceCells"] = raster["surfaceCells"]
                asset["interiorCells"] = raster["interiorCells"]
                asset["sourceSignedVolumeM3"] = round(volume, 2)
                asset["sourceMaxAxisM"] = round(max_axis, 3)
                asset["filledVoxelVolumeM3"] = round(len(cells) * cell_size ** 3, 2)
                assets.append(asset)
                size_counts[str(cell_size)] += 1
                total_cells += len(cells)
                accepted = True
                break
            except RuntimeError as error:
                failures.append({"cellSize": cell_size, "reason": str(error)})
        if not accepted:
            errors.append({"sourceShell": shell_id, "failures": failures})
        if attempted % 100 == 0:
            print(f"{attempted}/{len(closed)} shells, {len(assets)} accepted, {total_cells:,} cells, {len(errors)} errors", flush=True)
    report = {"description": "Watertight reference rock shells, fully solid, fixed per-asset adaptive voxel size",
              "source": args.object, "calibration": calibration, "sourceShellsExpected": len(closed),
              "sourceShellsAttempted": attempted, "assets": assets, "totalCells": total_cells,
              "sizeBandsMetres": args.size_bands,
              "cellSizeCounts": dict(size_counts), "maxCellsPerAsset": max((a["cells"] for a in assets), default=0),
              "errors": errors, "shellErrors": len(errors), "globalTranslation": [0, -420, 0]}
    (args.output / "geometry-manifest.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(f"{len(assets)}/{len(closed)} solid shells exported, {total_cells:,} cells, {len(errors)} errors, sizes {dict(size_counts)}")
    return 0 if not errors and len(assets) == len(closed) else 2


if __name__ == "__main__":
    raise SystemExit(main())
