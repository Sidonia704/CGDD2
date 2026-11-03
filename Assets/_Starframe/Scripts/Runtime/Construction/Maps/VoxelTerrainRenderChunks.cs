// @role Share exterior render tiles, optionally prewarm hidden views during loading, and rebuild only damaged neighbourhoods while preserving source leases.
// @calls VoxelHullAppearance, VoxelAssetDefinition, VoxelStructureRuntime, VoxelTemplateCache

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Starframe.Maps
{
    // Rendering only: these children never create colliders, scripts or rigidbodies.
    // All APIs use the Unity main thread; original cell coordinates remain asset-local, including after a split.
    public sealed class VoxelTerrainRenderChunks : IDisposable
    {
        public const int ChunkEdge = 4;
        public const int MinimumCells = 512;

        internal sealed class MeshResource
        {
            public Mesh mesh;
            public int[] masks;
            int users = 1;
            public MeshResource(Mesh value, int[] signature) { mesh = value; masks = signature; }
            public MeshResource Retain() { users++; return this; }
            public bool Matches(int[] other)
            {
                if (!mesh || masks.Length != other.Length) return false;
                for (int i = 0; i < masks.Length; i++) if (masks[i] != other[i]) return false;
                return true;
            }
            public void Release()
            {
                if (users <= 0 || --users != 0) return;
                DestroyResource(mesh); mesh = null; masks = null;
            }
        }
        internal sealed class Chunk
        {
            public Vector3Int coordinate;
            public Vector3Int[] positions;
            public MeshResource intact;
        }
        sealed class Binding
        {
            public MeshResource resource;
            public GameObject view;
            public MeshFilter filter;
            public MeshRenderer renderer;
        }
        public sealed class SharedTemplate
        {
            internal readonly VoxelAssetDefinition asset;
            internal readonly int revision, materialCount;
            internal readonly float cellSize;
            internal readonly VoxelHullAppearance.ModelCache models;
            internal readonly List<Chunk> chunks = new List<Chunk>();
            internal readonly Dictionary<Vector3Int, int> cellChunks = new Dictionary<Vector3Int, int>();
            internal int users;
            internal bool disposed;
            internal readonly bool exteriorOnly;
            public int ChunkCount => chunks.Count;
            public int TotalVertices { get; private set; }
            // Lower bound for generated mesh streams, excluding managed cache arrays and Unity object/renderer overhead.
            public long EstimatedMeshBytes { get; private set; }
            public double WarmupMilliseconds { get; private set; }
            public int Users => users;
            internal SharedTemplate(VoxelAssetDefinition source)
                : this(source, CopyCells(source), source.SurfaceCount * VoxelHullAppearance.ShapeCount,
                    source.cellSize, source.GeometryRevision, new VoxelHullAppearance.ModelCache(source.hullModules)) { }
            internal SharedTemplate(VoxelAssetDefinition source, IReadOnlyDictionary<Vector3Int, VoxelCellState> snapshot,
                int materials, float size, int generation, VoxelHullAppearance.ModelCache frozen, int edge = ChunkEdge, bool exterior = false)
            {
                asset = source; revision = generation; materialCount = materials; cellSize = size; models = frozen;
                exteriorOnly = exterior;
                var clock = Stopwatch.StartNew();
                try
                {
                    var groups = new Dictionary<Vector3Int, List<Vector3Int>>();
                    foreach (var cell in snapshot.Values)
                    {
                        var key = Coordinate(cell.position, edge);
                        if (!groups.TryGetValue(key, out var group)) { group = new List<Vector3Int>(); groups.Add(key, group); }
                        group.Add(cell.position);
                    }
                    foreach (var group in groups)
                    {
                        var chunk = new Chunk { coordinate = group.Key, positions = group.Value.ToArray() };
                        var subset = Subset(chunk, snapshot);
                        var masks = Signature(chunk, snapshot, models, exteriorOnly);
                        bool exposed = false; foreach (int mask in masks) if (mask >= 0 && mask != 63) { exposed = true; break; }
                        Mesh mesh = null;
                        if (exposed)
                        {
                            mesh = exteriorOnly ? VoxelGeometry.BuildExterior(subset, snapshot, materialCount, cellSize) : VoxelHullAppearance.BuildChunk(subset, snapshot, materialCount, cellSize, models);
                            mesh.name = "Shared terrain chunk " + group.Key; mesh.hideFlags = HideFlags.HideAndDontSave;
                            chunk.intact = new MeshResource(mesh, masks);
                        }
                        int id = chunks.Count; chunks.Add(chunk);
                        foreach (var position in chunk.positions) cellChunks.Add(position, id);
                        if (mesh)
                        {
                            TotalVertices += mesh.vertexCount;
                            EstimatedMeshBytes += (long)mesh.vertexCount * 48;
                            for (int i = 0; i < mesh.subMeshCount; i++) EstimatedMeshBytes += (long)mesh.GetIndexCount(i) * (mesh.indexFormat == UnityEngine.Rendering.IndexFormat.UInt32 ? 4 : 2);
                        }
                    }
                }
                catch { DisposeResources(); throw; }
                finally { clock.Stop(); WarmupMilliseconds = clock.Elapsed.TotalMilliseconds; }
            }
            internal void DisposeResources()
            {
                if (disposed) return; disposed = true;
                foreach (var chunk in chunks) chunk.intact?.Release();
                chunks.Clear(); cellChunks.Clear(); models.Dispose();
            }
        }
        static readonly Dictionary<VoxelAssetDefinition, SharedTemplate> shared = new Dictionary<VoxelAssetDefinition, SharedTemplate>();
        SharedTemplate template;
        VoxelAssetDefinition asset;
        Transform parent;
        Material[] materials;
        readonly Dictionary<int, Binding> bindings = new Dictionary<int, Binding>();
        readonly HashSet<int> dirtyChunks = new HashSet<int>();
        readonly List<Mesh> meshes = new List<Mesh>();
        readonly ReadOnlyCollection<Mesh> meshView;
        bool active;
        public IReadOnlyList<Mesh> Meshes => meshView;
        public IEnumerable<Mesh> EnumerateMeshes() => meshView;
        public int Revision { get; private set; }
        public bool HasChunks => active;
        public VoxelTerrainRenderChunks() { meshView = meshes.AsReadOnly(); }
        public static bool IsSupported(VoxelAssetDefinition asset) => asset && asset.UsesHullModels && asset.cells.Count >= MinimumCells;
        public static SharedTemplate AcquireTemplate(VoxelAssetDefinition asset)
        {
            if (!IsSupported(asset)) return null;
            if (!shared.TryGetValue(asset, out var result) || result.disposed || result.revision != asset.GeometryRevision)
            {
                result = new SharedTemplate(asset); shared[asset] = result;
            }
            result.users++; return result;
        }
        // Delayed warmup uses the retained source generation, even if the ScriptableObject was edited meanwhile.
        internal static SharedTemplate CreateFromSnapshot(VoxelAssetDefinition asset,
            IReadOnlyDictionary<Vector3Int, VoxelCellState> cells, int materialCount, float cellSize,
            int revision, VoxelHullAppearance.ModelCache models, int edge = ChunkEdge, bool exteriorOnly = false)
        {
            var result = new SharedTemplate(asset, cells, materialCount, cellSize, revision, models.ForkFrozen(), edge, exteriorOnly);
            result.users = 1;
            return result;
        }
        public static void ReleaseTemplate(SharedTemplate value)
        {
            if (value == null || value.users <= 0 || --value.users != 0) return;
            if (shared.TryGetValue(value.asset, out var current) && ReferenceEquals(current, value)) shared.Remove(value.asset);
            value.DisposeResources();
        }
        // The caller retains ownership of its AcquireTemplate lease. This helper independently retains another lease.
        public void Initialize(IReadOnlyDictionary<Vector3Int, VoxelCellState> cells, VoxelAssetDefinition definition, Material[] palette, Transform root, SharedTemplate sharedIntact)
        {
            Release();
            if (cells == null) throw new ArgumentNullException(nameof(cells));
            if (!definition) throw new ArgumentNullException(nameof(definition));
            if (!root) throw new ArgumentNullException(nameof(root));
            asset = definition; parent = root; materials = palette;
            // A fragment can still own an older, leased generation after the authored asset changes.
            // Its donor's template, rather than the asset's current cell count, selects the chunk path.
            if (sharedIntact == null)
            {
                if (!IsSupported(definition)) return;
                template = AcquireTemplate(definition);
            }
            else
            {
                if (sharedIntact.disposed || sharedIntact.asset != definition)
                    throw new ArgumentException("Terrain chunk template does not belong to this source asset.", nameof(sharedIntact));
                sharedIntact.users++; template = sharedIntact;
            }
            // Intact runtime keeps its single aggregate renderer. No child renderer is created until real damage/splitting.
        }
        public void Invalidate(IEnumerable<Vector3Int> positions)
        {
            if (template == null || positions == null) return;
            foreach (var position in positions)
            {
                Mark(position);
                foreach (var direction in VoxelGeometry.Directions) Mark(position + direction);
            }
        }
        void Mark(Vector3Int position) { if (template.cellChunks.TryGetValue(position, out int id)) dirtyChunks.Add(id); }
        public void RebuildDirty(IReadOnlyDictionary<Vector3Int, VoxelCellState> cells)
        {
            if (template == null || dirtyChunks.Count == 0) return;
            bool changed = !active;
            if (!active)
            {
                // Attachment is deliberately part of the first-damage cost, not hidden during intact initialization.
                for (int id = 0; id < template.chunks.Count; id++) changed |= UpdateChunk(id, cells, null);
                active = true;
                foreach (var binding in bindings.Values) binding.renderer.enabled = binding.resource.mesh.vertexCount > 0;
            }
            else foreach (int id in dirtyChunks) changed |= UpdateChunk(id, cells, null);
            dirtyChunks.Clear();
            if (changed) CommitMeshes();
        }
        // Called under the loading screen; child renderers stay disabled until the first actual damage flush.
        public void PrewarmBindings(IReadOnlyDictionary<Vector3Int, VoxelCellState> cells)
        {
            if (template == null || active) return;
            for (int id = 0; id < template.chunks.Count; id++) UpdateChunk(id, cells, null);
        }
        public VoxelTerrainRenderChunks ForkForIsland(IReadOnlyDictionary<Vector3Int, VoxelCellState> islandCells, Material[] palette, Transform root)
        {
            if (template == null) return null;
            var result = new VoxelTerrainRenderChunks();
            try
            {
                result.Initialize(islandCells, asset, palette, root, template);
                var ids = new HashSet<int>();
                foreach (var pair in islandCells) if (pair.Value.Alive && template.cellChunks.TryGetValue(pair.Key, out int id))
                { ids.Add(id); dirtyChunks.Add(id); }
                foreach (int id in ids) result.UpdateChunk(id, islandCells, this);
                result.active = true;foreach(var binding in result.bindings.Values)binding.renderer.enabled=binding.resource.mesh.vertexCount>0; result.CommitMeshes(); return result;
            }
            catch { result.Release(); throw; }
        }
        bool UpdateChunk(int id, IReadOnlyDictionary<Vector3Int, VoxelCellState> cells, VoxelTerrainRenderChunks donor)
        {
            var chunk = template.chunks[id];
            var masks = Signature(chunk, cells, template.models, template.exteriorOnly);
            // BuildChunk skips a cell whose six faces are all covered (mask 63), so a chunk that only contains such
            // cells has no geometry and must not keep a child renderer, MeshFilter or mesh binding at all.
            bool hasCells = false; for (int i = 0; i < masks.Length; i++) if (masks[i] >= 0 && masks[i] != 63) { hasCells = true; break; }
            bindings.TryGetValue(id, out var old);
            if (!hasCells)
            {
                if (old == null) return false;
                DestroyBinding(old); bindings.Remove(id); return true;
            }
            if (old != null && old.resource.Matches(masks)) return false;
            MeshResource resource = null;
            if (donor != null && donor.bindings.TryGetValue(id, out var source) && source.resource.Matches(masks)) resource = source.resource.Retain();
            else if (chunk.intact != null && chunk.intact.Matches(masks)) resource = chunk.intact.Retain();
            else
            {
                var subset = Subset(chunk, cells);
                var mesh = template.exteriorOnly ? VoxelGeometry.BuildExterior(subset, cells, template.materialCount, template.cellSize) : VoxelHullAppearance.BuildChunk(subset, cells, template.materialCount, template.cellSize, template.models);
                mesh.name = "Damaged terrain chunk " + chunk.coordinate; mesh.hideFlags = HideFlags.HideAndDontSave;
                resource = new MeshResource(mesh, masks);
            }
            if (resource.mesh.vertexCount == 0)
            {
                // Never mount a binding for an empty mesh (also covers a prewarmed empty chunk resource). A later
                // neighbour edit re-marks this chunk dirty through Invalidate's neighbour halo, so it reattaches.
                resource.Release();
                if (old == null) return false;
                DestroyBinding(old); bindings.Remove(id); return true;
            }
            if (old == null)
            {
                try
                {
                    old = CreateBinding(resource); bindings.Add(id, old);
                }
                catch { resource.Release(); throw; }
            }
            else
            {
                old.filter.sharedMesh = resource.mesh; old.renderer.enabled = active && resource.mesh.vertexCount > 0;
                var previous = old.resource; old.resource = resource; previous.Release();
            }
            return true;
        }
        Binding CreateBinding(MeshResource resource)
        {
            var view = new GameObject("Terrain render chunk");
            try
            {
                view.layer = parent.gameObject.layer; view.hideFlags = parent.gameObject.hideFlags;
                view.transform.SetParent(parent, false);
                var filter = view.AddComponent<MeshFilter>(); var renderer = view.AddComponent<MeshRenderer>();
                filter.sharedMesh = resource.mesh; renderer.sharedMaterials = materials; renderer.enabled = active && resource.mesh.vertexCount > 0;
                return new Binding { resource = resource, view = view, filter = filter, renderer = renderer };
            }
            catch { DestroyResource(view); throw; }
        }
        void CommitMeshes()
        {
            meshes.Clear(); var ids = new List<int>(bindings.Keys); ids.Sort();
            foreach (int id in ids)
            {
                var binding = bindings[id];
                if (binding.view && binding.renderer && binding.renderer.enabled && binding.resource.mesh && binding.resource.mesh.vertexCount > 0) meshes.Add(binding.resource.mesh);
            }
            Revision++;
        }
        public void Release()
        {
            bool changed = active || meshes.Count > 0;
            foreach (var binding in bindings.Values) DestroyBinding(binding);
            bindings.Clear(); meshes.Clear(); dirtyChunks.Clear(); active = false;
            ReleaseTemplate(template); template = null; asset = null; parent = null; materials = null;
            if (changed) Revision++;
        }
        public void Dispose() => Release();
        static void DestroyBinding(Binding binding)
        {
            if (binding.renderer) binding.renderer.enabled = false;
            if (binding.filter) binding.filter.sharedMesh = null;
            DestroyResource(binding.view); binding.resource.Release();
        }
        static void DestroyResource(Object value)
        {
            if (!value) return;
            if (Application.isPlaying) Object.Destroy(value); else Object.DestroyImmediate(value);
        }
        static int Floor(int value, int edge) => (value < 0 ? value - edge + 1 : value) / edge;
        static Dictionary<Vector3Int, VoxelCellState> CopyCells(VoxelAssetDefinition source)
        {
            var result = new Dictionary<Vector3Int, VoxelCellState>();
            foreach (var cell in source.cells) result.Add(cell.position, new VoxelCellState
            {
                position = cell.position, surface = cell.surface, shape = cell.shape,
                quarterTurns = cell.quarterTurns, modelOffset = cell.modelOffset
            });
            return result;
        }
        static Vector3Int Coordinate(Vector3Int value, int edge) => new Vector3Int(Floor(value.x, edge), Floor(value.y, edge), Floor(value.z, edge));
        static Dictionary<Vector3Int, VoxelCellState> Subset(Chunk chunk, IReadOnlyDictionary<Vector3Int, VoxelCellState> cells)
        {
            var result = new Dictionary<Vector3Int, VoxelCellState>();
            foreach (var position in chunk.positions) if (cells.TryGetValue(position, out var cell) && cell.Alive) result.Add(position, cell);
            return result;
        }
        static int[] Signature(Chunk chunk, IReadOnlyDictionary<Vector3Int, VoxelCellState> cells, VoxelHullAppearance.ModelCache models, bool exteriorOnly = false)
        {
            var result = new int[chunk.positions.Length];
            for (int i = 0; i < result.Length; i++)
                result[i] = cells.TryGetValue(chunk.positions[i], out var cell) && cell.Alive ? exteriorOnly ? ExteriorMask(cell.position, cells) : VoxelHullAppearance.CoverageMask(cell, cells, models) : -1;
            return result;
        }
        static int ExteriorMask(Vector3Int position, IReadOnlyDictionary<Vector3Int,VoxelCellState> cells)
        {
            int mask=0;for(int i=0;i<6;i++)if(cells.TryGetValue(position+VoxelGeometry.Directions[i],out var neighbor)&&neighbor.Alive)mask|=1<<i;return mask;
        }
    }
}
