# Reference source extraction and calibration

`cggd.blend` was read with Blender 4.5.2 in background mode using factory startup. The original `.blend` and four FBXs were not saved or imported again. Their SHA-256 values still match `Documentation/MapAudit-2026-11-01/content-manifest.json`.

## Scene content

The original Blender scene has 31 objects, 30 mesh objects and no active camera or evaluated collection instances. Four nondegenerate meshes in `Collection` carry the placed scene:

| Source object | Role | Triangles | Indexed shells | Closed shells |
| --- | --- | ---: | ---: | ---: |
| `Cube.001` | asteroid field (`astro.fbx`) | 253,870 | 4,344 | 1,279 |
| `Cylinder.018` | station (`mainstru.fbx`) | 64,138 | 602 | 600 |
| `tyFlow_node_515.347` | widely dispersed debris | 15,012 | 750 | 750 |
| `tyFlow_node_515.521` | local debris (`debri.fbx` root) | 1,220 | 60 | 60 |

The other 3,065 shells in `Cube.001` are open decorative cards. Their source centers and sizes can guide a placement, but the actual collidable rock must be a three-dimensional prototype. The `scatter` collection has 22 unplaced near-origin meshes, including closed rock prototypes and open asteroid decoration. `asteroids__0.1038` is one triangle far outside the field; three remote `tyFlow` objects are approximately 0.000001–0.000003 source units wide. These are excluded from physical bounds. The exact filtering list is in `source-calibration.json`.

## Coordinate transform

The four physical meshes' exact evaluated vertex bounds in Blender world space are X `[-8.63146651, 7.83932340]`, Y `[-9.25299700, 9.48477785]`, Z `[-0.15746608, 2.05123425]`. The longest source dimension is Blender Y, `18.73777485` units. Uniform scale `3500 / 18.73777485 = 186.78845424` metres per source unit yields a canonical Unity span of `3076.55 × 412.56 × 3500.00` metres (X, Y, Z).

For a Blender world-space point `(x, y, z)`, canonical Unity coordinates are:

```text
unityX = (x + 0.39607155323028564) * 186.78845424372102
unityY = (z + 0.1574660783862747) * 186.78845424372102
unityZ = -(y - 0.11589042842388153) * 186.78845424372102
```

This is a single right-handed basis rotation `(x, z, -y)`, horizontal centering and a ground baseline. It does not place the scene vertically in the final layout; apply one shared layout translation to put the slab in the lower map half. For source NPZ vertices, apply the object's `matrix_world` first and the calibration matrix second. Do not apply an FBX root transform after this. Unity's old FBX importer reported root scales of 100, 9.599648 and 0.682607 and differing rotations; using the `.blend` evaluated world coordinates avoids mixing those roots into the calibration.

## Interchange

`source-meshes/source-meshes.json` links each mesh object to a compressed NPZ with `vertices` (`float32`, local), `triangles` (`int32`), and `material_index` (`int16`). Its `matrix_world` is a row-major 4×4 matrix using column-vector multiplication. `source-meshes/source-components.json` gives every indexed shell's exact world bounds and references a corresponding `*-components.npz` with `vertex_component` and `triangle_component` labels. `source-meshes/source-topology.json` reports boundary and nonmanifold edge counts for each shell.

Reference views, with blue field rocks, gold station and red debris, are [top](source-preview-top.png), [front](source-preview-front.png), [side](source-preview-side.png), and [oblique](source-preview-oblique.png). The original scene contains no camera, so these are temporary orthographic workbench cameras; the `.blend` was not saved.
Station-only detail views are [top](source-preview-station-top.png) and [oblique](source-preview-station-oblique.png).

The `.blend` does have saved editor viewports; `source-inventory.json` records their view quaternions, locations and distances. The active Layout viewport is perspective, looking at approximately `(-0.846, 0.801, -0.341)` source units with quaternion `(w,x,y,z)=(-0.7080,-0.2333,-0.2086,-0.6330)`. These are view-editor state, not a scene camera or authoritative map transform.

## Structural sample IDs

The large left rounded source mass is `Cylinder.018` shell 1; the right is shell 128. The open hex deck is shells 497–569 plus 593–600, in paired top/body components. The straight bridge truss between masses is shell 127. Visual highlights are [left rounded mass](source-highlight-dome-left-oblique.png), [hex deck](source-highlight-hex-deck-oblique.png) and [bridge truss](source-highlight-truss-span-oblique.png). `source-local-frames.json` provides optional local orthonormal frames for the hex deck and truss. Local voxelization of the slim truss preserves its geometry better than sampling it on one global axis-aligned grid; apply its stored instance pose after sampling.

Additional structural IDs: shell 0 is the left angled hanging truss, shell 134 the right horizontal truss extension, and shells 2–18 the equipment/platform over the left rounded mass. Shells 0 and 134 have no boundary edges but have nonmanifold edges; they are real truss structures despite failing the simple watertight flag. Shells 570–574 are five long parallel tanks beside the hex deck. Two distant radiator/tower banks consist roughly of shells 200–213 plus 243–270 and shells 214–242 plus 271–284; each bank contains many separate source pieces requiring connectivity decisions after sampling. [Left truss](source-highlight-left-open-structure-oblique.png), [right truss](source-highlight-right-open-structure-oblique.png), and [upper equipment](source-highlight-left-upper-equipment-oblique.png) have source-only visual proofs.
`source-station-semantic-groups.json` covers all 602 station shells exactly once in 21 visual families. It marks confidence per family and requires exact contact/six-neighbor splitting; the families are not automatic rigid-body assignments.

At a 1 m vertex-proximity threshold, the station has 525 candidate cross-shell contacts, leaving 403 provisional groups (`source-weld-candidates.json`). This is a diagnostic, not a rigid-body split. In particular, truss shell 127 has no vertex within 2 m of another shell even though it visually meets the rounded masses; a coarse surface can intersect a truss without close vertices. Use exact surface/voxel contact and semantic structure to decide which pieces form one physical asset.

`source-volume-budget.json` integrates the consistently oriented closed source shells at the calibrated scale. The 1,279 placed closed field rocks enclose about 6.86 million m³ in total. The left rounded mass (station shell 1) alone encloses about 10.39 million m³. A true 1 m solid occupancy must therefore hold at least that many cells for the mass before boundary dilation. A surface-intersection pass can use far fewer cells, but it represents a shell rather than a filled physical body and needs a separate behaviour decision.
