"""Reference-mesh to sparse six-connected voxel interchange.

This is an offline geometry tool. It reads a Blender-exported manifest and NPZ
triangle meshes, and writes only JSON/NPZ files outside Unity's Assets folder.
It never changes source meshes, Unity scenes, or native assets.

The source surface is rasterized by exact triangle/AABB separating-axis tests.
There are no invented bridges: disconnected voxel islands become separate
objects, and a source mesh is filled only if all mesh edges are paired.
"""

from __future__ import annotations

import argparse
from collections import Counter, deque
from dataclasses import dataclass
import json
import math
from pathlib import Path
import sys

import numpy as np
from scipy import ndimage
from scipy.spatial import ConvexHull, cKDTree
from scipy.spatial.transform import Rotation


NEIGHBORS = np.array([(1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1)], dtype=np.int32)
MAX_ASSET_CELLS = 131_072


@dataclass
class Limits:
    max_triangle_tests: int = 8_000_000
    max_object_cells: int = 1_000_000
    max_fill_grid: int = 8_000_000
    max_total_cells: int = 3_000_000
    chunk_tests: int = 200_000


def source_objects(manifest: dict) -> list[dict]:
    for key in ("objects", "meshes"):
        if isinstance(manifest.get(key), list):
            return [item for item in manifest[key] if item.get("npz") or item.get("mesh_file")]
    raise ValueError("Manifest needs an objects/meshes array with NPZ paths")


def object_arrays(item: dict, manifest_dir: Path) -> tuple[np.ndarray, np.ndarray]:
    npz_path = manifest_dir / (item.get("npz") or item["mesh_file"])
    with np.load(npz_path) as data:
        vertices = np.asarray(data["vertices"], dtype=np.float64)
        triangles = np.asarray(data["triangles"], dtype=np.int32).reshape(-1, 3)
    if vertices.ndim != 2 or vertices.shape[1] != 3 or triangles.ndim != 2 or triangles.shape[1] != 3:
        raise ValueError(f"Invalid triangle arrays: {npz_path}")
    if not np.isfinite(vertices).all() or triangles.min(initial=0) < 0 or triangles.max(initial=0) >= len(vertices):
        raise ValueError(f"Nonfinite vertex or invalid index: {npz_path}")
    return vertices, triangles


def world_vertices(vertices: np.ndarray, item: dict) -> np.ndarray:
    matrix = np.asarray(item.get("matrix_world", np.eye(4)), dtype=np.float64).reshape(4, 4)
    if not np.isfinite(matrix).all():
        raise ValueError(f"Nonfinite matrix_world for {item.get('name')}")
    return vertices @ matrix[:3, :3].T + matrix[:3, 3]


def unity_basis(vertices: np.ndarray) -> np.ndarray:
    """Convert Blender Z-up world coordinates to Unity Y-up (x,z,-y)."""
    return vertices[:, [0, 2, 1]] * np.array((1, 1, -1))


def grid_transform(bounds: np.ndarray, long_axis_m: float, cell_size_m: float, origin: np.ndarray | None) -> dict:
    spans = bounds[1] - bounds[0]
    longest = float(spans.max())
    if longest <= 0:
        raise ValueError("Source scene has zero extent")
    scale = long_axis_m / longest
    center = (bounds[0] + bounds[1]) * 0.5 if origin is None else origin
    return {"scale": scale, "source_origin": center.tolist(), "cell_size_m": cell_size_m,
            "source_bounds": bounds.tolist(), "world_spans_m": (spans * scale).tolist(),
            "long_axis_m": long_axis_m}


def calibrated(vertices: np.ndarray, calibration: dict) -> np.ndarray:
    return (vertices - np.asarray(calibration["source_origin"])) * calibration["scale"] / calibration["cell_size_m"]


def horizontal_frame(vertices: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
    """Source-derived orthonormal local frame, with Unity up kept vertical."""
    origin = (vertices.min(axis=0) + vertices.max(axis=0)) * 0.5
    horizontal = vertices[:, [0, 2]] - origin[[0, 2]]
    covariance = horizontal.T @ horizontal
    _, vectors = np.linalg.eigh(covariance)
    forward = np.array((vectors[0, -1], 0.0, vectors[1, -1]))
    if forward[2] < 0 or (abs(forward[2]) < 1e-8 and forward[0] < 0):
        forward *= -1
    forward /= np.linalg.norm(forward)
    up = np.array((0.0, 1.0, 0.0))
    right = np.cross(up, forward)
    matrix = np.stack((right, up, forward), axis=1)
    return origin, matrix


def unity_euler(matrix: np.ndarray) -> list[float]:
    # Unity Quaternion.Euler applies Z, then X, then Y. scipy's extrinsic zxy
    # sequence gives that same matrix; convert its [z,x,y] output to [x,y,z].
    zxy = Rotation.from_matrix(matrix).as_euler("zxy", degrees=True)
    return [float(zxy[1]), float(zxy[2]), float(zxy[0])]


def axes_for_triangle(t: np.ndarray) -> np.ndarray:
    e = np.stack((t[1] - t[0], t[2] - t[1], t[0] - t[2]))
    axes = [np.array((1.0, 0.0, 0.0)), np.array((0.0, 1.0, 0.0)), np.array((0.0, 0.0, 1.0)), np.cross(e[0], e[1])]
    for edge in e:
        for unit in np.eye(3):
            axes.append(np.cross(edge, unit))
    return np.asarray([axis for axis in axes if np.dot(axis, axis) > 1e-18])


def raster_triangle(t: np.ndarray, limits: Limits) -> np.ndarray:
    lo = np.ceil(t.min(axis=0) - 0.5 - 1e-8).astype(np.int64)
    hi = np.floor(t.max(axis=0) + 0.5 + 1e-8).astype(np.int64)
    width = hi - lo + 1
    tests = math.prod(int(v) for v in width)
    if tests <= 0:
        return np.empty((0, 3), dtype=np.int32)
    if tests > limits.max_triangle_tests:
        raise RuntimeError(f"One triangle needs {tests:,} cell tests, limit {limits.max_triangle_tests:,}; exact 1m conversion is not affordable")
    axes = axes_for_triangle(t)
    projected = t @ axes.T
    lower = projected.min(axis=0) - 0.5 * np.abs(axes).sum(axis=1) - 1e-9
    upper = projected.max(axis=0) + 0.5 * np.abs(axes).sum(axis=1) + 1e-9
    normal = np.cross(t[1] - t[0], t[2] - t[0])
    normal_len = np.linalg.norm(normal)
    output = []
    yz = int(width[1] * width[2])
    for start in range(0, tests, limits.chunk_tests):
        ids = np.arange(start, min(tests, start + limits.chunk_tests), dtype=np.int64)
        centers = np.stack((ids // yz + lo[0], ids // int(width[2]) % int(width[1]) + lo[1], ids % int(width[2]) + lo[2]), axis=1)
        proj = centers @ axes.T
        hit = np.all((proj >= lower) & (proj <= upper), axis=1)
        if normal_len > 1e-12 and np.any(hit):
            # A triangle exactly on a voxel face belongs to the solid side of
            # that face. This avoids doubling axis-aligned deck thickness.
            signed = (centers[hit] - t[0]) @ normal
            radius = 0.5 * np.abs(normal).sum()
            boundary_outside = (signed > 0) & (np.abs(signed - radius) < 1e-7 * normal_len)
            index = np.flatnonzero(hit)
            hit[index[boundary_outside]] = False
        if np.any(hit):
            output.append(centers[hit].astype(np.int32))
    return np.concatenate(output) if output else np.empty((0, 3), dtype=np.int32)


def mesh_watertight(triangles: np.ndarray) -> bool:
    if len(triangles) == 0:
        return False
    edges = np.concatenate((triangles[:, [0, 1]], triangles[:, [1, 2]], triangles[:, [2, 0]]))
    edges.sort(axis=1)
    _, counts = np.unique(edges, axis=0, return_counts=True)
    return bool(np.all(counts == 2))


def raster_mesh(vertices: np.ndarray, triangles: np.ndarray, limits: Limits, fill: bool,
                return_stats: bool = False) -> np.ndarray | tuple[np.ndarray, dict]:
    cells = set()
    running_tests = 0
    for i, indices in enumerate(triangles):
        tri = vertices[indices]
        if np.linalg.norm(np.cross(tri[1] - tri[0], tri[2] - tri[0])) < 1e-10:
            continue
        width = np.floor(tri.max(0) + .5 + 1e-8) - np.ceil(tri.min(0) - .5 - 1e-8) + 1
        running_tests += max(0, math.prod(int(v) for v in width))
        if running_tests > limits.max_triangle_tests:
            raise RuntimeError(f"Mesh exceeds {limits.max_triangle_tests:,} triangle-cell tests at triangle {i}; no cells written")
        for c in raster_triangle(tri, limits):
            cells.add(tuple(int(v) for v in c))
        if len(cells) > limits.max_object_cells:
            raise RuntimeError(f"Mesh exceeds {limits.max_object_cells:,} cells; no cells written")
    if not cells:
        empty = np.empty((0, 3), dtype=np.int32)
        return (empty, {"surfaceCells": 0, "interiorCells": 0}) if return_stats else empty
    result = np.asarray(sorted(cells), dtype=np.int32)
    surface_count = len(result)
    if fill and mesh_watertight(triangles):
        lo = result.min(0) - 1
        hi = result.max(0) + 1
        size = hi - lo + 1
        volume = math.prod(int(v) for v in size)
        if volume > limits.max_fill_grid:
            raise RuntimeError(f"Interior fill needs {volume:,} grid cells, above {limits.max_fill_grid:,}; use explicit --no-fill or a larger budget")
        mask = np.zeros(tuple(size), dtype=bool)
        shifted = result - lo
        mask[shifted[:, 0], shifted[:, 1], shifted[:, 2]] = True
        filled = ndimage.binary_fill_holes(mask)
        result = np.argwhere(filled).astype(np.int32) + lo
        if len(result) > limits.max_object_cells:
            raise RuntimeError(f"Filled mesh exceeds {limits.max_object_cells:,} cells; no cells written")
    if return_stats:
        return result, {"surfaceCells": surface_count, "interiorCells": len(result) - surface_count}
    return result


def connected_components(cells: np.ndarray) -> list[np.ndarray]:
    if len(cells) == 0:
        return []
    remaining = {tuple(int(v) for v in c) for c in cells}
    components = []
    while remaining:
        seed = remaining.pop()
        queue = deque((seed,))
        component = [seed]
        while queue:
            c = queue.popleft()
            for offset in NEIGHBORS:
                neighbor = (c[0] + int(offset[0]), c[1] + int(offset[1]), c[2] + int(offset[2]))
                if neighbor in remaining:
                    remaining.remove(neighbor)
                    queue.append(neighbor)
                    component.append(neighbor)
        components.append(np.asarray(component, dtype=np.int32))
    return sorted(components, key=lambda c: -len(c))


def pack_block8(local: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
    """Return per-cell shape ID and modelOffset; all eight physical cells remain."""
    positions = {tuple(int(v) for v in c): i for i, c in enumerate(local)}
    shape = np.zeros(len(local), dtype=np.uint8)
    offset = np.zeros((len(local), 3), dtype=np.int8)
    offsets = np.array([(x, y, z) for x in (0, 1) for y in (0, 1) for z in (0, 1)], dtype=np.int32)
    # Fixed phases give predictable groups and avoid greedy checkerboards.
    counts = []
    for phase in offsets:
        anchors = [c for c in local if np.all((c & 1) == phase)]
        counts.append(sum(all(tuple(int(v) for v in c + o) in positions for o in offsets) for c in anchors))
    phase = offsets[int(np.argmax(counts))]
    for anchor in local:
        if not np.all((anchor & 1) == phase):
            continue
        ids = [positions.get(tuple(int(v) for v in anchor + o)) for o in offsets]
        if any(i is None or shape[i] != 0 for i in ids):
            continue
        for i, o in zip(ids, offsets):
            shape[i] = 13  # Block8 enum value
            offset[i] = o
    return shape, offset


def hull_lookup(template_path: Path) -> tuple[list[dict], np.ndarray, np.ndarray, list[dict]]:
    """Fit exact native hull volumes to quantized source tangent halfspaces."""
    template = json.loads(template_path.read_text(encoding="utf-8"))
    single = [part for part in template["parts"] if len(part["occupied"]) == 1]
    rotations = template["rotations"]
    axes = np.linspace(-.5 + .5 / 9, .5 - .5 / 9, 9)
    samples = np.stack(np.meshgrid(axes, axes, axes, indexing="ij"), axis=-1).reshape(-1, 3)
    candidates = []
    masks = []
    for part in single:
        vertices = np.asarray(part["vertices"], dtype=np.float64)
        for rotation in rotations:
            matrix = np.asarray(rotation["matrix"], dtype=np.float64).reshape(3, 3)
            hull = ConvexHull(vertices @ matrix.T)
            equation = hull.equations
            mask = np.all(samples @ equation[:, :3].T + equation[:, 3] <= 1e-7, axis=1)
            candidates.append({"shape": int(part["id"]), "name": part["shape"], "turns": rotation["turns"]})
            masks.append(mask)
    masks = np.stack(masks)
    directions = np.asarray([(x, y, z) for x in (-1, 0, 1) for y in (-1, 0, 1) for z in (-1, 0, 1)
                             if x or y or z], dtype=np.float64)
    directions /= np.linalg.norm(directions, axis=1)[:, None]
    offsets = np.linspace(-.45, .45, 10)
    lookup = np.zeros((len(directions), len(offsets)), dtype=np.int16)
    for i, direction in enumerate(directions):
        proj = samples @ direction
        for j, offset in enumerate(offsets):
            target = proj <= offset
            false_solid = np.count_nonzero(masks & ~target, axis=1)
            false_empty = np.count_nonzero(~masks & target, axis=1)
            # Filling a source void closes openings; charge that more heavily.
            score = false_solid * 1.25 + false_empty
            lookup[i, j] = int(np.argmin(score))
    return candidates, directions, lookup, rotations


def source_vertex_normals(vertices: np.ndarray, triangles: np.ndarray) -> np.ndarray:
    face = np.cross(vertices[triangles[:, 1]] - vertices[triangles[:, 0]],
                    vertices[triangles[:, 2]] - vertices[triangles[:, 0]])
    volume6 = np.einsum("ij,ij->", vertices[triangles[:, 0]], face)
    if volume6 < 0:
        face *= -1
    normals = np.zeros_like(vertices)
    for corner in range(3):
        np.add.at(normals, triangles[:, corner], face)
    length = np.linalg.norm(normals, axis=1)
    normals[length > 1e-12] /= length[length > 1e-12, None]
    return normals


def fit_single_cell_hulls(cells: np.ndarray, shape: np.ndarray, turns: np.ndarray,
                          vertices: np.ndarray, triangles: np.ndarray,
                          hull_fit: tuple[list[dict], np.ndarray, np.ndarray]) -> None:
    candidates, directions, lookup, _ = hull_fit
    occupied = {tuple(int(v) for v in cell) for cell in cells}
    boundary = []
    for index, cell in enumerate(cells):
        if shape[index] != 0:
            continue
        if any(tuple(int(v) for v in cell + offset) not in occupied for offset in NEIGHBORS):
            boundary.append(index)
    if not boundary:
        return
    boundary = np.asarray(boundary, dtype=np.int32)
    normals = source_vertex_normals(vertices, triangles)
    tree = cKDTree(vertices)
    _, nearest = tree.query(cells[boundary], workers=-1)
    source_normal = normals[nearest]
    normal_length = np.linalg.norm(source_normal, axis=1)
    valid = normal_length > 1e-8
    if not np.any(valid):
        return
    selected = boundary[valid]
    source_normal = source_normal[valid]
    nearest_vertex = vertices[nearest[valid]]
    direction_id = np.argmax(source_normal @ directions.T, axis=1)
    offset = np.einsum("ij,ij->i", nearest_vertex - cells[selected], source_normal)
    offset_id = np.clip(np.rint((offset + .45) / .1).astype(np.int32), 0, 9)
    fit = lookup[direction_id, offset_id]
    for candidate_id in np.unique(fit):
        cell_ids = selected[fit == candidate_id]
        candidate = candidates[int(candidate_id)]
        shape[cell_ids] = candidate["shape"]
        turns[cell_ids] = candidate["turns"]


def pack_block7(local: np.ndarray, shape: np.ndarray, turns: np.ndarray, offset: np.ndarray,
                hull_fit: tuple) -> None:
    """Join adjacent native Slabs into a physically complete two-cell Block7."""
    rotations = hull_fit[3]
    by_turns = {tuple(rotation["turns"]): np.asarray(rotation["matrix"], dtype=np.int8).reshape(3, 3)
                for rotation in rotations}
    # A Block7 is a long half-height slab. Both source cells must have selected
    # Slab and the same empty-side direction; no arbitrary neighbours are filled.
    lookup = {}
    for rotation in rotations:
        matrix = np.asarray(rotation["matrix"], dtype=np.int8).reshape(3, 3)
        lookup[(tuple(int(v) for v in matrix[:, 1]), tuple(int(v) for v in matrix[:, 2]))] = rotation["turns"]
    positions = {tuple(int(v) for v in c): i for i, c in enumerate(local)}
    used = set()
    for i, cell in enumerate(local):
        if shape[i] != 4 or i in used:
            continue
        matrix = by_turns[tuple(int(v) for v in turns[i])]
        normal = tuple(int(v) for v in matrix[:, 1])
        for direction in ((1, 0, 0), (0, 1, 0), (0, 0, 1)):
            if np.dot(normal, direction):
                continue
            j = positions.get(tuple(int(v) for v in cell + direction))
            if j is None or shape[j] != 4 or j in used:
                continue
            other_matrix = by_turns[tuple(int(v) for v in turns[j])]
            if tuple(int(v) for v in other_matrix[:, 1]) != normal:
                continue
            pose = lookup.get((normal, direction))
            if pose is None:
                continue
            shape[i] = shape[j] = 12
            turns[i] = turns[j] = pose
            offset[i] = (0, 0, 0)
            offset[j] = (0, 0, 1)
            used.add(i)
            used.add(j)
            break


def validate_multi_cell_models(local: np.ndarray, shape: np.ndarray, turns: np.ndarray,
                               offset: np.ndarray, hull_fit: tuple | None) -> None:
    if len({tuple(int(v) for v in c) for c in local}) != len(local):
        raise ValueError("Duplicate voxel positions")
    matrices = {(0, 0, 0): np.eye(3, dtype=np.int32)}
    if hull_fit is not None:
        matrices.update({tuple(rotation["turns"]): np.asarray(rotation["matrix"], dtype=np.int32).reshape(3, 3)
                         for rotation in hull_fit[3]})
    groups = {}
    for cell, kind, pose, model_offset in zip(local, shape, turns, offset):
        kind = int(kind)
        model_offset = tuple(int(v) for v in model_offset)
        if kind not in (12, 13):
            if model_offset != (0, 0, 0):
                raise ValueError("Single-cell hull has a nonzero modelOffset")
            continue
        rotation = matrices[tuple(int(v) for v in pose)]
        anchor = tuple(int(v) for v in cell - rotation @ np.asarray(model_offset, dtype=np.int32))
        key = (kind, tuple(int(v) for v in pose), anchor)
        groups.setdefault(key, set()).add(model_offset)
    block7_offsets = {(0, 0, 0), (0, 0, 1)}
    block8_offsets = {(x, y, z) for x in (0, 1) for y in (0, 1) for z in (0, 1)}
    for (kind, _, anchor), present in groups.items():
        expected = block7_offsets if kind == 12 else block8_offsets
        if present != expected:
            raise ValueError(f"Incomplete {kind} model at {anchor}: {sorted(present)}")


def export_asset(out_dir: Path, asset_id: str, cells: np.ndarray, source: dict, cell_size_m: float,
                 vertices: np.ndarray | None = None, triangles: np.ndarray | None = None,
                 hull_fit: tuple | None = None, json_rows: bool = False,
                 frame_origin: np.ndarray | None = None, frame_matrix: np.ndarray | None = None) -> dict:
    if len(cells) > MAX_ASSET_CELLS:
        raise RuntimeError(f"{asset_id} has {len(cells):,} cells, above Unity's {MAX_ASSET_CELLS:,} per-asset limit")
    anchor = np.rint((cells.min(0) + cells.max(0)) * 0.5).astype(np.int32)
    local = cells - anchor
    shape, offset = pack_block8(local)
    turns = np.zeros((len(local), 3), dtype=np.uint8)
    if hull_fit is not None and vertices is not None and triangles is not None:
        fit_single_cell_hulls(cells, shape, turns, vertices, triangles, hull_fit)
        pack_block7(local, shape, turns, offset, hull_fit)
    # Canonical order makes prototypes and checksums reproducible.
    order = np.lexsort((local[:, 2], local[:, 1], local[:, 0]))
    local, shape, offset, turns = local[order], shape[order], offset[order], turns[order]
    validate_multi_cell_models(local, shape, turns, offset, hull_fit)
    file_name = asset_id + ".npz"
    np.savez_compressed(out_dir / file_name, position=local, shape=shape, modelOffset=offset,
                        quarterTurns=turns, surface=np.zeros(len(local), dtype=np.uint8))
    json_file = None
    if json_rows:
        json_file = asset_id + ".json"
        rows = np.concatenate((local, shape[:, None], turns, offset, np.zeros((len(local), 1), dtype=np.int32)), axis=1)
        (out_dir / json_file).write_text(json.dumps({"assetKey": asset_id, "cells": rows.tolist()}, separators=(",", ":")),
                                         encoding="utf-8")
    shape_counts = Counter(int(v) for v in shape)
    names = {0: "Cube", 1: "Wedge", 4: "Slab", 5: "Pyramid", 10: "Block5", 11: "Block6", 12: "Block7", 13: "Block8"}
    if frame_matrix is None:
        frame_matrix = np.eye(3)
    if frame_origin is None:
        frame_origin = np.zeros(3)
    world_position = (frame_origin + frame_matrix @ anchor) * cell_size_m
    return {"id": asset_id, "npz": file_name, "json": json_file, "source": source, "cellSize": cell_size_m,
            "position": world_position.tolist(), "eulerAngles": unity_euler(frame_matrix),
            "rotationMatrix": frame_matrix.reshape(-1).tolist(), "scale": 1,
            "driftVelocity": [0, 0, 0], "angularVelocity": [0, 0, 0],
            "cells": len(local), "boundsCells": [local.min(0).tolist(), local.max(0).tolist()],
            "shapeCounts": {names[k]: v for k, v in sorted(shape_counts.items())},
            "connectedComponents": 1}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("manifest", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--long-axis-m", type=float, default=3500)
    parser.add_argument("--cell-size-m", type=float, default=1)
    parser.add_argument("--origin", type=float, nargs=3, help="Source-space origin; default scene bounds centre")
    parser.add_argument("--max-triangle-tests", type=int, default=8_000_000)
    parser.add_argument("--max-object-cells", type=int, default=1_000_000)
    parser.add_argument("--max-total-cells", type=int, default=3_000_000)
    parser.add_argument("--preflight", action="store_true", help="Only report calibrated bounds and estimated raster work")
    parser.add_argument("--only-names", nargs="*", help="Process just these exact source object names")
    parser.add_argument("--exclude-collection", action="append", default=[], help="Exclude source prototype collections")
    parser.add_argument("--only-collection", help="Process only source objects in this collection")
    parser.add_argument("--unity-basis", action="store_true", help="Blender (x,y,z) to Unity (x,z,-y)")
    parser.add_argument("--calibration-json", type=Path, help="Reuse calibration from a scene preflight/geometry manifest")
    parser.add_argument("--source-shells", action="store_true", help="Keep indexed triangle shells separate before voxelization")
    parser.add_argument("--only-shell-ids", nargs="*", type=int, help="Select indexed shell IDs in each chosen source object")
    parser.add_argument("--hull-templates", type=Path, help="Fit each boundary cell to exact native 24-pose hull solids")
    parser.add_argument("--closed-shells-only", action="store_true", help="Exclude open decoration shells from physical assets")
    parser.add_argument("--json-rows", action="store_true", help="Also write Unity import-ready per-asset JSON cell rows")
    parser.add_argument("--merge-selected-shells", action="store_true", help="Combine selected source shells before six-connectivity split")
    parser.add_argument("--horizontal-frame", action="store_true", help="Voxelize each selected group in its source-derived horizontal frame")
    parser.add_argument("--local-frames-json", type=Path, help="Validated source-local frames from the calibration export")
    parser.add_argument("--local-frame-key", help="Name of one frame in --local-frames-json")
    parser.add_argument("--no-fill", action="store_true", help="Keep only source-intersecting surface cells, including for closed meshes")
    parser.add_argument("--count-only", action="store_true", help="Rasterize and count every selected shell without writing cell assets")
    parser.add_argument("--asset-prefix", default="ref", help="Stable prefix that names the source batch and avoids cross-batch key collisions")
    args = parser.parse_args()
    manifest = json.loads(args.manifest.read_text(encoding="utf-8"))
    objects = source_objects(manifest)
    if args.only_names is not None:
        requested = set(args.only_names)
        objects = [item for item in objects if item.get("name") in requested]
        missing = requested - {item.get("name") for item in objects}
        if missing:
            raise ValueError(f"Source objects not found: {sorted(missing)}")
    objects = [item for item in objects if not set(item.get("collections", [])) & set(args.exclude_collection)]
    if args.only_collection:
        objects = [item for item in objects if args.only_collection in item.get("collections", [])]
    if not objects:
        raise ValueError("No source objects selected")
    bounds = np.array([[np.inf] * 3, [-np.inf] * 3], dtype=np.float64)
    loaded = []
    for item in objects:
        v, t = object_arrays(item, args.manifest.parent)
        v = world_vertices(v, item)
        if args.unity_basis:
            v = unity_basis(v)
        bounds[0] = np.minimum(bounds[0], v.min(0))
        bounds[1] = np.maximum(bounds[1], v.max(0))
        loaded.append((item, v, t))
    if args.calibration_json:
        prior = json.loads(args.calibration_json.read_text(encoding="utf-8"))
        if "calibration" in prior:
            calibration = prior["calibration"]
        else:
            matrix = np.asarray(prior["sourceWorldToCanonicalUnityMatrixRows"], dtype=np.float64)
            scale = float(prior["metresPerSourceUnit"])
            calibration = {"scale": scale, "source_origin": (-matrix[:3, 3] / scale).tolist(),
                           "cell_size_m": args.cell_size_m, "source_bounds": bounds.tolist(),
                           "world_spans_m": prior["canonicalUnityExtentMetres"], "long_axis_m": 3500}
        if abs(calibration["cell_size_m"] - args.cell_size_m) > 1e-9:
            raise ValueError("--cell-size-m must match the reused calibration")
    else:
        calibration = grid_transform(bounds, args.long_axis_m, args.cell_size_m,
                                     np.asarray(args.origin) if args.origin else None)
    summary = {"calibration": calibration, "sourceObjects": len(loaded), "objects": []}
    limits = Limits(max_triangle_tests=args.max_triangle_tests, max_object_cells=args.max_object_cells,
                    max_total_cells=args.max_total_cells)
    hull_fit = hull_lookup(args.hull_templates) if args.hull_templates and not args.preflight else None
    chosen_frame = None
    if args.local_frame_key:
        if not args.local_frames_json:
            raise ValueError("--local-frame-key needs --local-frames-json")
        chosen_frame = json.loads(args.local_frames_json.read_text(encoding="utf-8"))[args.local_frame_key]
    topology = None
    if args.closed_shells_only:
        topology_file = args.manifest.parent / "source-topology.json"
        topology = {o["name"]: {int(c["id"]) for c in o["components"] if c["watertight"]}
                    for o in json.loads(topology_file.read_text(encoding="utf-8"))["objects"]}
    for item, v, t in loaded:
        gv = calibrated(v, calibration)
        tri = gv[t]
        widths = np.floor(tri.max(axis=1) + .5 + 1e-8) - np.ceil(tri.min(axis=1) - .5 - 1e-8) + 1
        estimate = int(np.maximum(0, np.prod(widths, axis=1)).sum())
        entry = {"name": item.get("name", "unnamed"), "triangles": len(t), "boundsCells": [gv.min(0).tolist(), gv.max(0).tolist()],
                 "triangleCellTestsEstimate": estimate, "maxSingleTriangleTests": int(np.prod(widths, axis=1).max(initial=0))}
        summary["objects"].append(entry)
    if args.preflight:
        summary["triangleCellTestsEstimate"] = sum(x["triangleCellTestsEstimate"] for x in summary["objects"])
        print(json.dumps(summary, indent=2))
        return 0
    args.output.mkdir(parents=True, exist_ok=True)
    assets = []
    total = 0
    for index, (item, v, t) in enumerate(loaded):
        name = item.get("name", f"object{index}")
        try:
            if args.source_shells:
                stem = Path(item["npz"]).stem
                labels_file = args.manifest.parent / (stem + "-components.npz")
                with np.load(labels_file) as labels_data:
                    labels = np.asarray(labels_data["triangle_component"], dtype=np.int32)
                if len(labels) != len(t):
                    raise ValueError(f"Source shell labels do not match triangle count for {name}")
                source_groups = [(int(label), t[labels == label]) for label in np.unique(labels)]
                if topology is not None:
                    allowed = topology.get(name, set())
                    source_groups = [(shell, tri) for shell, tri in source_groups if shell in allowed]
                if args.only_shell_ids is not None:
                    requested_shells = set(args.only_shell_ids)
                    source_groups = [(shell, tri) for shell, tri in source_groups if shell in requested_shells]
                    missing_shells = requested_shells - {shell for shell, _ in source_groups}
                    if missing_shells:
                        raise ValueError(f"Source shell IDs not found for {name}: {sorted(missing_shells)}")
                if args.merge_selected_shells and source_groups:
                    source_groups = [(-1, np.concatenate([tri for _, tri in source_groups]))]
            else:
                source_groups = [(0, t)]
            cell_count = 0
            component_sizes = []
            shell_counts = []
            gv = calibrated(v, calibration)
            for shell_id, shell_triangles in source_groups:
                unique, inverse = np.unique(shell_triangles, return_inverse=True)
                shell_vertices = gv[unique]
                shell_triangles = inverse.reshape(-1, 3).astype(np.int32)
                if chosen_frame is not None:
                    frame_origin = np.asarray(chosen_frame["canonicalUnityOriginMetres"], dtype=np.float64) / args.cell_size_m
                    frame_matrix = np.asarray(chosen_frame["canonicalUnityRotationRows"], dtype=np.float64)
                    if not np.allclose(frame_matrix.T @ frame_matrix, np.eye(3), atol=1e-5):
                        raise ValueError("Chosen frame is not orthonormal")
                else:
                    frame_origin, frame_matrix = horizontal_frame(shell_vertices) if args.horizontal_frame else (np.zeros(3), np.eye(3))
                local_vertices = (shell_vertices - frame_origin) @ frame_matrix
                try:
                    cells = raster_mesh(local_vertices, shell_triangles, limits, fill=not args.no_fill)
                except RuntimeError as error:
                    if not args.count_only:
                        raise
                    shell_counts.append({"sourceShell": shell_id, "error": str(error)})
                    continue
                cell_count += len(cells)
                components = connected_components(cells)
                shell_counts.append({"sourceShell": shell_id, "cells": len(cells),
                                     "components": [len(part) for part in components]})
                for part_index, component in enumerate(components):
                    total += len(component)
                    if total > limits.max_total_cells and not args.count_only:
                        raise RuntimeError(f"Scene exceeds {limits.max_total_cells:,} total cells")
                    if args.count_only:
                        component_sizes.append(len(component))
                        continue
                    asset_id = f"{args.asset_prefix}_{index:04d}_{shell_id:04d}_{part_index:02d}"
                    assets.append(export_asset(args.output, asset_id, component, {"object": name, "objectIndex": index,
                                                                                   "sourceShell": shell_id, "componentIndex": part_index}, args.cell_size_m,
                                               local_vertices, shell_triangles, hull_fit, args.json_rows,
                                               frame_origin, frame_matrix))
                    component_sizes.append(len(component))
            summary["objects"][index]["cells"] = cell_count
            summary["objects"][index]["sourceShells"] = len(source_groups)
            summary["objects"][index]["components"] = sorted(component_sizes, reverse=True)
            summary["objects"][index]["shellCounts"] = shell_counts
        except RuntimeError as error:
            summary["objects"][index]["error"] = str(error)
            print(f"{name}: {error}", file=sys.stderr)
    summary["assets"] = assets
    summary["totalCells"] = total
    summary["errors"] = sum("error" in obj for obj in summary["objects"])
    summary["shellErrors"] = sum("error" in shell for obj in summary["objects"] for shell in obj.get("shellCounts", []))
    (args.output / "geometry-manifest.json").write_text(json.dumps(summary, indent=2), encoding="utf-8")
    print(f"{len(assets)} exported assets, {total:,} cells, {summary['errors']} object errors, {summary['shellErrors']} shell errors")
    return 0 if summary["errors"] == 0 and summary["shellErrors"] == 0 else 2


if __name__ == "__main__":
    raise SystemExit(main())
