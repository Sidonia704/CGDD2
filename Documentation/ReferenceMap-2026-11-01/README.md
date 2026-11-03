# Reference mining station, 3500 m

The new map is reconstructed from the evaluated geometry and placed object transforms in `cggd.blend`. The longest source axis is calibrated to **3500 m**; the other axes keep their source proportions. One common Unity transform `(Blender x, z, −y)` and a 420 m downward layout translation preserve the station and asteroid field's relative positions. The conservative occupied-cell envelope is about **3079 × 418 × 3507 m** after voxel boundaries. [Calibration](source-calibration.md) gives the exact matrix and landmarks.

## Final artifacts

| Artifact | Location |
| --- | --- |
| Editable map layout | [ReferenceMiningStation3500.asset](../../Assets/_Starframe/Maps/ReferenceMiningStation/Layouts/ReferenceMiningStation3500.asset) |
| Native voxel assets and preview meshes | `Assets/_Starframe/Maps/ReferenceMiningStation/Assets/` and `Meshes/` |
| Sandbox scene | [ReferenceMiningStation3500.unity](../../Assets/_Starframe/Maps/ReferenceMiningStation/Scenes/ReferenceMiningStation3500.unity) |
| Frozen geometry catalog | [geometry-full-scene-final.json](../../Temp/ReferenceMap20261101/geometry-full-scene-final.json) |
| Physical assembly sidecar | [scene-assembly-links.json](../../Temp/ReferenceMap20261101/scene-assembly-links.json) |
| Durable derived-data archive | [interchange-final.zip](interchange-final.zip), [manifest](interchange-final-manifest.json), and [restore instructions](interchange-final-README.md) |
| Source extraction, checks, and overlays | This directory and `tools/ReferenceMap20261101/Source*.py` |

The catalog contains **2,345 unique editable voxel definitions**, **2,345 native preview meshes**, and **2,854 layout instances**. There are **2,559,830 unique cell records** and **3,123,469 instance-weighted cell records**. All 24 native import batches completed without reported errors or warnings. The [static correspondence audit](source-qa-native-assets.json) matches every catalog asset to its native definition and mesh, checks its cell size, and resolves all 2,854 layout GUID references. Seven imported scatter prototypes are deliberately unplaced library assets.

| Source family | Source coverage | Placed physical instances |
| --- | ---: | ---: |
| Station `Cylinder.018` | 602/602 connected source shells | 250 |
| Closed asteroid field `Cube.001` | 1,279/1,279 3D shells | 1,279 |
| Planar asteroid cards `Cube.001` | 3,065/3,065 card shells grouped into 515 rocks | 515 |
| Wide debris `tyFlow_node_515.347` | 750/750 closed shells | 750 |
| Local debris `tyFlow_node_515.521` | 60/60 closed shells | 60 |

Every source shell above is covered exactly once in the [frozen catalog audit](../../Temp/ReferenceMap20261101/geometry-full-scene-audit.json). The 515 planar-card rocks use closed source-derived 3D prototypes at exact card-cluster centers; 18 prototype asset keys are used. Wide debris shares nine duplicate definitions, so its 750 instances use 741 unique assets. The station's 250 definitions cover all 602 shells as semantic physical pieces. The [QA samples](source-qa-samples.md) show source/voxel overlays for the open hex deck, all three trusses, five tanks, and equipment.

## Grid, shapes, and assembly

The approved fixed grid size is chosen **per physical asset**: 1 m for thin mechanical detail and 1, 2, 4, or 5 m for solid bodies according to size. Counts are 1,395 assets at 1 m, 578 at 2 m, 315 at 4 m, and 57 at 5 m. The two largest rounded station masses are full solids at 5 m and 4 m. Across the library, **2,102 assets are solid** and **243 mechanical assets use authored hollow-shell occupancy** to retain open structures and apertures. [Resolution evidence](source-resolution-budget.md) explains why the largest bodies cannot fit a strict 1 m solid grid within the per-asset limit.

All eight hull shapes occur in the real map: Cube, Wedge, Slab, Pyramid, Block5, Block6, Block7, and Block8. The [shape audit](../../Temp/ReferenceMap20261101/geometry-full-scene-audit.json) records their cell counts; the [sample usage report](source-qa-template-usage.json) checks Block7/8 multi-cell model offsets. Each imported physical asset was generated as one initially six-neighbor connected cell set.

The initial station assembly sidecar has **125 candidate pairs**: 112 source-intersecting pairs are marked for FixedJoints, and 13 voxel-overlap pairs are marked for temporary collision filtering only. The [source contact audit](source-qa-station-contact-pairs.json) identifies the 112 triangle intersections within a 2 mm tolerance, 11 pairs without detected source contact, and two pairs separated by about 2.5 cm. The [assembly audit](../../Temp/ReferenceMap20261101/scene-assembly-links-audit.json) passes asset references and anchors. [Native assembly import](assembly-native.json) created all 125 links and verified 250 anchors. Runtime joint creation and fracture behavior still need Play verification.

## Visual and physics budget

The first graphics-enabled standalone [Player probe](player-probe.json), before the collision budget, took **46.27 s to load** and averaged **1,714 ms/frame** in the overview and **1,634 ms/frame** near the station. This failed performance acceptance. The user authorized preserving the map's visible source reconstruction while simplifying physical collision.

The optimized [Player probe](player-optimized.json) completed on an **i7-13700K / RTX 5080** at **1440×900**. It ran in batchmode and explicitly called `Camera.Render` each frame. Each view was sampled for 120 frames after warmup; CPU frame intervals include rendering and presentation. GPU timing returned no samples, so these figures are not a normal interactive Player FPS claim.

| Optimized probe | Load / enabled colliders | Mean frame | P95 frame | Max frame |
| --- | ---: | ---: | ---: | ---: |
| Initial map | 25.936 s / 2,854 | — | — | — |
| Overview, 120 frames | — | 14.608 ms | 20.751 ms | 24.043 ms |
| Near station, 120 frames | 8,000 enabled colliders | 9.223 ms | 13.051 ms | **182.174 ms** |

The probe also verified that one selected rock moved after an impulse, temporary damage expired without restoring its destroyed cell, and total surviving cells decreased by exactly that one cell. These checks do not cover every possible fracture or collision case. It reported **1.156 GB managed memory** (`GC.GetTotalMemory`) and **0.954 GB Profiler allocated memory**; these counters overlap and must not be added. The [optimized Windows build](player-build-optimized.json) succeeded with zero errors and 486 reported Sentis/Pipeline warnings. [Overview](player-optimized-overview.png), [near-station capture](player-optimized-near-station.png), and [Player log](Player-optimized.log) are preserved.

The sandbox scene now enables `SpaceMapRuntime.useBudgetedPhysics`. Distant bodies use one moving bounds BoxCollider each. Within **300 m** of the camera-to-body-bounds distance, the nearest whole bodies receive their detailed colliders when they fit the **8,000 enabled-scenery-collider budget**; their bounds proxy is disabled while detailed mode is active. The dedicated `BudgetedScenery` layer disables scenery-to-scenery collision while retaining interaction with ships and projectiles. Distant ship contact can occur on empty space inside a rock's bounds box. Projectile traversal still checks surviving voxel cells, so the bounds box alone does not make empty space count as a projectile hit. Bodies remain dynamic and zero gravity, and sparse hit HP and physical fragmentation remain in the runtime path.

`VoxelStructureRuntime.ColliderCount` reports the number of generated detailed boxes, including disabled ones. It is **not** the currently active count; `SpaceMapRuntime.ActiveMapColliderCount` reports the budgeted active count. The scene sets `detailedCollisionDistance=300`, `activeColliderBudget=8000`, `physicsRefreshFrames=12`, and `maximumFineVisualCells=8000`. Intact assets above 8,000 cells keep their baked exterior Cube mesh instead of generating the exact fine hull mesh near the camera; their cell data remains available for damage. The 182 ms near-view maximum remains; first-time fine-visual generation may still stall and has not been isolated as its cause. The exterior Cube mesh follows the source boundary but is not final art quality. These knobs and `@tune` notes are in [SpaceMapRuntime.cs](../../Assets/_Starframe/Scripts/Runtime/Construction/Maps/SpaceMapRuntime.cs), with scene-specific values in [ReferenceMiningStation3500.unity](../../Assets/_Starframe/Maps/ReferenceMiningStation/Scenes/ReferenceMiningStation3500.unity).

The first destructive hit on a very large body can still generate damaged-region visuals and cause a separate spike; this probe did not time that path. The near-view boundary and silhouettes are source-derived voxel approximations, with final art review still open.

## Open and verify

In Unity, double-click the layout asset above. Its `SpaceMapDefinition` opens directly in **Starframe → 开发工具 → 体块地图工作室**. The **地图布局** mode displays the placed instances; double-click any voxel asset definition to edit its cells. The final 2,854-instance layout has been opened in Map Studio, with [full-map](unity-full-map-studio.png) and [station-close](unity-station-map-studio.png) preview captures; the Editor Console had zero errors at capture. These captures show layout visibility, not final high-detail art acceptance. Open the sandbox scene above and enter Play for the staged runtime view. Its construction is configured in frame batches of up to 32 instances with a 5 ms work budget per frame, so objects appear progressively. The scene is separate from the existing game flow. [Scene creation](scene-native.json) succeeded.

[Native world verification](world-native.json) checked all 2,854 serialized instance asset references, positions, rotations, scales, link anchors and flags against the frozen catalog and assembly sidecar. It also checked 2,338 placed definitions against the 2,345-asset library, seven spare prototypes, **zero disconnected native assets**, the unique and placed cell totals, and nonzero use of all eight hull shapes. The pre-optimization full EditMode suite passed **392/392** with zero skipped, and focused budgeted-runtime tests passed **11/11**; see [full result](tests/full-editmode.json) and [focused result](tests/map-runtime-optimized.json). The optimized Player probe above is separate evidence.

The 10.6 MB interchange archive was independently verified against its manifest: **2,351 entries**, including all 2,345 final cell-row files. It preserves the derived importer inputs if `Temp` is cleared. The archive does not replace the native Unity assets or original source files.

The five original reference files (`cggd.blend` and `astro`, `debri`, `mainstru`, `scatter` FBXs) still match the original SHA-256 manifest in [source-qa-original-hashes.json](source-qa-original-hashes.json). The older [MiningStationAsteroidField.asset](../../Assets/_Starframe/Maps/Layouts/MiningStationAsteroidField.asset) draft and `MiningStationRound3` assets remain preserved. Temporary build outputs under `Temp/ReferenceMap20261101/` remain because automatic approval review refused their deletion; the final layout contains native asset GUID references and no temporary paths.

The [EditorContextPreserved.unity](../../Assets/_Starframe/Maps/ReferenceMiningStation/Scenes/EditorContextPreserved.unity) helper scene was saved to preserve the Editor's previous clean untitled scene with its two default roots. It is an Editor context backup, not game content; keep it with this audit.

**Runtime status:** The first Player baseline failed; the optimized batchmode probe now measures loading, frame intervals, and three targeted physics/damage checks. A 182 ms near-view spike remains; very large-body first-hit cost, broad Play collision/fracture behavior, ordinary interactive Player FPS, and final visual acceptance still need verification. The large-body exterior Cube boundary is an intentional visual approximation.
