"""Build fixed-joint contact anchors from frozen reference station voxel rows.

The sidecar uses one canonical world anchor per pair. Native authoring derives
both joint-local anchors from that one point after applying the common map
translation; the tracked integer cells are for destruction breakage. No source
asset, cell row, or scene layout is changed here.
"""

from __future__ import annotations

import argparse
from collections import Counter
import hashlib
import json
from pathlib import Path

import numpy as np
from scipy.spatial import cKDTree


OFFSETS = np.asarray([(1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1)], dtype=np.int32)


def read_instance(project: Path, asset: dict, instance: dict) -> dict:
    payload = json.loads((project / asset["cellRowsPath"]).read_text(encoding="utf-8"))
    local = np.asarray([row[:3] for row in payload["cells"]], dtype=np.int32)
    matrix = np.asarray(instance["rotationMatrix"], dtype=np.float64).reshape(3, 3)
    half = asset["cellSize"] * instance["scale"] * .5
    centers = local.astype(np.float64) * (2 * half) @ matrix.T + instance["position"]
    occupied = {tuple(int(v) for v in row) for row in local}
    boundary = np.fromiter((any(tuple(int(v) for v in row + offset) not in occupied for offset in OFFSETS)
                            for row in local), dtype=bool, count=len(local))
    return {"instance": instance, "asset": asset, "local": local, "matrix": matrix,
            "half": half, "centers": centers, "tree": cKDTree(centers), "boundary": boundary,
            "aabbHalf": np.abs(matrix).sum(axis=1) * half}


def obb_overlaps(a: dict, ia: int, b: dict, ib: int) -> bool:
    """Exact 15-axis SAT for two complete occupied cell boxes."""
    ra, rb = a["matrix"], b["matrix"]
    rotation = ra.T @ rb
    absolute = np.abs(rotation) + 1e-9
    t = ra.T @ (b["centers"][ib] - a["centers"][ia])
    ah, bh = a["half"], b["half"]
    for i in range(3):
        if abs(t[i]) > ah + bh * absolute[i, :].sum():
            return False
    for j in range(3):
        if abs(np.dot(t, rotation[:, j])) > bh + ah * absolute[:, j].sum():
            return False
    for i in range(3):
        i1, i2 = (i + 1) % 3, (i + 2) % 3
        for j in range(3):
            j1, j2 = (j + 1) % 3, (j + 2) % 3
            left = abs(t[i2] * rotation[i1, j] - t[i1] * rotation[i2, j])
            right = ah * (absolute[i1, j] + absolute[i2, j]) + bh * (absolute[i, j1] + absolute[i, j2])
            if left > right:
                return False
    return True


def contact_anchor(a: dict, b: dict) -> dict | None:
    # Query the smaller cell set against the larger KD-tree, then verify exact
    # rotated cell-box overlap. This rejects conservative world-AABB contacts.
    swapped = len(a["local"]) > len(b["local"])
    small, large = (b, a) if swapped else (a, b)
    radius = float(np.sqrt(3) * (small["half"] + large["half"]))
    best = None
    best_score = None
    accepted_pairs = 0
    boundary_pairs = 0
    for start in range(0, len(small["local"]), 5000):
        chunk = small["centers"][start:start + 5000]
        possible = large["tree"].query_ball_point(chunk, radius)
        for index, neighbors in enumerate(possible):
            si = start + index
            for li in neighbors:
                if not obb_overlaps(small, si, large, li):
                    continue
                accepted_pairs += 1
                exposed = int(small["boundary"][si]) + int(large["boundary"][li])
                boundary_pairs += int(exposed == 2)
                delta = small["centers"][si] - large["centers"][li]
                conservative_depth = float(np.min(small["aabbHalf"] + large["aabbHalf"] - np.abs(delta)))
                score = (exposed, min(conservative_depth, 3.0), -float(np.linalg.norm(delta)),
                         -int(si), -int(li))
                if best_score is None or score > best_score:
                    best_score = score
                    best = (si, li, conservative_depth, exposed)
    if best is None:
        return None
    si, li, depth, exposed = best
    ia, ib = (li, si) if swapped else (si, li)
    # A single shared canonical point makes both FixedJoint local anchors
    # coincide at creation, even when one cell is much larger than the other.
    world_anchor = (a["centers"][ia] + b["centers"][ib]) * .5
    return {"anchorCellA": a["local"][ia].tolist(), "anchorCellB": b["local"][ib].tolist(),
            "worldAnchor": world_anchor.tolist(), "overlappingCellPairs": accepted_pairs,
            "surfaceCellPairs": boundary_pairs, "selectedCellsExposed": exposed,
            "selectedConservativePenetrationMetres": round(depth, 4)}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("catalog", type=Path)
    parser.add_argument("overlap_report", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--source-evidence", type=Path, help="Optional exact source contact report keyed by asset pair")
    args = parser.parse_args()
    catalog = json.loads(args.catalog.read_text(encoding="utf-8"))
    overlaps = json.loads(args.overlap_report.read_text(encoding="utf-8"))["exactPairs"]
    project = Path(__file__).resolve().parents[2]
    assets = {asset["assetKey"]: asset for asset in catalog["assets"]}
    station_instances = [i for i in catalog["instances"] if i["source"]["object"] == "Cylinder.018"]
    by_asset = {}
    for instance in station_instances:
        by_asset.setdefault(instance["assetKey"], []).append(instance)
    if any(len(values) != 1 for values in by_asset.values()):
        raise ValueError("Station asset-to-instance mapping is not one-to-one")
    evidence = {}
    evidence_sha = None
    if args.source_evidence:
        evidence_raw = args.source_evidence.read_bytes()
        external = json.loads(evidence_raw)
        evidence_sha = hashlib.sha256(evidence_raw).hexdigest()
        evidence = {"|".join(sorted(pair["assetPair"])): pair for pair in external["pairs"]}
        if len(evidence) != len(overlaps):
            raise ValueError("Source contact evidence does not cover every voxel pair")
    cache = {}
    links = []
    rejected = []
    for pair in overlaps:
        key_a, key_b = pair["firstAsset"], pair["secondAsset"]
        if key_a not in by_asset or key_b not in by_asset:
            raise ValueError(f"Missing station instance for pair {key_a}/{key_b}")
        for key in (key_a, key_b):
            if key not in cache:
                cache[key] = read_instance(project, assets[key], by_asset[key][0])
        contact = contact_anchor(cache[key_a], cache[key_b])
        if contact is None:
            rejected.append({"assetKeyA": key_a, "assetKeyB": key_b,
                             "reason": "Conservative AABB cell boxes overlap, exact rotated OBB cells do not"})
            continue
        first, second = by_asset[key_a][0], by_asset[key_b][0]
        pair_key = "|".join(sorted((key_a, key_b)))
        source_record = evidence.get(pair_key)
        if args.source_evidence and source_record is None:
            raise ValueError(f"Missing source contact evidence for {pair_key}")
        source_status = source_record["status"] if source_record else "unverified source geometry"
        fixed = source_status == "source_triangle_intersection"
        link = {"instanceIdA": first["id"], "instanceIdB": second["id"],
                "assetKeyA": key_a, "assetKeyB": key_b,
                "sourceA": first["source"], "sourceB": second["source"],
                "sourceEvidence": source_status,
                "sourceAabbGapMetres": source_record.get("sourceAabbGapMetres") if source_record else None,
                "ignoreMutualCollision": True, "createFixedJoint": fixed,
                "jointKind": "FixedJoint" if fixed else None, **contact}
        links.append(link)
    unique = {tuple(sorted((link["instanceIdA"], link["instanceIdB"]))) for link in links}
    if len(unique) != len(links):
        raise ValueError("Duplicate assembly link pair")
    report = {"description": "Candidate contact links from exact physical occupied-cell OBB overlap; source-evidence status is explicit",
              "catalogSha256": hashlib.sha256(args.catalog.read_bytes()).hexdigest(),
              "sourceEvidenceSha256": evidence_sha,
              "coordinateSystem": "canonical Unity metres before globalTranslation",
              "globalTranslationApplied": False,
              "candidateAabbPairs": len(overlaps), "jointLinks": len(links), "rejectedConservativePairs": len(rejected),
              "fixedJointPairs": sum(link["createFixedJoint"] for link in links),
              "ignoreOnlyPairs": sum(not link["createFixedJoint"] for link in links),
              "links": links, "rejected": rejected,
              "sourceEvidenceCounts": dict(Counter(str(link["sourceEvidence"]) for link in links))}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(f"{len(overlaps)} AABB pairs -> {len(links)} exact occupied-cell OBB links, {len(rejected)} rejected")


if __name__ == "__main__":
    main()
