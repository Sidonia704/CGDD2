"""Read-only Blender scene inventory for the reference-map reconstruction.

Run: blender --background cggd.blend --python SourceInspect.py -- OUTPUT.json
Never saves or mutates the source blend.
"""

import json
import math
import sys
from collections import Counter
from pathlib import Path

import bpy
from mathutils import Vector


def point(v):
    return [float(v.x), float(v.y), float(v.z)]


def matrix(m):
    return [[float(x) for x in row] for row in m]


def bounds_world(obj):
    if obj.type != "MESH" or not obj.bound_box:
        return None
    corners = [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
    return {
        "min": [min(p[i] for p in corners) for i in range(3)],
        "max": [max(p[i] for p in corners) for i in range(3)],
    }


output = Path(sys.argv[sys.argv.index("--") + 1])
scene = bpy.context.scene
objects = []
for obj in bpy.data.objects:
    item = {
        "name": obj.name,
        "type": obj.type,
        "data": obj.data.name if obj.data else None,
        "parent": obj.parent.name if obj.parent else None,
        "collections": [x.name for x in obj.users_collection],
        "sceneMember": obj.name in scene.objects,
        "hideRender": bool(obj.hide_render),
        "hideViewport": bool(obj.hide_viewport),
        "location": point(obj.location),
        "rotationEuler": [float(x) for x in obj.rotation_euler],
        "scale": point(obj.scale),
        "matrixWorld": matrix(obj.matrix_world),
        "dimensions": point(obj.dimensions),
        "worldBounds": bounds_world(obj),
        "instanceType": obj.instance_type,
        "instanceCollection": obj.instance_collection.name if obj.instance_collection else None,
    }
    if obj.type == "MESH":
        item.update({
            "vertices": len(obj.data.vertices),
            "polygons": len(obj.data.polygons),
            "materials": [x.name if x else None for x in obj.data.materials],
            "modifiers": [{"name": m.name, "type": m.type} for m in obj.modifiers],
        })
    elif obj.type == "CAMERA":
        item.update({
            "lens": float(obj.data.lens),
            "viewForward": point(obj.matrix_world.to_quaternion() @ Vector((0, 0, -1))),
            "viewUp": point(obj.matrix_world.to_quaternion() @ Vector((0, 1, 0))),
        })
    objects.append(item)

instances = []
for inst in bpy.context.evaluated_depsgraph_get().object_instances:
    if inst.is_instance:
        instances.append({
            "object": inst.object.original.name,
            "parent": inst.parent.original.name if inst.parent else None,
            "matrixWorld": matrix(inst.matrix_world),
            "persistentId": list(inst.persistent_id),
        })

collections = [{
    "name": col.name,
    "parents": [p.name for p in bpy.data.collections if col.name in p.children],
    "objects": [o.name for o in col.objects],
    "hideRender": bool(col.hide_render),
    "hideViewport": bool(col.hide_viewport),
} for col in bpy.data.collections]

saved_viewports = []
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type != "VIEW_3D":
            continue
        region = area.spaces.active.region_3d
        saved_viewports.append({
            "screen": screen.name,
            "viewPerspective": region.view_perspective,
            "viewLocation": point(region.view_location),
            "viewRotationQuaternion": [float(x) for x in region.view_rotation],
            "viewDistance": float(region.view_distance),
        })

result = {
    "blend": bpy.data.filepath,
    "scene": scene.name,
    "sceneCamera": scene.camera.name if scene.camera else None,
    "unitSettings": {"system": scene.unit_settings.system, "scaleLength": scene.unit_settings.scale_length},
    "objectCounts": dict(Counter(x["type"] for x in objects)),
    "instanceCount": len(instances),
    "objects": objects,
    "instances": instances,
    "collections": collections,
    "savedViewports": saved_viewports,
}
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text(json.dumps(result, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
print(f"SOURCE_INSPECT: {len(objects)} objects, {len(instances)} instances -> {output}", flush=True)
