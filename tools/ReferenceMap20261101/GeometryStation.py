"""Semantically group remaining 1m reference station shell walls by real contact.

Large rounded masses, the three oriented trusses, hex deck, five tanks and left
upper equipment are authored separately. This tool covers every other station
shell exactly once. Inside each source-identified family it unions only voxels
that overlap or share a face, then writes one six-connected asset per component.
No cell is inserted as a bridge. These metal walls are explicitly authored as
one-cell source shells rather than silently claiming solid interior occupancy.
"""

from __future__ import annotations

import argparse
from collections import Counter
import json
from pathlib import Path

import numpy as np

from Geometry import (Limits, calibrated, connected_components, export_asset,
                      hull_lookup, object_arrays, raster_mesh, unity_basis,
                      world_vertices)


FAMILIES = {
    "left_supports": list(range(19, 25)),
    "left_box_grid": list(range(25, 119)),
    "left_tank_cluster": list(range(119, 127)),
    "right_rock_fittings": list(range(129, 134)),
    "right_station_fixtures": list(range(135, 200)),
    "left_radiator_bank": list(range(200, 214)) + list(range(243, 271)),
    "right_radiator_bank": list(range(214, 243)) + list(range(271, 285)),
    "cantilever_network": list(range(285, 340)),
    "outer_equipment_a": list(range(340, 398)),
    "outer_equipment_b": list(range(398, 450)),
    "crossing_booms": list(range(450, 497)),
    "deck_understructure": list(range(575, 593)),
    "deck_post_601": [601],
}

ALREADY_AUTHORED = ({0, 1, 127, 128, 134} | set(range(2, 19)) |
                    set(range(497, 575)) | set(range(593, 601)))


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source_manifest", type=Path)
    parser.add_argument("source_calibration", type=Path)
    parser.add_argument("hull_templates", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--only-families", nargs="*")
    args = parser.parse_args()
    selected = FAMILIES if args.only_families is None else {name: FAMILIES[name] for name in args.only_families}
    if not args.only_families:
        covered = [shell for ids in FAMILIES.values() for shell in ids]
        expected = set(range(602)) - ALREADY_AUTHORED
        if len(covered) != len(set(covered)) or set(covered) != expected:
            raise ValueError(f"Station shell coverage mismatch: missing {sorted(expected-set(covered))}, extra {sorted(set(covered)-expected)}")
    source = json.loads(args.source_manifest.read_text(encoding="utf-8"))
    item = next(obj for obj in source["objects"] if obj["name"] == "Cylinder.018")
    local_vertices, triangles = object_arrays(item, args.source_manifest.parent)
    basis_vertices = unity_basis(world_vertices(local_vertices, item))
    cal = json.loads(args.source_calibration.read_text(encoding="utf-8"))
    matrix = np.asarray(cal["sourceWorldToCanonicalUnityMatrixRows"], dtype=np.float64)
    calibration = {"scale": float(cal["metresPerSourceUnit"]),
                   "source_origin": (-matrix[:3, 3] / float(cal["metresPerSourceUnit"])).tolist(),
                   "cell_size_m": 1, "world_spans_m": cal["canonicalUnityExtentMetres"], "long_axis_m": 3500}
    vertices = calibrated(basis_vertices, calibration)
    labels_file = args.source_manifest.parent / (Path(item["npz"]).stem + "-components.npz")
    with np.load(labels_file) as data:
        labels = np.asarray(data["triangle_component"], dtype=np.int32)
    order = np.argsort(labels, kind="stable")
    sorted_labels = labels[order]
    ids, starts = np.unique(sorted_labels, return_index=True)
    ends = np.r_[starts[1:], len(order)]
    slices = {int(shell): triangles[order[start:end]] for shell, start, end in zip(ids, starts, ends)}
    limits = Limits(max_triangle_tests=60_000_000, max_object_cells=1_000_000,
                    max_fill_grid=8_000_000, max_total_cells=100_000_000)
    hull_fit = hull_lookup(args.hull_templates)
    args.output.mkdir(parents=True, exist_ok=True)
    assets = []
    group_report = []
    errors = []
    for family, shell_ids in selected.items():
        group_cells = set()
        by_shell = {}
        family_indices = []
        print(f"{family}: {len(shell_ids)} source shells", flush=True)
        for shell_id in shell_ids:
            if shell_id not in slices:
                errors.append({"family": family, "sourceShell": shell_id, "error": "Missing source shell"})
                continue
            tri_global = slices[shell_id]
            unique, inverse = np.unique(tri_global, return_inverse=True)
            tri = inverse.reshape(-1, 3).astype(np.int32)
            try:
                cells = raster_mesh(vertices[unique], tri, limits, fill=False)
            except RuntimeError as error:
                errors.append({"family": family, "sourceShell": shell_id, "error": str(error)})
                continue
            shell_set = {tuple(int(v) for v in cell) for cell in cells}
            by_shell[shell_id] = shell_set
            group_cells.update(shell_set)
            family_indices.append(tri_global)
        if not group_cells:
            group_report.append({"family": family, "sourceShells": shell_ids, "assets": 0, "cells": 0})
            continue
        components = connected_components(np.asarray(list(group_cells), dtype=np.int32))
        tri_group_global = np.concatenate(family_indices)
        unique, inverse = np.unique(tri_group_global, return_inverse=True)
        group_vertices = vertices[unique]
        group_triangles = inverse.reshape(-1, 3).astype(np.int32)
        counts = []
        for part_index, component in enumerate(components):
            comp_set = {tuple(int(v) for v in cell) for cell in component}
            sources = [shell for shell, shell_set in by_shell.items() if not comp_set.isdisjoint(shell_set)]
            asset_id = f"station_{family}_{part_index:03d}"
            try:
                asset = export_asset(args.output, asset_id, component,
                                     {"object": "Cylinder.018", "family": family, "sourceShells": sources},
                                     1, group_vertices, group_triangles, hull_fit, True)
                asset["occupancyMode"] = "authoredHollowShell"
                asset["wallThicknessCells"] = 1
                assets.append(asset)
                counts.append(len(component))
            except RuntimeError as error:
                errors.append({"family": family, "component": part_index, "cells": len(component), "error": str(error)})
        group_report.append({"family": family, "sourceShells": shell_ids,
                             "components": [len(component) for component in components],
                             "assets": len(counts), "cells": sum(counts)})
        print(f"  {len(components)} connected components, {sum(counts):,} exported cells", flush=True)
    shape_counts = Counter()
    for asset in assets:
        shape_counts.update(asset["shapeCounts"])
    report = {"description": "Remaining station structures: source-exact one-metre authored hollow walls grouped only by actual six-neighbour contact within visual families",
              "calibration": calibration, "source": "Cylinder.018", "occupancyMode": "authoredHollowShell",
              "families": group_report, "assets": assets, "totalCells": sum(a["cells"] for a in assets),
              "shapeCounts": dict(shape_counts), "errors": errors, "shellErrors": len(errors),
              "coveredSourceShells": sorted({shell for group in group_report for shell in group["sourceShells"]}),
              "alreadyAuthoredSourceShells": sorted(ALREADY_AUTHORED)}
    (args.output / "geometry-manifest.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(f"{len(assets)} station assets, {report['totalCells']:,} cells, {len(errors)} errors")
    return 0 if not errors else 2


if __name__ == "__main__":
    raise SystemExit(main())
