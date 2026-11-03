"""Export exact local triangles and world transforms without altering cggd.blend.

Run: blender --background cggd.blend --python SourceExtract.py -- OUTPUT_DIRECTORY
Each NPZ has float32 vertices (N,3), int32 triangles (M,3), and int16
material_index (M). source-meshes.json links the files to row-major 4x4
matrix_world values. Apply matrix_world exactly once to get Blender world space.
"""

import json
import re
import sys
from pathlib import Path

import bpy
import numpy as np


def matrix_rows(m):
    return [[float(x) for x in row] for row in m]


def bounds(a):
    if not len(a):
        return None
    return {"min": a.min(axis=0).astype(float).tolist(),
            "max": a.max(axis=0).astype(float).tolist()}


def safe_name(name):
    return re.sub(r"[^A-Za-z0-9._-]", "_", name)[:90]


outdir = Path(sys.argv[sys.argv.index("--") + 1])
outdir.mkdir(parents=True, exist_ok=True)
depsgraph = bpy.context.evaluated_depsgraph_get()
records = []
for number, obj in enumerate(bpy.data.objects):
    if obj.type != "MESH":
        continue
    evaluated = obj.evaluated_get(depsgraph)
    mesh = evaluated.to_mesh(preserve_all_data_layers=False, depsgraph=depsgraph)
    try:
        mesh.calc_loop_triangles()
        vertices = np.empty((len(mesh.vertices), 3), dtype=np.float32)
        mesh.vertices.foreach_get("co", vertices.ravel())
        triangles = np.empty((len(mesh.loop_triangles), 3), dtype=np.int32)
        mesh.loop_triangles.foreach_get("vertices", triangles.ravel())
        material_index = np.empty(len(mesh.loop_triangles), dtype=np.int16)
        mesh.loop_triangles.foreach_get("material_index", material_index)
        filename = f"{number:03d}_{safe_name(obj.name)}.npz"
        np.savez_compressed(outdir / filename, vertices=vertices,
                            triangles=triangles, material_index=material_index)
        world = np.asarray(obj.matrix_world, dtype=np.float64)
        world_vertices = vertices.astype(np.float64) @ world[:3, :3].T + world[:3, 3]
        record = {
            "name": obj.name,
            "data": obj.data.name,
            "parent": obj.parent.name if obj.parent else None,
            "collections": [c.name for c in obj.users_collection],
            "npz": filename,
            "matrix_world": matrix_rows(obj.matrix_world),
            "vertex_count": len(vertices),
            "triangle_count": len(triangles),
            "local_bounds": bounds(vertices),
            "world_bounds_exact": bounds(world_vertices),
            "materials": [m.name if m else None for m in obj.data.materials],
            "source_modifier_types": [m.type for m in obj.modifiers],
            "mesh_evaluated": True,
        }
        records.append(record)
        print(f"SOURCE_EXTRACT: {obj.name}: {len(vertices)} vertices, {len(triangles)} triangles", flush=True)
    finally:
        evaluated.to_mesh_clear()

manifest = {
    "source_blend": bpy.data.filepath,
    "coordinate_system": "Blender right-handed Z-up; mesh local coordinates plus matrix_world",
    "matrix_convention": "column vector, world = matrix_world @ [x,y,z,1]; JSON rows",
    "objects": records,
}
(outdir / "source-meshes.json").write_text(
    json.dumps(manifest, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
print(f"SOURCE_EXTRACT_DONE: {len(records)} mesh objects -> {outdir}", flush=True)
