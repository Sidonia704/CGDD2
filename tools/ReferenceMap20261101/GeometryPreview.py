"""Three-view source/voxel silhouette overlay for a selected reference landmark."""

from __future__ import annotations

import argparse
import json
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw
from skimage.draw import polygon

from Geometry import object_arrays, world_vertices, unity_basis, calibrated


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source_manifest", type=Path)
    parser.add_argument("geometry_manifest", type=Path)
    parser.add_argument("source_name")
    parser.add_argument("--shells", nargs="+", type=int, required=True)
    parser.add_argument("--frame-json", type=Path)
    parser.add_argument("--frame-key")
    parser.add_argument("--png", type=Path, required=True)
    args = parser.parse_args()
    source = json.loads(args.source_manifest.read_text(encoding="utf-8"))
    geometry = json.loads(args.geometry_manifest.read_text(encoding="utf-8"))
    obj = next(o for o in source["objects"] if o["name"] == args.source_name)
    vertices, triangles = object_arrays(obj, args.source_manifest.parent)
    vertices = calibrated(unity_basis(world_vertices(vertices, obj)), geometry["calibration"])
    with np.load(args.source_manifest.parent / (Path(obj["npz"]).stem + "-components.npz")) as labels_data:
        labels = labels_data["triangle_component"]
    triangles = triangles[np.isin(labels, args.shells)]
    unique, inverse = np.unique(triangles, return_inverse=True)
    vertices = vertices[unique]
    triangles = inverse.reshape(-1, 3)
    if args.frame_key:
        frame = json.loads(args.frame_json.read_text(encoding="utf-8"))[args.frame_key]
        origin = np.asarray(frame["canonicalUnityOriginMetres"], dtype=np.float64)
        matrix = np.asarray(frame["canonicalUnityRotationRows"], dtype=np.float64)
    else:
        origin = np.zeros(3)
        matrix = np.eye(3)
    source_local = (vertices - origin) @ matrix
    all_cells = []
    for asset in geometry["assets"]:
        with np.load(args.geometry_manifest.parent / asset["npz"]) as data:
            local = data["position"].astype(np.float64)
        transform = np.asarray(asset["rotationMatrix"]).reshape(3, 3)
        world = local @ transform.T + np.asarray(asset["position"])
        all_cells.append((world - origin) @ matrix)
    voxel_local = np.concatenate(all_cells)
    views = [("TOP XZ", (0, 2)), ("FRONT XY", (0, 1)), ("SIDE ZY", (2, 1))]
    panels = []
    stats = {}
    for title, (u, v) in views:
        lo = np.floor(np.minimum(source_local[:, [u, v]].min(0), voxel_local[:, [u, v]].min(0)) - 2).astype(int)
        hi = np.ceil(np.maximum(source_local[:, [u, v]].max(0), voxel_local[:, [u, v]].max(0)) + 2).astype(int)
        size = hi - lo + 1
        if np.prod(size) > 4_000_000:
            raise RuntimeError(f"Projection {title} too large: {size}")
        source_mask = np.zeros((size[1], size[0]), dtype=bool)
        for tri in triangles:
            projected = source_local[tri][:, [u, v]] - lo
            rows, cols = polygon(projected[:, 1], projected[:, 0], shape=source_mask.shape)
            source_mask[rows, cols] = True
        voxel_mask = np.zeros_like(source_mask)
        projected_voxels = np.rint(voxel_local[:, [u, v]] - lo).astype(int)
        voxel_mask[projected_voxels[:, 1], projected_voxels[:, 0]] = True
        overlap = source_mask & voxel_mask
        union = source_mask | voxel_mask
        stats[title] = {"sourcePixels": int(source_mask.sum()), "voxelPixels": int(voxel_mask.sum()),
                        "intersection": int(overlap.sum()), "union": int(union.sum()),
                        "silhouetteIoU": round(float(overlap.sum() / max(1, union.sum())), 4)}
        rgb = np.full((*source_mask.shape, 3), (25, 30, 38), dtype=np.uint8)
        rgb[source_mask & ~voxel_mask] = (255, 105, 87)  # source-only
        rgb[voxel_mask & ~source_mask] = (78, 190, 239)  # voxel-only
        rgb[overlap] = (226, 223, 202)
        panel = Image.fromarray(rgb).transpose(Image.Transpose.FLIP_TOP_BOTTOM)
        scale = min(4, max(1, int(550 / max(panel.size))))
        panel = panel.resize((panel.width * scale, panel.height * scale), resample=Image.Resampling.NEAREST)
        panels.append((title, panel))
    width = max(image.width for _, image in panels) + 32
    height = sum(image.height + 32 for _, image in panels) + 16
    canvas = Image.new("RGB", (width, height), (17, 21, 28))
    draw = ImageDraw.Draw(canvas)
    y = 12
    for title, panel in panels:
        draw.text((12, y), f"{title}  IoU {stats[title]['silhouetteIoU']:.3f}   source red / vox blue / overlap cream", fill=(240, 240, 240))
        y += 20
        canvas.paste(panel, (12, y))
        y += panel.height + 12
    args.png.parent.mkdir(parents=True, exist_ok=True)
    canvas.save(args.png)
    args.png.with_suffix(".json").write_text(json.dumps(stats, indent=2), encoding="utf-8")
    print(json.dumps(stats))


if __name__ == "__main__":
    main()
