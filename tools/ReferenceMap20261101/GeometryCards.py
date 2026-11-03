"""Replace each reference field's planar rock card with a source 3D prototype.

The planar shells remain evidence for positions, long/short silhouette spans and
orientation. A closed 3D mesh from the reference's own scatter collection is
chosen by aspect and size; each substitute is an independent map instance.
"""

from __future__ import annotations

import argparse
import json
import math
from pathlib import Path

import numpy as np
from scipy.sparse import coo_matrix
from scipy.sparse.csgraph import connected_components
from scipy.spatial import cKDTree

from Geometry import object_arrays, world_vertices, unity_basis, calibrated, unity_euler


def points_for_shell(item: dict, manifest_dir: Path, shell_id: int, calibration: dict) -> np.ndarray:
    vertices, triangles = object_arrays(item, manifest_dir)
    vertices = calibrated(unity_basis(world_vertices(vertices, item)), calibration)
    labels_file = manifest_dir / (Path(item["npz"]).stem + "-components.npz")
    with np.load(labels_file) as data:
        labels = data["triangle_component"]
    used = np.unique(triangles[labels == shell_id])
    return vertices[used]


def profile(points: np.ndarray) -> dict:
    center = (points.min(axis=0) + points.max(axis=0)) * .5
    centered = points - center
    _, vectors = np.linalg.eigh(centered.T @ centered)
    major = vectors[:, 2]
    minor = vectors[:, 0]
    if major[np.argmax(np.abs(major))] < 0:
        major *= -1
    if minor[np.argmax(np.abs(minor))] < 0:
        minor *= -1
    middle = np.cross(minor, major)
    middle /= np.linalg.norm(middle)
    minor = np.cross(major, middle)
    frame = np.stack((major, middle, minor), axis=1)
    span = np.ptp(centered @ frame, axis=0)
    # Major and middle are ordered by PCA eigenvalue, but axis-aligned span can
    # reverse slightly; use the two largest dimensions for matching.
    sorted_span = np.sort(span)[::-1]
    return {"center": center, "frame": frame, "span": sorted_span,
            "aspect": float(sorted_span[0] / max(sorted_span[1], .01))}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source_manifest", type=Path)
    parser.add_argument("topology", type=Path)
    parser.add_argument("prototype_manifest", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--extra-prototypes", type=Path, action="append", default=[])
    args = parser.parse_args()
    source = json.loads(args.source_manifest.read_text(encoding="utf-8"))
    topology = json.loads(args.topology.read_text(encoding="utf-8"))
    manifests = [json.loads(path.read_text(encoding="utf-8"))
                 for path in [args.prototype_manifest, *args.extra_prototypes]]
    prototype = manifests[0]
    calibration = prototype["calibration"]
    by_name = {item["name"]: item for item in source["objects"]}
    field = by_name["Cube.001"]
    topo_field = next(item for item in topology["objects"] if item["name"] == "Cube.001")
    card_ids = [int(item["id"]) for item in topo_field["components"] if not item["watertight"]]
    proto_profiles = []
    for asset in [asset for manifest in manifests for asset in manifest["assets"]]:
        src = asset["source"]
        points = points_for_shell(by_name[src["object"]], args.source_manifest.parent, src["sourceShell"], calibration)
        p = profile(points)
        p["assetKey"] = asset["id"]
        p["sourceObject"] = src["object"]
        proto_profiles.append(p)
    if not proto_profiles:
        raise ValueError("No closed source 3D prototypes")
    vertices, triangles = object_arrays(field, args.source_manifest.parent)
    vertices = calibrated(unity_basis(world_vertices(vertices, field)), calibration)
    labels_file = args.source_manifest.parent / (Path(field["npz"]).stem + "-components.npz")
    with np.load(labels_file) as data:
        labels = np.asarray(data["triangle_component"], dtype=np.int32)
    order = np.argsort(labels, kind="stable")
    sorted_labels = labels[order]
    unique_ids, starts = np.unique(sorted_labels, return_index=True)
    ends = np.r_[starts[1:], len(order)]
    shell_triangles = {int(shell_id): triangles[order[start:end]] for shell_id, start, end in zip(unique_ids, starts, ends)
                       if int(shell_id) in set(card_ids)}
    card_shells = []
    for shell_id in card_ids:
        tri = shell_triangles[shell_id]
        points = vertices[np.unique(tri)]
        card = profile(points)
        card_shells.append({"id": shell_id, "points": points, "triangles": len(tri), "profile": card})
    centers = np.stack([shell["profile"]["center"] for shell in card_shells])
    radii = np.asarray([shell["profile"]["span"][0] * .5 for shell in card_shells])
    max_radius = float(radii.max())
    pairs = cKDTree(centers).query_pairs(2 * max_radius, output_type="ndarray")
    separation = np.linalg.norm(centers[pairs[:, 0]] - centers[pairs[:, 1]], axis=1)
    linked = pairs[separation < radii[pairs[:, 0]] + radii[pairs[:, 1]]]
    graph = coo_matrix((np.ones(len(linked), dtype=np.uint8), (linked[:, 0], linked[:, 1])),
                       shape=(len(card_shells), len(card_shells)))
    cluster_count, cluster_id = connected_components(graph)
    instances = []
    scales = []
    cluster_sizes = []
    for cluster in range(cluster_count):
        members = [card_shells[i] for i in np.flatnonzero(cluster_id == cluster)]
        card = profile(np.concatenate([member["points"] for member in members]))
        cluster_sizes.append(len(members))
        best = None
        best_score = math.inf
        for candidate in proto_profiles:
            scale = float(card["span"][0] / max(candidate["span"][0], .01))
            clamped = min(100.0, max(.25, scale))
            score = abs(math.log(card["aspect"] / candidate["aspect"])) + .15 * abs(math.log(max(scale, .01)))
            if clamped != scale:
                score += 100 * abs(math.log(max(scale, .01) / clamped))
            if score < best_score:
                best, best_score = candidate, score
        scale = float(np.clip(card["span"][0] / best["span"][0], .25, 100))
        rotation = card["frame"] @ best["frame"].T
        instances.append({"assetKey": best["assetKey"], "position": card["center"].tolist(),
                          "eulerAngles": unity_euler(rotation), "rotationMatrix": rotation.reshape(-1).tolist(),
                          "scale": scale, "healthMultiplier": 1, "driftVelocity": [0, 0, 0],
                          "angularVelocity": [0, 0, 0], "source": {"object": "Cube.001",
                          "sourceShells": [member["id"] for member in members],
                          "sourceTriangles": sum(member["triangles"] for member in members),
                          "sourceSpanMetres": card["span"].tolist(),
                          "replacement": "closed source scatter prototype", "prototypeObject": best["sourceObject"]}})
        scales.append(scale)
    report = {"description": "Overlapping planar card faces are grouped into physical source rocks, then each is replaced by a closed 3D source prototype at its exact reference center",
              "cardShellCount": len(card_ids), "rockClusterCount": cluster_count,
              "clusterSizeHistogram": {str(size): cluster_sizes.count(size) for size in sorted(set(cluster_sizes))},
              "prototypeCount": len(proto_profiles), "instances": instances,
              "prototypeUse": {key: sum(x["assetKey"] == key for x in instances)
                               for key in sorted({x["assetKey"] for x in instances})},
              "scaleMinMedianMax": [float(np.min(scales)), float(np.median(scales)), float(np.max(scales))],
              "canonicalCenterBoundsMetres": [np.min([x["position"] for x in instances], axis=0).tolist(),
                                               np.max([x["position"] for x in instances], axis=0).tolist()]}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(f"{len(card_ids)} card faces -> {len(instances)} source-positioned 3D rocks, {len(report['prototypeUse'])} prototypes used")


if __name__ == "__main__":
    main()
