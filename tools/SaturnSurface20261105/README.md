# SaturnRing continuous source surfaces

`Prepare.py` recovers original source triangles using the archived map catalog,
the frozen source NPZ meshes and the original calibration. It writes only
`Temp/SaturnSurface20261105`, leaving original assets and scenes intact.

Run `python tools/SaturnSurface20261105/Prepare.py`. The final
`manifest-0000.json` lists all recovered meshes and explicit failures. Every
surface must fit the corresponding occupied-cell bounds. Unique source shells
retain the original triangles. Seven assets whose source shells were split
between physical objects use exact triangle clipping against the union of
their occupied cell boxes. Internal coplanar clipping edges are dissolved;
ambiguous loops fall back to the exact clipped polygons. This preserves flat
panels without voxel steps, deformation or invented bridges.

`Starframe.EditorTools.SaturnSurfaceAuthoring.ImportBatch(manifestPath,start,count)`
clones original voxel definitions and writes signed render meshes under
`Assets/_Starframe/Maps/SaturnRingContinuous`. Batch sizes of 50–100 bound asset
import work. The returned object includes start/end, total, changed count and
triangle count. `CreateLayout()` clones the established layout, preserving all
instance IDs, poses, scales and assembly links, and redirects its definitions
to the cloned assets. Original physical cell topology is preserved. Imported
structures use 45-degree crease normals, so pipes can shade continuously while
right-angle panels remain crisp; rocks share vertex normals.

`CreateBackdropMeshes()` writes four shared smooth closed rock meshes, two
detailed and two distant. All fit a unit sphere. Bind these four assets to the
existing `RangeAsteroidBackdrop.Configure` arguments to preserve the existing
fixed placement, fades, culling and instancing budgets. The generator does not
change scene bindings itself.

The offline source reconstruction is repeatable from tracked source files.
Generated JSON is interchange, not an additional runtime dependency.
