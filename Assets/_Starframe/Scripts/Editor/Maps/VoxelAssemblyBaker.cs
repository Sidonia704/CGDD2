using System;
using System.Collections.Generic;
using System.IO;
using Starframe.Maps;
using UnityEditor;
using UnityEngine;

namespace Starframe.EditorTools
{
    public static class VoxelAssemblyBaker
    {
        // Editor provenance convention, not a tamper-proof geometry check.
        public static bool IsVoxelAssemblyCache(VoxelAssetDefinition asset) => asset && asset.HasValidInitialLod &&
            asset.initialLodMesh.name == asset.name + " Voxel Assembly" &&
            !AssetDatabase.IsSubAsset(asset.initialLodMesh) &&
            AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GetAssetPath(asset.initialLodMesh)) == asset.initialLodMesh;

        public static long TriangleCount(Mesh mesh)
        {
            if (!mesh) return 0;
            long count = 0;
            for (int i = 0; i < mesh.subMeshCount; i++) count += (long)mesh.GetIndexCount(i) / 3;
            return count;
        }

        public static Mesh Bake(VoxelAssetDefinition asset, string meshPath)
        {
            if (!asset) throw new ArgumentNullException(nameof(asset));
            if (!asset.Validate(out string error)) throw new ArgumentException(error, nameof(asset));
            if (asset.cells.Count == 0) throw new ArgumentException("Cannot bake an empty voxel assembly.", nameof(asset));
            meshPath = (meshPath ?? "").Replace('\\', '/');
            if (!meshPath.StartsWith("Assets/", StringComparison.Ordinal) || meshPath.Contains("/../") ||
                !string.Equals(Path.GetExtension(meshPath), ".asset", StringComparison.OrdinalIgnoreCase) ||
                !AssetDatabase.IsValidFolder(Path.GetDirectoryName(meshPath).Replace('\\', '/')))
                throw new ArgumentException("Use an existing Assets folder and a standalone .asset mesh path.", nameof(meshPath));
            var existing = AssetDatabase.LoadMainAssetAtPath(meshPath);
            if (existing && (!(existing is Mesh) || AssetDatabase.IsSubAsset(existing)))
                throw new ArgumentException("The target must be a standalone Mesh asset; imported model subassets cannot be overwritten.", nameof(meshPath));
            var states = new Dictionary<Vector3Int, VoxelCellState>(asset.cells.Count);
            foreach (var cell in asset.cells)
                states.Add(cell.position, new VoxelCellState { position = cell.position, surface = cell.surface,
                    shape = cell.shape, quarterTurns = cell.quarterTurns, modelOffset = cell.modelOffset });
            Mesh built = VoxelGeometry.Build(states, asset.RenderMaterialCount, asset.cellSize, asset.hullModules);
            try
            {
                var merged = SceneVoxelFaceMerger.Merge(built, asset);
                if (merged != built) { UnityEngine.Object.DestroyImmediate(built); built = merged; }
                if (!built || built.vertexCount == 0 || built.subMeshCount != asset.RenderMaterialCount)
                    throw new InvalidOperationException("The voxel assembly produced an empty mesh or incompatible material slots.");
                built.name = asset.name + " Voxel Assembly";
                built.hideFlags = HideFlags.None;
                Mesh result;
                if (existing)
                {
                    result = (Mesh)existing;
                    // CopySerialized updates CPU data but can leave an already rendered Mesh's GPU buffers stale.
                    // Use Mesh setters so editor previews and live renderers see the rebuilt block assembly immediately.
                    result.Clear(); result.indexFormat = built.indexFormat;
                    result.vertices = built.vertices; result.normals = built.normals;
                    result.uv = built.uv; result.tangents = built.tangents;
                    result.subMeshCount = built.subMeshCount;
                    for (int s = 0; s < built.subMeshCount; s++) result.SetIndices(built.GetIndices(s), built.GetTopology(s), s, false);
                    result.bounds = built.bounds; result.UploadMeshData(false);
                    EditorUtility.SetDirty(result);
                }
                else
                {
                    result = built;
                    AssetDatabase.CreateAsset(result, meshPath);
                    built = null;
                }
                result.name = asset.name + " Voxel Assembly";
                EditorUtility.SetDirty(result);
                asset.initialLodMesh = result;
                asset.NotifyChanged();
                asset.initialLodSignature = SpaceMapRuntime.ComputeGeometrySignature(asset);
                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssetIfDirty(result);
                AssetDatabase.SaveAssetIfDirty(asset);
                return result;
            }
            finally { if (built) UnityEngine.Object.DestroyImmediate(built); }
        }
    }
}
