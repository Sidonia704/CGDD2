"""Combine reference-derived voxel batches into an import-ready asset/instance catalog."""

from __future__ import annotations

import argparse
from collections import Counter
import hashlib
import json
from pathlib import Path
import uuid


MERGED_SOURCE_SHELLS = {
    "GeometryHexDeck": list(range(497, 570)) + list(range(593, 601)),
    "LeftUpperEquipment": list(range(2, 19)),
}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path)
    parser.add_argument("manifests", type=Path, nargs="+")
    parser.add_argument("--cards", type=Path)
    parser.add_argument("--assets-only", type=Path, action="append", default=[],
                        help="Load prototype assets from this manifest without placing its source prototype objects")
    parser.add_argument("--hollow-manifest", type=Path, action="append", default=[],
                        help="Explicitly mark this source-surface batch as authored hollow one-cell walls")
    parser.add_argument("--translation-y", type=float, default=-420)
    args = parser.parse_args()
    project = Path(__file__).resolve().parents[2]
    assets = {}
    instances = []
    prototype_by_hash = {}
    assets_only = {path.resolve() for path in args.assets_only}
    hollow_manifests = {path.resolve() for path in args.hollow_manifest}
    for manifest_path in args.manifests:
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
        if manifest.get("errors") or manifest.get("shellErrors"):
            raise ValueError(f"Cannot catalog incomplete geometry: {manifest_path}")
        for asset in manifest["assets"]:
            key = asset["id"]
            if key in assets:
                raise ValueError(f"Duplicate asset key across batches: {key}")
            json_path = (manifest_path.parent / asset["json"]).resolve()
            if not asset["json"] or not json_path.is_file():
                raise ValueError(f"Missing JSON cell rows: {json_path}")
            payload = json.loads(json_path.read_text(encoding="utf-8"))
            rows = payload["cells"]
            if len(rows) != asset["cells"] or any(len(row) != 11 for row in rows):
                raise ValueError(f"Invalid cell rows for {key}")
            digest = hashlib.sha256(json.dumps(rows, separators=(",", ":")).encode("utf-8")).hexdigest()
            prototype = prototype_by_hash.setdefault((asset["cellSize"], digest), key)
            source = dict(asset["source"])
            if source.get("sourceShell") == -1:
                selected_shells = MERGED_SOURCE_SHELLS.get(manifest_path.parent.name)
                if selected_shells is None:
                    raise ValueError(f"Missing provenance for merged source batch {manifest_path}")
                source.pop("sourceShell")
                source["sourceGroupShells"] = selected_shells
            occupancy_mode = asset.get("occupancyMode", manifest.get("occupancyMode",
                                 "authoredHollowShell" if manifest_path.resolve() in hollow_manifests else "solid"))
            assets[key] = {"assetKey": key, "prototypeKey": prototype,
                           "cellRowsPath": json_path.relative_to(project).as_posix(),
                           "cellSize": asset["cellSize"], "cells": asset["cells"],
                           "boundsCells": asset["boundsCells"],
                           "occupancyMode": occupancy_mode,
                           "wallThicknessCells": 1 if occupancy_mode == "authoredHollowShell" else None,
                           "shapeCounts": asset["shapeCounts"], "connectedComponents": asset["connectedComponents"],
                           "geometryHashSha256": digest, "source": source}
            if manifest_path.resolve() in assets_only:
                continue
            instances.append({"id": uuid.uuid5(uuid.NAMESPACE_URL, key).hex, "assetKey": prototype,
                              "label": key, "position": asset["position"],
                              "eulerAngles": asset["eulerAngles"],
                              "rotationMatrix": asset.get("rotationMatrix", [1, 0, 0, 0, 1, 0, 0, 0, 1]),
                              "scale": asset["scale"], "healthMultiplier": 1,
                              "driftVelocity": asset.get("driftVelocity", [0, 0, 0]),
                              "angularVelocity": asset.get("angularVelocity", [0, 0, 0]),
                              "source": source})
    if args.cards:
        cards = json.loads(args.cards.read_text(encoding="utf-8"))
        for index, card in enumerate(cards["instances"]):
            key = card["assetKey"]
            if key not in assets:
                raise ValueError(f"Card uses absent prototype {key}")
            instance = dict(card)
            instance["assetKey"] = assets[key]["prototypeKey"]
            instance["id"] = uuid.uuid5(uuid.NAMESPACE_URL, f"card-cluster-{index}").hex
            instance["label"] = f"Reference card rock {index:04d}"
            instances.append(instance)
    unique = {item["prototypeKey"]: item for item in assets.values() if item["prototypeKey"] == item["assetKey"]}
    shape_counts = Counter()
    for asset in unique.values():
        shape_counts.update(asset["shapeCounts"])
    result = {"description": "Reference-derived voxel assets and exact canonical source placements. Apply globalTranslation once at map layout import.",
              "globalTranslation": [0, args.translation_y, 0], "coordinateSystem": "Unity x,y,z metres",
              "assets": list(unique.values()), "instances": instances,
              "sourceBatchAssets": len(assets), "uniquePrototypeAssets": len(unique),
              "instanceCount": len(instances), "uniqueCells": sum(a["cells"] for a in unique.values()),
              "instancedCells": sum(unique[i["assetKey"]]["cells"] for i in instances),
              "shapeCountsUnique": dict(shape_counts), "cardsIncluded": bool(args.cards)}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2), encoding="utf-8")
    print(f"{len(unique)} unique assets, {len(instances)} instances, {result['uniqueCells']:,} unique cells, {result['instancedCells']:,} instanced cells")


if __name__ == "__main__":
    main()
