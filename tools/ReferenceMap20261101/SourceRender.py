"""Render read-only geometric reference views from the Blender scene.

Run: blender --background --factory-startup --disable-autoexec cggd.blend
     --python SourceRender.py -- OUTPUT_DIRECTORY
No blend file is saved.
"""

import sys
from pathlib import Path

import bpy
from mathutils import Vector


outdir = Path(sys.argv[sys.argv.index("--") + 1])
outdir.mkdir(parents=True, exist_ok=True)
scene = bpy.context.scene
scene.render.engine = "BLENDER_WORKBENCH"
scene.render.resolution_x = 1400
scene.render.resolution_y = 1000
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = "PNG"
scene.render.film_transparent = False
scene.display.shading.light = "STUDIO"
scene.display.shading.color_type = "OBJECT"
scene.display.shading.show_shadows = True
scene.display.shading.show_cavity = True
scene.display.shading.cavity_type = "BOTH"
scene.display.shading.background_type = "WORLD"
scene.display.shading.background_color = (0.025, 0.033, 0.052)
scene.render.filepath = str(outdir / "source-preview-top.png")

for obj in bpy.data.objects:
    if obj.name == "asteroids__0.1038" or "scatter" in [c.name for c in obj.users_collection]:
        obj.hide_render = True
        continue
    if obj.name == "Cube.001":
        obj.color = (0.35, 0.57, 0.83, 1.0)
    elif obj.name == "Cylinder.018":
        obj.color = (0.95, 0.67, 0.20, 1.0)
    elif obj.name.startswith("tyFlow"):
        obj.color = (0.89, 0.37, 0.25, 1.0)

camera_data = bpy.data.cameras.new("SourceAuditCamera")
camera_data.type = "ORTHO"
camera = bpy.data.objects.new("SourceAuditCamera", camera_data)
scene.collection.objects.link(camera)
scene.camera = camera

center = Vector((0.0, 0.0, 0.5))
views = [
    ("top", Vector((0.0, 0.0, 40.0)), 30.0),
    ("front", Vector((0.0, -40.0, 0.0)), 24.0),
    ("side", Vector((40.0, 0.0, 0.0)), 24.0),
    ("oblique", Vector((27.0, -30.0, 24.0)), 27.0),
]
for name, offset, ortho_scale in views:
    camera.location = center + offset
    direction = center - camera.location
    camera.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
    camera_data.ortho_scale = ortho_scale
    scene.render.filepath = str(outdir / f"source-preview-{name}.png")
    bpy.ops.render.render(write_still=True)
    print(f"SOURCE_RENDER: {scene.render.filepath}", flush=True)

for obj in bpy.data.objects:
    if obj.type == "MESH" and obj.name != "Cylinder.018":
        obj.hide_render = True
center = Vector((-0.11, 0.99, 0.94))
for name, offset in [
    ("station-top", Vector((0.0, 0.0, 20.0))),
    ("station-oblique", Vector((9.0, -11.0, 8.0))),
]:
    camera.location = center + offset
    camera.rotation_euler = (center - camera.location).to_track_quat("-Z", "Y").to_euler()
    camera_data.ortho_scale = 8.5
    scene.render.filepath = str(outdir / f"source-preview-{name}.png")
    bpy.ops.render.render(write_still=True)
    print(f"SOURCE_RENDER: {scene.render.filepath}", flush=True)
