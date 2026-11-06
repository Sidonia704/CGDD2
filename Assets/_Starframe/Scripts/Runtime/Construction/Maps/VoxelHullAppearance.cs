// @role Reuse exact ship hull models and scenery surfaces, culling shared full-cell faces across source models.
// @calls ModuleDatabase, BlockGeometry, VoxelGeometry, VoxelAssetDefinition, VoxelModelPartition, VoxelTerrainRenderChunks

using System;
using System.Collections.Generic;
using Starframe.Construction;
using Starframe.Flight;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Starframe.Maps
{
    public static class VoxelHullAppearance
    {
        public static readonly BlockShape[] Shapes = { BlockShape.Cube, BlockShape.Wedge, BlockShape.Slab, BlockShape.Pyramid, BlockShape.Block5, BlockShape.Block6, BlockShape.Block7, BlockShape.Block8 };
        public const int ShapeCount = 8;
        public static int ShapeIndex(BlockShape shape) { int index = Array.IndexOf(Shapes, shape); return index < 0 ? 0 : index; }
        public static Material[] Materials(VoxelAssetDefinition asset, List<Material> owned)
        {
            var result = new Material[asset.SurfaceCount * ShapeCount];
            for (int surface = 0; surface < asset.SurfaceCount; surface++) for (int i = 0; i < ShapeCount; i++)
            {
                var definition = asset.hullModules ? asset.hullModules.Get(Shapes[i]) : null;
                // Explicit scenery surfaces override borrowed hull shading; geometry and ship materials stay shared.
                var overrideMaterial = asset.surfaces != null && surface < asset.surfaces.Length ? asset.surfaces[surface] : null;
                var source = overrideMaterial ? overrideMaterial : definition ? definition.material : null;
                Color tint = asset.baseColor * (asset.surfaceColors != null && surface < asset.surfaceColors.Count ? asset.surfaceColors[surface] : Color.white);
                if (source && tint == Color.white) { result[surface * ShapeCount + i] = source; continue; }
                var material = source ? new Material(source) : new Material(Shader.Find("Universal Render Pipeline/Complex Lit"));
                material.name = "Map " + Shapes[i] + " Colour " + surface; material.hideFlags = HideFlags.HideAndDontSave; material.enableInstancing = true;
                var color = source && source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor") : Color.white;
                material.SetColor("_BaseColor", color * tint); owned.Add(material); result[surface * ShapeCount + i] = material;
            }
            return result;
        }

        internal sealed class Model
        {
            public Vector3[] vertices, normals;
            public Vector2[] uv;
            public Vector4[] tangents;
            public int[] indices, hiddenBy, remap;
            public bool fillsCell;
            readonly Dictionary<int, int[]> visible = new Dictionary<int, int[]>();
            public int[] VisibleIndices(int localCovered)
            {
                if (localCovered == 0) return indices;
                if (visible.TryGetValue(localCovered, out var cached)) return cached;
                var result = new List<int>(indices.Length);
                for (int triangle = 0; triangle < hiddenBy.Length; triangle++)
                {
                    int face = hiddenBy[triangle];
                    if (face >= 0 && (localCovered & (1 << face)) != 0) continue;
                    result.Add(indices[triangle * 3]); result.Add(indices[triangle * 3 + 1]); result.Add(indices[triangle * 3 + 2]);
                }
                cached = result.ToArray(); visible.Add(localCovered, cached); return cached;
            }
        }

        // Main-thread, template-owned cache. No static source arrays or generated Mesh objects outlive its final owner.
        internal sealed class ModelCache : IDisposable
        {
            readonly ModuleDatabase database;
            readonly Dictionary<(BlockShape, Vector3Int), Model> models = new Dictionary<(BlockShape, Vector3Int), Model>();
            public ModelCache(ModuleDatabase source) { database = source; }
            public Model Get(BlockShape shape, Vector3Int offset)
            {
                var key = (shape, offset);
                if (!models.TryGetValue(key, out var model)) { model = Read(database, shape, offset); models.Add(key, model); }
                return model;
            }
            // A delayed chunk build must use the model arrays captured with its template generation.
            // Remap is mutable build scratch, so each fork gets separate Model wrappers and remap arrays.
            public ModelCache ForkFrozen()
            {
                var result = new ModelCache(null);
                foreach (var pair in models)
                {
                    var source = pair.Value;
                    result.models.Add(pair.Key, new Model { vertices = source.vertices, normals = source.normals,
                        uv = source.uv, tangents = source.tangents, indices = source.indices, hiddenBy = source.hiddenBy,
                        fillsCell = source.fillsCell, remap = new int[source.remap.Length] });
                }
                return result;
            }
            public void Dispose() => models.Clear();
        }

        static Model Read(ModuleDatabase database, BlockShape shape, Vector3Int offset)
        {
            var definition = database ? database.Get(shape) : null; var mesh = definition ? definition.mesh : null; bool owned = !mesh;
            if (!mesh) mesh = BlockGeometry.CreateSolid(shape);
            if (shape == BlockShape.Block7 || shape == BlockShape.Block8)
            {
                var source = mesh; mesh = VoxelModelPartition.Create(source, offset);
                if (owned) { if (Application.isPlaying) Object.Destroy(source); else Object.DestroyImmediate(source); }
                owned = true;
            }
            var model = new Model { vertices = mesh.vertices, normals = mesh.normals, uv = mesh.uv, tangents = mesh.tangents, indices = mesh.triangles };
            model.remap = new int[model.vertices.Length];
            model.hiddenBy = new int[model.indices.Length / 3];
            for (int triangle = 0; triangle < model.hiddenBy.Length; triangle++)
            {
                int a = model.indices[triangle * 3], b = model.indices[triangle * 3 + 1], c = model.indices[triangle * 3 + 2];
                Vector3 normal = Vector3.Cross(model.vertices[b] - model.vertices[a], model.vertices[c] - model.vertices[a]).normalized;
                model.hiddenBy[triangle] = -1;
                for (int face = 0; face < 6; face++)
                {
                    Vector3 direction = VoxelGeometry.Directions[face];
                    if (Vector3.Dot(normal, direction) > .9999f && Mathf.Abs(Vector3.Dot(model.vertices[a], direction) - .5f) < .00001f && Mathf.Abs(Vector3.Dot(model.vertices[b], direction) - .5f) < .00001f && Mathf.Abs(Vector3.Dot(model.vertices[c], direction) - .5f) < .00001f)
                    { model.hiddenBy[triangle] = face; break; }
                }
            }
            model.fillsCell = FillsUnitCell(model);
            if (owned) { if (Application.isPlaying) Object.Destroy(mesh); else Object.DestroyImmediate(mesh); }
            return model;
        }
        // A partition is a full cube only if its entire boundary consists of the six
        // outward unit-square faces. Corner presence alone would accept bevels or cavities.
        static bool FillsUnitCell(Model model)
        {
            const float epsilon = .00001f;
            foreach (var point in model.vertices)
                if (Mathf.Abs(point.x) > .5f + epsilon || Mathf.Abs(point.y) > .5f + epsilon || Mathf.Abs(point.z) > .5f + epsilon)
                    return false;
            var areas = new double[6];
            for (int triangle = 0; triangle < model.hiddenBy.Length; triangle++)
            {
                var a = model.vertices[model.indices[triangle * 3]];
                var b = model.vertices[model.indices[triangle * 3 + 1]];
                var c = model.vertices[model.indices[triangle * 3 + 2]];
                float twiceArea = Vector3.Cross(b - a, c - a).magnitude;
                if (twiceArea <= .00000001f) continue;
                int face = model.hiddenBy[triangle];
                if (face < 0) return false;
                areas[face] += twiceArea * .5;
            }
            for (int face = 0; face < 6; face++) if (Math.Abs(areas[face] - 1.0) > .0001) return false;
            return true;
        }
        static bool SameSource(VoxelCellState a, VoxelCellState b) => a.shape == b.shape && Quaternion.Angle(a.Rotation, b.Rotation) < .01f &&
            a.position - Vector3Int.RoundToInt(a.Rotation * (Vector3)a.modelOffset) == b.position - Vector3Int.RoundToInt(b.Rotation * (Vector3)b.modelOffset);
        internal static int CoverageMask(VoxelCellState cell, IReadOnlyDictionary<Vector3Int, VoxelCellState> coverage, ModelCache cache)
        {
            int mask = 0;
            for (int face = 0; face < 6; face++)
                if (coverage.TryGetValue(cell.position + VoxelGeometry.Directions[face], out var neighbor) && neighbor.Alive &&
                    (cache.Get(neighbor.shape, neighbor.modelOffset).fillsCell || SameSource(cell, neighbor))) mask |= 1 << face;
            return mask;
        }
        public static Mesh CreateCellMesh(ModuleDatabase database, BlockShape shape, Vector3Int offset)
        {
            var model = Read(database, shape, offset);
            var mesh = new Mesh { name = shape + " map cell " + offset, hideFlags = HideFlags.HideAndDontSave, indexFormat = model.vertices.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.vertices = model.vertices; mesh.normals = model.normals; mesh.uv = model.uv; mesh.tangents = model.tangents; mesh.triangles = model.indices; mesh.RecalculateBounds(); return mesh;
        }
        public static Mesh Build(IReadOnlyDictionary<Vector3Int, VoxelCellState> cells, int materialCount, float size, ModuleDatabase database)
        {
            using (var cache = new ModelCache(database)) return BuildChunk(cells, cells, materialCount, size, cache);
        }
        // The subset supplies emitted cells; coverage must contain the entire current island, never just this render chunk.
        internal static Mesh BuildChunk(IReadOnlyDictionary<Vector3Int, VoxelCellState> subset, IReadOnlyDictionary<Vector3Int, VoxelCellState> coverage,
            int materialCount, float size, ModelCache cache)
        {
            var vertices = new List<Vector3>(); var normals = new List<Vector3>(); var uv = new List<Vector2>(); var tangents = new List<Vector4>();
            var indices = new List<int>[Mathf.Max(ShapeCount, materialCount)]; for (int i = 0; i < indices.Length; i++) indices[i] = new List<int>();
            foreach (var pair in subset)
            {
                var cell = pair.Value; if (!cell.Alive) continue;
                int covered = CoverageMask(cell, coverage, cache); if (covered == 63) continue;
                int shape = ShapeIndex(cell.shape), slot = Mathf.Clamp(cell.surface, 0, indices.Length / ShapeCount - 1) * ShapeCount + shape;
                var model = cache.Get(cell.shape, cell.modelOffset); var remap = model.remap; Array.Fill(remap, -1);
                Quaternion rotation = cell.Rotation; int localCovered = 0;
                if (covered != 0) for (int face = 0; face < 6; face++)
                {
                    var world = Vector3Int.RoundToInt(rotation * (Vector3)VoxelGeometry.Directions[face]);
                    for (int f = 0; f < 6; f++) if (world == VoxelGeometry.Directions[f]) { if ((covered & (1 << f)) != 0) localCovered |= 1 << face; break; }
                }
                foreach (int source in model.VisibleIndices(localCovered))
                {
                    if (remap[source] < 0)
                    {
                        remap[source] = vertices.Count; vertices.Add(((Vector3)pair.Key + rotation * model.vertices[source]) * size);
                        normals.Add(rotation * (source < model.normals.Length ? model.normals[source] : Vector3.up));
                        uv.Add(source < model.uv.Length ? model.uv[source] : Vector2.zero);
                        var t = source < model.tangents.Length ? model.tangents[source] : new Vector4(1, 0, 0, 1); var tangent = rotation * new Vector3(t.x, t.y, t.z);
                        tangents.Add(new Vector4(tangent.x, tangent.y, tangent.z, t.w));
                    }
                    indices[slot].Add(remap[source]);
                }
            }
            var output = new Mesh { name = "Shared ship hull scenery", indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            output.SetVertices(vertices); output.SetNormals(normals); output.SetUVs(0, uv); output.SetTangents(tangents); output.subMeshCount = indices.Length;
            for (int i = 0; i < indices.Length; i++) output.SetTriangles(indices[i], i, false);
            output.RecalculateBounds(); return output;
        }
    }
}
