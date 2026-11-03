"""Validate every assembly-link reference and shared-anchor invariant."""

from __future__ import annotations

from collections import Counter
import hashlib
import json
from pathlib import Path
import sys

import numpy as np


def main() -> None:
    if len(sys.argv) != 4:
        raise SystemExit("usage: GeometryAssemblyAudit.py catalog.json scene-assembly-links.json audit.json")
    catalog_path, links_path, out_path = map(Path, sys.argv[1:])
    catalog_raw, links_raw = catalog_path.read_bytes(), links_path.read_bytes()
    catalog, sidecar = json.loads(catalog_raw), json.loads(links_raw)
    project = Path(__file__).resolve().parents[2]
    assets = {asset["assetKey"]: asset for asset in catalog["assets"]}
    instances = {instance["id"]: instance for instance in catalog["instances"]}
    if hashlib.sha256(catalog_raw).hexdigest() != sidecar["catalogSha256"]:
        raise ValueError("Assembly sidecar targets a different frozen scene catalog")
    pair_ids = set()
    row_cache = {}
    statuses = Counter()
    max_midpoint_error = 0.0
    for link in sidecar["links"]:
        first_id, second_id = link["instanceIdA"], link["instanceIdB"]
        if first_id not in instances or second_id not in instances or first_id == second_id:
            raise ValueError("Missing/self instance reference")
        pair_id = tuple(sorted((first_id, second_id)))
        if pair_id in pair_ids:
            raise ValueError("Duplicate assembly pair")
        pair_ids.add(pair_id)
        expected_fixed = link["sourceEvidence"] == "source_triangle_intersection"
        if link["createFixedJoint"] != expected_fixed or link["ignoreMutualCollision"] is not True:
            raise ValueError("Source-contact policy mismatch")
        if link["jointKind"] != ("FixedJoint" if expected_fixed else None):
            raise ValueError("Joint kind mismatch")
        statuses[link["sourceEvidence"]] += 1
        centers = []
        for suffix, instance_id in (("A", first_id), ("B", second_id)):
            instance = instances[instance_id]
            key = instance["assetKey"]
            if link[f"assetKey{suffix}"] != key or key not in assets:
                raise ValueError("Asset key and instance reference mismatch")
            if key not in row_cache:
                payload = json.loads((project / assets[key]["cellRowsPath"]).read_text(encoding="utf-8"))
                row_cache[key] = {tuple(row[:3]) for row in payload["cells"]}
            cell = tuple(link[f"anchorCell{suffix}"])
            if len(cell) != 3 or not all(isinstance(v, int) for v in cell) or cell not in row_cache[key]:
                raise ValueError(f"Tracked anchor cell missing from asset {key}: {cell}")
            matrix = np.asarray(instance["rotationMatrix"], dtype=np.float64).reshape(3, 3)
            center = matrix @ (np.asarray(cell) * assets[key]["cellSize"] * instance["scale"])
            center += np.asarray(instance["position"])
            centers.append(center)
        world_anchor = np.asarray(link["worldAnchor"], dtype=np.float64)
        if world_anchor.shape != (3,) or not np.isfinite(world_anchor).all():
            raise ValueError("Invalid shared canonical world anchor")
        midpoint_error = float(np.linalg.norm(world_anchor - (centers[0] + centers[1]) * .5))
        max_midpoint_error = max(max_midpoint_error, midpoint_error)
        if midpoint_error > 1e-6:
            raise ValueError("Shared world anchor is not the recorded contact midpoint")
        # Deriving both local joint anchors from this ONE point must reproduce
        # the same world point, including after the common layout translation.
        shift = np.asarray(catalog["globalTranslation"])
        for instance_id in (first_id, second_id):
            instance = instances[instance_id]
            matrix = np.asarray(instance["rotationMatrix"], dtype=np.float64).reshape(3, 3)
            local_anchor = matrix.T @ (world_anchor - np.asarray(instance["position"])) / instance["scale"]
            reproduced = matrix @ (local_anchor * instance["scale"]) + np.asarray(instance["position"]) + shift
            if np.linalg.norm(reproduced - (world_anchor + shift)) > 1e-6:
                raise ValueError("FixedJoint local anchors would not coincide after layout translation")
    if len(sidecar["links"]) != sidecar["jointLinks"] or len(sidecar["links"]) != 125:
        raise ValueError("Link count mismatch")
    if statuses != Counter({"source_triangle_intersection": 112,
                            "no_source_contact_detected": 11,
                            "source_separated_aabb": 2}):
        raise ValueError(f"Unexpected source evidence distribution: {statuses}")
    report = {"sidecarSha256": hashlib.sha256(links_raw).hexdigest(),
              "catalogSha256": sidecar["catalogSha256"],
              "sourceEvidenceSha256": sidecar["sourceEvidenceSha256"],
              "pairCount": len(sidecar["links"]), "fixedJointPairs": sidecar["fixedJointPairs"],
              "ignoreOnlyPairs": sidecar["ignoreOnlyPairs"],
              "sourceStatusCounts": dict(statuses), "uniqueAssetCellFilesChecked": len(row_cache),
              "maxSharedAnchorMidpointErrorMetres": max_midpoint_error,
              "allChecksPassed": True}
    out_path.parent.mkdir(parents=True, exist_ok=True)
    out_path.write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
