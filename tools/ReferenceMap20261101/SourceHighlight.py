"""Render selected station shell IDs in red over the source station.

Run: blender -b --factory-startup cggd.blend -P SourceHighlight.py -- MESH_DIR OUTPUT_DIR
This only creates temporary scene objects in memory; the blend is never saved.
"""

import json
import sys
from pathlib import Path

import bpy
import numpy as np
from mathutils import Vector


meshdir = Path(sys.argv[sys.argv.index("--") + 1])
outdir = Path(sys.argv[sys.argv.index("--") + 2])
outdir.mkdir(parents=True, exist_ok=True)
manifest = json.loads((meshdir / "source-meshes.json").read_text(encoding="utf-8"))
record = next(x for x in manifest["objects"] if x["name"] == "Cylinder.018")
mesh = np.load(meshdir / record["npz"])
labels = np.load(meshdir / record["npz"].replace(".npz", "-components.npz"))["triangle_component"]
vertices, triangles = mesh["vertices"], mesh["triangles"]
scene = bpy.context.scene
scene.render.engine = "BLENDER_WORKBENCH"
scene.render.resolution_x = 1400
scene.render.resolution_y = 1000
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = "PNG"
scene.display.shading.light = "STUDIO"
scene.display.shading.color_type = "OBJECT"
scene.display.shading.show_cavity = True
scene.display.shading.cavity_type = "BOTH"
scene.display.shading.background_type = "WORLD"
scene.display.shading.background_color = (0.025, 0.033, 0.052)
for obj in bpy.data.objects:
    if obj.type == "MESH" and obj.name != "Cylinder.018":
        obj.hide_render = True
source_obj = bpy.data.objects["Cylinder.018"]
source_obj.color = (0.48, 0.46, 0.35, 1)
camera_data = bpy.data.cameras.new("SourceHighlightCamera")
camera_data.type = "ORTHO"
camera_data.ortho_scale = 8.5
camera = bpy.data.objects.new("SourceHighlightCamera", camera_data)
scene.collection.objects.link(camera)
scene.camera = camera
center = Vector((-0.11, 0.99, 0.94))
groups = [
    ("dome-left", [1]),
    ("dome-right", [128]),
    ("hex-candidates", [570, 571, 572, 573, 574]),
    ("hex-deck", list(range(497, 570)) + list(range(593, 601))),
    ("truss-span", [127]),
    ("long-beams", [470, 471]),
    ("truss-bridge", list(range(40, 128))),
    ("group-000-039", list(range(0, 40))),
    ("group-128-199", list(range(128, 200))),
    ("group-200-284", list(range(200, 285))),
    ("group-285-397", list(range(285, 398))),
    ("group-398-496", list(range(398, 497))),
    ("group-128-149", list(range(128, 150))),
    ("group-150-169", list(range(150, 170))),
    ("group-170-199", list(range(170, 200))),
    ("group-200-229", list(range(200, 230))),
    ("group-230-259", list(range(230, 260))),
    ("group-260-284", list(range(260, 285))),
    ("group-285-339", list(range(285, 340))),
    ("group-340-397", list(range(340, 398))),
    ("group-398-449", list(range(398, 450))),
    ("group-450-496", list(range(450, 497))),
    ("left-open-structure", [0]),
    ("right-open-structure", [134]),
    ("left-upper-equipment", list(range(2, 19))),
    ("left-supports-19-24", list(range(19, 25))),
    ("left-box-grid-25-118", list(range(25, 119))),
    ("left-tanks-119-126", list(range(119, 127))),
    ("right-fittings-129-133", list(range(129, 134))),
    ("right-fixtures-135-199", list(range(135, 200))),
    ("deck-understructure-575-592", list(range(575, 593))),
    ("last-shell-601", [601]),
]
for label, ids in groups:
    chosen = triangles[np.isin(labels, ids)]
    used, remap = np.unique(chosen, return_inverse=True)
    new_mesh = bpy.data.meshes.new("highlight-" + label)
    new_mesh.from_pydata(vertices[used].tolist(), [], remap.reshape(-1, 3).tolist())
    new_mesh.update()
    marker = bpy.data.objects.new("highlight-" + label, new_mesh)
    scene.collection.objects.link(marker)
    marker.matrix_world = source_obj.matrix_world.copy()
    marker.color = (1.0, 0.12, 0.08, 1)
    for view, offset in [("top", Vector((0, 0, 20))),
                         ("oblique", Vector((9, -11, 8)))]:
        camera.location = center + offset
        camera.rotation_euler = (center - camera.location).to_track_quat("-Z", "Y").to_euler()
        # Orthographic projection is unchanged by a view-axis shift, but the
        # selected shell now draws visibly in front of the original surface.
        marker.matrix_world = source_obj.matrix_world.copy()
        marker.matrix_world.translation += offset.normalized() * 10.0
        scene.render.filepath = str(outdir / f"source-highlight-{label}-{view}.png")
        bpy.ops.render.render(write_still=True)
        print("SOURCE_HIGHLIGHT", label, view, ids, flush=True)
    bpy.data.objects.remove(marker, do_unlink=True)
    bpy.data.meshes.remove(new_mesh)
