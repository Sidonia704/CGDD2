// @role Share generation-safe collision, exact hulls and lightweight damaged exterior templates without changing voxel health or occupancy.
// @calls VoxelGeometry, VoxelAssetDefinition, VoxelStructureRuntime, VoxelTerrainRenderChunks, VoxelTerrainTopology, VoxelHullAppearance

using System.Collections.Generic;
using Starframe.Construction;
using Starframe.Flight;
using UnityEngine;

namespace Starframe.Maps
{
    public static class VoxelTemplateCache
    {
        public sealed class Template
        {
            public VoxelAssetDefinition asset;
            public int revision, users;
            // Certified once from the authored cell set when this generation is built: exact cell count and
            // six-neighbour connectivity. Warm instances seed their topology from this instead of re-running BFS.
            public int cellCount;
            public bool initialConnected;
            public bool current = true;
            public Mesh mesh;
            // Borrowed imported mesh. Its owner is the asset; this cache never destroys it.
            public Mesh initialLodMesh;
            internal Dictionary<Vector3Int, VoxelCellState> snapshot;
            internal VoxelHullAppearance.ModelCache frozenModels;
            internal ModelGeneration modelGeneration;
            internal int materialCount;
            internal float cellSize;
            internal bool usesHullModels;
            public List<Bounds> boxes;
            public VoxelTerrainRenderChunks.SharedTemplate renderChunks;
            internal VoxelTerrainRenderChunks.SharedTemplate lightweightChunks;
        }
        internal sealed class ModelGeneration
        {
            public ModuleDatabase database;
            public int revision, users;
            public VoxelHullAppearance.ModelCache models;
        }
        static readonly Dictionary<VoxelAssetDefinition, Template> cache = new Dictionary<VoxelAssetDefinition, Template>();
        static readonly Dictionary<(ModuleDatabase, int), ModelGeneration> modelGenerations =
            new Dictionary<(ModuleDatabase, int), ModelGeneration>();
        public static Template Acquire(VoxelAssetDefinition asset) => Acquire(asset, false);
        public static Template Acquire(VoxelAssetDefinition asset, bool lazyFineVisuals)
        {
            if (!cache.TryGetValue(asset, out var template) ||
                (template.snapshot == null && !template.mesh) || template.revision != asset.GeometryRevision)
            {
                if (template != null) { template.current = false; if (template.users == 0) Dispose(template); }
                template = new Template { asset = asset, revision = asset.GeometryRevision };
                var states = new Dictionary<Vector3Int, VoxelCellState>();
                foreach (var cell in asset.cells) states.Add(cell.position, new VoxelCellState { position = cell.position, surface = cell.surface, shape = cell.shape, quarterTurns = cell.quarterTurns, modelOffset = cell.modelOffset });
                int materials = asset.SurfaceCount * (asset.UsesHullModels ? VoxelHullAppearance.ShapeCount : 1);
                try
                {
                    // Connectivity certification is part of cold template construction (prewarm/initialization),
                    // never of the warm instance path; it is computed once for this generation's cell set.
                    template.cellCount = states.Count;
                    template.initialConnected = new VoxelTerrainTopology().Reset(states);
                    template.materialCount = materials; template.cellSize = asset.cellSize;
                    template.usesHullModels = asset.UsesHullModels;
                    if (lazyFineVisuals && ValidInitialLod(asset, materials))
                    {
                        template.snapshot = states;
                        template.initialLodMesh = asset.initialLodMesh;
                        template.modelGeneration = AcquireModels(asset.hullModules);
                        template.frozenModels = template.modelGeneration.models.ForkFrozen();
                    }
                    else
                    {
                        // Default path retains the existing eager exact mesh and chunk prewarm.
                        template.renderChunks = VoxelTerrainRenderChunks.AcquireTemplate(asset);
                        template.mesh = template.renderChunks != null
                            ? VoxelHullAppearance.BuildChunk(states, states, materials, asset.cellSize, template.renderChunks.models)
                            : VoxelGeometry.Build(states, materials, asset.cellSize, asset.hullModules);
                        template.mesh.hideFlags = HideFlags.HideAndDontSave;
                    }
                    template.boxes = VoxelGeometry.CollisionBoxes(states, asset.cellSize); cache[asset] = template;
                }
                catch { Dispose(template); throw; }
            }
            if (!lazyFineVisuals && template.snapshot != null)
            {
                EnsureRenderChunks(template);
                EnsureExactMesh(template);
            }
            template.users++; return template;
        }
        static bool ValidInitialLod(VoxelAssetDefinition asset, int materials) => asset.UsesHullModels &&
            asset.initialLodMesh && asset.initialLodMesh.vertexCount > 0 &&
            asset.initialLodMesh.subMeshCount == materials &&
            asset.initialLodSignature == SpaceMapVisuals.ComputeGeometrySignature(asset);
        static ModelGeneration AcquireModels(ModuleDatabase database)
        {
            int revision = database ? database.Revision : 0;
            var key = (database, revision);
            if (!modelGenerations.TryGetValue(key, out var generation))
            {
                var models = new VoxelHullAppearance.ModelCache(database);
                try
                {
                    // All map hull offsets are a tiny fixed vocabulary. Freeze each source model generation once;
                    // each asset gets its own cheap mutable remap scratch through ForkFrozen.
                    foreach (var shape in VoxelHullAppearance.Shapes)
                        foreach (var offset in BlockModules.LocalCells(shape)) models.Get(shape, offset);
                    generation = new ModelGeneration { database = database, revision = revision, models = models };
                    modelGenerations.Add(key, generation);
                }
                catch { models.Dispose(); throw; }
            }
            generation.users++;
            return generation;
        }
        static void ReleaseModels(ModelGeneration generation)
        {
            if (generation == null || generation.users <= 0 || --generation.users != 0) return;
            var key = (generation.database, generation.revision);
            if (modelGenerations.TryGetValue(key, out var current) && ReferenceEquals(current, generation))
                modelGenerations.Remove(key);
            generation.models.Dispose(); generation.models = null;
        }
        public static Mesh EnsureExactMesh(Template template)
        {
            if (template == null || template.users <= 0) throw new System.ObjectDisposedException(nameof(template));
            if (template.mesh) return template.mesh;
            if (template.snapshot == null || template.frozenModels == null) throw new System.ObjectDisposedException(nameof(template));
            template.mesh = VoxelHullAppearance.BuildChunk(template.snapshot, template.snapshot,
                template.materialCount, template.cellSize, template.frozenModels);
            template.mesh.hideFlags = HideFlags.HideAndDontSave;
            return template.mesh;
        }
        public static VoxelTerrainRenderChunks.SharedTemplate EnsureRenderChunks(Template template)
        {
            if (template == null || template.users <= 0) throw new System.ObjectDisposedException(nameof(template));
            if (template.renderChunks != null) return template.renderChunks;
            if (!template.usesHullModels || template.cellCount < VoxelTerrainRenderChunks.MinimumCells) return null;
            template.renderChunks = template.snapshot == null
                ? VoxelTerrainRenderChunks.AcquireTemplate(template.asset)
                : VoxelTerrainRenderChunks.CreateFromSnapshot(template.asset, template.snapshot,
                    template.materialCount, template.cellSize, template.revision, template.frozenModels);
            return template.renderChunks;
        }
        // Large coarse scenery must not expand into every bevelled interior ship mesh on its first hit.
        // Keep source shape records/material slots intact, while displaying the same full-cell exterior LOD in larger render tiles.
        public static VoxelTerrainRenderChunks.SharedTemplate EnsureLightweightRenderChunks(Template template)
        {
            if (template == null || template.users <= 0) throw new System.ObjectDisposedException(nameof(template));
            if (template.lightweightChunks != null) return template.lightweightChunks;
            if (template.snapshot == null) return EnsureRenderChunks(template);
            template.lightweightChunks = VoxelTerrainRenderChunks.CreateFromSnapshot(template.asset, template.snapshot,
                template.materialCount, template.cellSize, template.revision, template.frozenModels, 8, true);
            return template.lightweightChunks;
        }
        // Retain this exact generation; a fragment must not acquire a newer edited source asset.
        public static Template Retain(Template template)
        {
            if (template == null) throw new System.ArgumentNullException(nameof(template));
            if (template.users <= 0 || (template.snapshot == null && !template.mesh)) throw new System.ObjectDisposedException(nameof(template));
            template.users++; return template;
        }
        public static void Release(Template template)
        {
            if (template == null || template.users <= 0 || --template.users != 0) return;
            if (cache.TryGetValue(template.asset, out var current) && ReferenceEquals(current, template)) cache.Remove(template.asset);
            template.current = false; Dispose(template);
        }
        public static void Clear() { foreach (var template in cache.Values) { template.current = false; if (template.users == 0) Dispose(template); } cache.Clear(); }
        static void Dispose(Template template)
        {
            if (template.mesh) { if (Application.isPlaying) Object.Destroy(template.mesh); else Object.DestroyImmediate(template.mesh); }
            template.mesh = null;
            VoxelTerrainRenderChunks.ReleaseTemplate(template.renderChunks); template.renderChunks = null;
            VoxelTerrainRenderChunks.ReleaseTemplate(template.lightweightChunks); template.lightweightChunks = null;
            template.frozenModels?.Dispose(); template.frozenModels = null;
            ReleaseModels(template.modelGeneration); template.modelGeneration = null;
            template.snapshot?.Clear(); template.snapshot = null;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void Reset() => Clear();
    }
}
