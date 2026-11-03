"""Freeze checks for the complete reference scene interchange, outside Unity."""

from __future__ import annotations

import argparse
from collections import Counter
import hashlib
import json
from pathlib import Path

import numpy as np


SHAPES = {"Cube", "Wedge", "Slab", "Pyramid", "Block5", "Block6", "Block7", "Block8"}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("catalog", type=Path)
    parser.add_argument("source_topology", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    raw = args.catalog.read_bytes()
    catalog = json.loads(raw)
    topology = json.loads(args.source_topology.read_text(encoding="utf-8"))
    project = Path(__file__).resolve().parents[2]
    assets = {asset["assetKey"]: asset for asset in catalog["assets"]}
    if len(assets) != len(catalog["assets"]):
        raise ValueError("Duplicate catalog asset keys")
    keys = [instance["id"] for instance in catalog["instances"]]
    if len(set(keys)) != len(keys):
        raise ValueError("Duplicate catalog instance IDs")
    total_unique = 0
    shapes = Counter()
    modes = Counter()
    sizes = Counter()
    for asset in assets.values():
        if asset["connectedComponents"] != 1 or not 0 < asset["cells"] <= 131072:
            raise ValueError(f"Invalid connectivity/cap: {asset['assetKey']}")
        if asset["occupancyMode"] not in ("solid", "authoredHollowShell"):
            raise ValueError(f"Missing explicit occupancy mode: {asset['assetKey']}")
        rows_file = project / asset["cellRowsPath"]
        payload = json.loads(rows_file.read_text(encoding="utf-8"))
        rows = payload["cells"]
        if len(rows) != asset["cells"] or any(len(row) != 11 for row in rows):
            raise ValueError(f"Invalid frozen cell rows: {asset['assetKey']}")
        digest = hashlib.sha256(json.dumps(rows, separators=(",", ":")).encode("utf-8")).hexdigest()
        if digest != asset["geometryHashSha256"]:
            raise ValueError(f"Cell rows changed after catalog freeze: {asset['assetKey']}")
        positions = [tuple(row[:3]) for row in rows]
        if len(set(positions)) != len(positions):
            raise ValueError(f"Duplicate voxel positions: {asset['assetKey']}")
        total_unique += len(rows)
        shapes.update(asset["shapeCounts"])
        modes[asset["occupancyMode"]] += 1
        sizes[f"{float(asset['cellSize']):g}"] += 1
    if set(shapes) != SHAPES:
        raise ValueError(f"Expected all eight real hull shapes, got {sorted(shapes)}")
    if total_unique != catalog["uniqueCells"]:
        raise ValueError("Catalog unique cell total mismatch")
    source_shells = {}
    source_group_shells = {}
    mode_instances = Counter()
    occupied_bounds = [np.full(3, np.inf), np.full(3, -np.inf)]
    for instance in catalog["instances"]:
        asset = assets.get(instance["assetKey"])
        if asset is None:
            raise ValueError(f"Instance references absent asset: {instance['assetKey']}")
        mode_instances[asset["occupancyMode"]] += 1
        source = instance["source"]
        name = source["object"]
        if "sourceGroupShells" in source:
            group_key = (name, tuple(source["sourceGroupShells"]))
            source_group_shells[group_key] = list(source["sourceGroupShells"])
        else:
            ids = source.get("sourceShells", [source.get("sourceShell")])
            if any(shell is None or shell < 0 for shell in ids):
                raise ValueError(f"Missing source shell provenance: {instance['id']}")
            for shell in ids:
                source_shells.setdefault(name, []).append(int(shell))
        lo, hi = [np.asarray(v, dtype=np.float64) for v in asset["boundsCells"]]
        corners = np.asarray([(x, y, z) for x in (lo[0] - .5, hi[0] + .5)
                                      for y in (lo[1] - .5, hi[1] + .5)
                                      for z in (lo[2] - .5, hi[2] + .5)])
        matrix = np.asarray(instance["rotationMatrix"], dtype=np.float64).reshape(3, 3)
        world = corners * asset["cellSize"] * instance["scale"] @ matrix.T
        world += np.asarray(instance["position"]) + np.asarray(catalog["globalTranslation"])
        occupied_bounds[0] = np.minimum(occupied_bounds[0], world.min(axis=0))
        occupied_bounds[1] = np.maximum(occupied_bounds[1], world.max(axis=0))
    topologies = {item["name"]: {int(shell["id"]) for shell in item["components"]}
                  for item in topology["objects"]}
    expected = {name: topologies[name] for name in ("Cylinder.018", "Cube.001", "tyFlow_node_515.347", "tyFlow_node_515.521")}
    coverage = {}
    for name, expected_ids in expected.items():
        seen = source_shells.get(name, []) + [shell for (group_name, _), shells in source_group_shells.items()
                                               if group_name == name for shell in shells]
        counts = Counter(seen)
        missing = expected_ids - set(seen)
        extra = set(seen) - expected_ids
        duplicate = [shell for shell, count in counts.items() if count > 1]
        coverage[name] = {"expected": len(expected_ids), "placed": len(seen),
                          "missing": sorted(missing), "extra": sorted(extra), "duplicate": sorted(duplicate)}
        if missing or extra or duplicate:
            raise ValueError(f"Source coverage failure for {name}: {coverage[name]}")
    if any(instance["source"]["object"] in ("scatter", "asteroids__0.1038") for instance in catalog["instances"]):
        raise ValueError("Unplaced prototype or decorative card became a scene instance")
    result = {"catalogSha256": hashlib.sha256(raw).hexdigest(),
              "sourceCoverage": coverage, "assetCount": len(assets),
              "instanceCount": len(catalog["instances"]), "uniqueCells": total_unique,
              "instancedCells": catalog["instancedCells"],
              "shapeCountsUnique": dict(shapes), "occupancyModeAssets": dict(modes),
              "occupancyModeInstances": dict(mode_instances), "cellSizeAssets": dict(sizes),
              "conservativeSceneBoundsMetres": [occupied_bounds[0].tolist(), occupied_bounds[1].tolist()],
              "conservativeSceneSpansMetres": (occupied_bounds[1] - occupied_bounds[0]).tolist(),
              "globalTranslation": catalog["globalTranslation"], "allChecksPassed": True}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2), encoding="utf-8")
    print(json.dumps({key: result[key] for key in ("assetCount", "instanceCount", "uniqueCells", "instancedCells",
                                                    "occupancyModeAssets", "cellSizeAssets", "conservativeSceneSpansMetres")}, indent=2))


if __name__ == "__main__":
    main()
