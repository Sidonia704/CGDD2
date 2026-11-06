// @role Share generation-safe collision and immutable exact hull templates without changing voxel health or occupancy.
// @calls VoxelGeometry, VoxelAssetDefinition, VoxelStructureRuntime, VoxelTerrainRenderChunks, VoxelHullAppearance

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
            // Fixed occupied-cell count for this immutable generation.
            public int cellCount;
            public bool current = true;
            internal bool initialLodChecked, exactMeshBuilt;
            public Mesh mesh;
            // Borrowed imported mesh. Its owner is the asset; this cache never destroys it.
            public Mesh initialLodMesh;
            internal SharedVoxelCells snapshot;
            internal VoxelHullAppearance.ModelCache frozenModels;
            internal ModelGeneration modelGeneration;
            internal int materialCount;
            internal float cellSize;
            internal bool usesHullModels;
            public List<Bounds> boxes;
            public VoxelTerrainRenderChunks.SharedTemplate renderChunks;
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
        public static Template Acquire(VoxelAssetDefinition asset, bool lazyFineVisuals, bool dataOnly = false)
        {
            if (!cache.TryGetValue(asset, out var template) || template.snapshot == null || (template.exactMeshBuilt && !template.mesh) || template.revision != asset.GeometryRevision)
            {
                if (!asset.Validate(out var error)) throw new System.ArgumentException(error);
                if (template != null) { template.current = false; if (template.users == 0) Dispose(template); }
                template = new Template { asset = asset, revision = asset.GeometryRevision,
                    snapshot = new SharedVoxelCells(asset), materialCount = asset.RenderMaterialCount,
                    cellSize = asset.cellSize, usesHullModels = asset.UsesHullModels };
                template.cellCount = template.snapshot.Count;
                cache[asset] = template;
            }
            template.users++;
            try
            {
                if (!dataOnly)
                {
                    EnsureModels(template);
                    if (lazyFineVisuals && !template.initialLodChecked)
                    {
                        template.initialLodChecked = true;
                        if (asset.HasValidInitialLod) template.initialLodMesh = asset.initialLodMesh;
                    }
                    if (!lazyFineVisuals || !template.initialLodMesh) { EnsureRenderChunks(template); EnsureExactMesh(template); }
                    if (template.boxes == null)
                    {
                        template.boxes = new List<Bounds>();
                        // Bounded scratch partitions keep an oversized component as one body.
                        for (int start = 0; start < template.cellCount; start += VoxelAssetDefinition.MaximumCells)
                            template.boxes.AddRange(VoxelGeometry.CollisionBoxes(template.snapshot.Slice(start,
                                System.Math.Min(VoxelAssetDefinition.MaximumCells, template.cellCount - start)), template.cellSize));
                    }
                }
                return template;
            }
            catch { Release(template); throw; }
        }
        static void EnsureModels(Template template)
        {
            if (template.modelGeneration != null) return;
            template.modelGeneration = AcquireModels(template.asset.hullModules);
            template.frozenModels = template.modelGeneration.models.ForkFrozen();
        }
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
            if (template.snapshot == null || (template.usesHullModels && template.frozenModels == null)) throw new System.ObjectDisposedException(nameof(template));
            template.mesh = template.usesHullModels
                ? VoxelHullAppearance.BuildChunk(template.snapshot, template.snapshot, template.materialCount, template.cellSize, template.frozenModels)
                : VoxelGeometry.Build(template.snapshot, template.materialCount, template.cellSize, null);
            template.mesh.hideFlags = HideFlags.HideAndDontSave; template.exactMeshBuilt = true;
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
            template.frozenModels?.Dispose(); template.frozenModels = null;
            ReleaseModels(template.modelGeneration); template.modelGeneration = null;
            template.snapshot = null;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void Reset() => Clear();
    }
}
