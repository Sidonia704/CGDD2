# Player build warning and native-data audit

Read-only review of `player-build.json`, the Unity `Editor.log` build section, `Player.log`, the native map YAML, and the saved native verification reports. No Unity Editor command was run for this review.

## Build warnings

The Windows Player build succeeded in 71.5 seconds with **0 errors and 486 warnings**. The Editor log accounts for them as follows:

| Count | Source | Meaning |
| ---: | --- | --- |
| 460 | `com.unity.ai.inference` / Sentis `ConvGeneric.compute` | Compute-shader variant warnings involving `TRANSPOSE_OUTPUT` and `NON_UNIFORM_CONVGROUP_PER_OC`. |
| 25 | Other `com.unity.ai.inference` / Sentis shaders (`ScatterND` 9, `ConvTranspose` 6, `SliceSet` 4, `GridSample` 3, `ScatterElements` 2, `Pad` 1) | D3D11 shader compiler warnings, mainly integer modulus, signedness, or loop variable diagnostics. |
| 1 | `com.unity.pipeline` build processor | No `RuntimePipelineConfig` asset; Pipeline runtime will be disabled in Player builds. |

The first 485 warnings come from installed package shaders under `Library/PackageCache/com.unity.ai.inference@9a123aee5df7`, not from the generated reference-map geometry or its materials. The last is a project/package configuration warning. Its practical effect depends on whether gameplay needs the Pipeline runtime. This log contains no map-cell, assembly-link, mesh-reference, or C# compiler warning in the build section.

## Imported map checks

- Saved `assembly-native.json`: 2,854 instances, 125 links (112 FixedJoints and 13 collision-ignore-only links), all 250 anchor cells verified.
- Saved `world-native.json`: 2,338 placed unique assets, 7 intentional unplaced scatter prototypes, 2,345 library assets, 2,559,830 unique cells, 3,123,469 placed cells, all 8 hull shapes, and 0 disconnected assets. World bounds are X −1540.984 to 1538.5 m, Y −420.5 to −2.5 m, Z −1752.5 to 1754 m.
- Direct YAML scan: 2,345 voxel definitions and 2,345 coarse LOD meshes. Every definition has a distinct existing LOD mesh GUID, a nonzero `initialLodSignature`, and the hull module database reference. Cell-size counts are 1 m: 1,395; 2 m: 578; 4 m: 315; 5 m: 57.
- The saved scene has staged startup and lazy fine visuals enabled, an initial assembly depenetration cap of 1, and the layout serializes 125 assembly links.

The captured `Player.log` shows startup and shutdown without a map-related error. It is a short launch record, so it does not by itself establish long-session runtime performance or destruction behaviour.
