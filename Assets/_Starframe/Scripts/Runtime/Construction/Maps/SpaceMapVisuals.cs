// @role Select authored coarse or exact meshes for intact scenery and cull whole-object renderers without touching physics or voxel state.
// @calls SpaceMapRuntime, VoxelAssetDefinition, VoxelStructureRuntime

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Starframe.Maps
{
    [Serializable]
    public sealed class SpaceMapVisualLodEntry
    {
        public VoxelAssetDefinition asset;
        // Same asset-local coordinate system and material-slot order as the exact mesh.
        public Mesh mesh;
        public long geometrySignature;
    }

    internal sealed class SpaceMapVisuals
    {
        sealed class State
        {
            public VoxelStructureRuntime structure;
            public MeshRenderer[] exact;
            public MeshRenderer proxy;
            public MeshRenderer sourceRenderer;
            public MeshFilter sourceFilter;
            public Material[] materials;
            public Action<VoxelCellState> destroyedHandler;
            public int renderRevision, sourceRevision, originalCells;
            public bool damaged;
        }

        readonly Dictionary<VoxelStructureRuntime, State> states = new Dictionary<VoxelStructureRuntime, State>();
        readonly Dictionary<VoxelAssetDefinition, KeyValuePair<int, long>> signatures = new Dictionary<VoxelAssetDefinition, KeyValuePair<int, long>>();
        readonly List<VoxelStructureRuntime> stale = new List<VoxelStructureRuntime>();

        public void Refresh(IReadOnlyList<VoxelStructureRuntime> structures, List<SpaceMapVisualLodEntry> entries,
            Camera camera, float lodDistance, float cullDistance, int maximumFineCells = 0,
            bool keepCoarseIntactVisuals = false)
        {
            stale.Clear();
            foreach (var pair in states) if (!pair.Key) stale.Add(pair.Key);
            foreach (var key in stale) { Release(states[key]); states.Remove(key); }
            foreach (var structure in structures)
            {
                if (!structure || !structure.asset) continue;
                if (!states.TryGetValue(structure, out var state))
                {
                    state = new State { structure = structure, renderRevision = structure.RenderRevision,
                        sourceRevision = structure.SourceGeometryRevision, originalCells = structure.AliveCount };
                    state.sourceRenderer = structure.GetComponent<MeshRenderer>();
                    state.sourceFilter = structure.GetComponent<MeshFilter>();
                    state.materials = state.sourceRenderer ? state.sourceRenderer.sharedMaterials : null;
                    state.exact = ExactRenderers(state);
                    state.destroyedHandler = _ => Damage(state);
                    structure.CellDestroyed += state.destroyedHandler;
                    states.Add(structure, state);
                }
                if (state.renderRevision != structure.RenderRevision)
                {
                    state.renderRevision = structure.RenderRevision;
                    state.exact = ExactRenderers(state);
                }
                if (structure.AliveCount != state.originalCells || structure.asset.GeometryRevision != state.sourceRevision)
                    Damage(state);
                bool culled = false, coarse = false;
                Mesh coarseMesh = null;
                if (camera)
                {
                    var world = WorldBounds(structure.transform, structure.LocalBounds);
                    float distance = Vector3.Distance(camera.transform.position, world.ClosestPoint(camera.transform.position));
                    culled = cullDistance > 0 && distance > cullDistance;
                    if (!keepCoarseIntactVisuals && !culled && structure.HasLazyInitialVisual &&
                        (maximumFineCells <= 0 || structure.AliveCount <= maximumFineCells) &&
                        (lodDistance <= 0 || distance <= lodDistance))
                    {
                        structure.EnsureExactVisuals();
                        state.renderRevision = structure.RenderRevision;
                        state.exact = ExactRenderers(state);
                    }
                    if (!culled && lodDistance > 0 && distance > lodDistance && !state.damaged &&
                        !structure.HasLazyInitialVisual && !structure.DataOnlyPreview && structure.AliveCount > 0)
                        coarseMesh = MatchingMesh(state, entries);
                    coarse = coarseMesh;
                }
                if (coarse && !state.proxy) CreateProxy(state, coarseMesh);
                foreach (var renderer in state.exact) if (renderer) renderer.forceRenderingOff = culled || coarse;
                if (state.proxy) state.proxy.enabled = coarse;
            }
        }

        static MeshRenderer[] ExactRenderers(State state)
        {
            var all = state.structure.GetComponentsInChildren<MeshRenderer>(true);
            if (!state.proxy) return all;
            var exact = new List<MeshRenderer>(all.Length);
            foreach (var renderer in all) if (renderer != state.proxy) exact.Add(renderer);
            return exact.ToArray();
        }
        static void Damage(State state)
        {
            state.damaged = true;
            if (state.proxy) state.proxy.enabled = false;
            if (state.exact != null) foreach (var renderer in state.exact) if (renderer) renderer.forceRenderingOff = false;
        }
        Mesh MatchingMesh(State state, List<SpaceMapVisualLodEntry> entries)
        {
            var asset = state.structure.asset;
            // A detached island retains the source asset reference but must never display its whole-asset proxy.
            if (state.originalCells != asset.cells.Count || state.sourceRevision != asset.GeometryRevision ||
                !state.sourceRenderer || !state.sourceRenderer.enabled || !state.sourceFilter ||
                !state.sourceFilter.sharedMesh || state.materials == null) return null;
            if (!signatures.TryGetValue(asset, out var current) || current.Key != asset.GeometryRevision)
            {
                current = new KeyValuePair<int, long>(asset.GeometryRevision, ComputeGeometrySignature(asset));
                signatures[asset] = current;
            }
            if (ValidCandidate(asset.initialLodMesh, asset.initialLodSignature, current.Value, state))
                return asset.initialLodMesh;
            if (entries != null) foreach (var entry in entries)
            {
                if (entry == null || entry.asset != asset) continue;
                if (ValidCandidate(entry.mesh, entry.geometrySignature, current.Value, state))
                    return entry.mesh;
            }
            return null;
        }
        static bool ValidCandidate(Mesh candidate, long signature, long current, State state)
        {
            return candidate && signature == current && candidate.vertexCount > 0 &&
                candidate.vertexCount < state.sourceFilter.sharedMesh.vertexCount &&
                candidate.subMeshCount == state.materials.Length;
        }
        static void CreateProxy(State state, Mesh mesh)
        {
            var root = new GameObject("Distant scenery visual");
            root.layer = state.structure.gameObject.layer;
            root.hideFlags = state.structure.gameObject.hideFlags;
            root.transform.SetParent(state.structure.transform, false);
            var filter = root.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            state.proxy = root.AddComponent<MeshRenderer>();
            state.proxy.sharedMaterials = state.materials;
            state.proxy.enabled = false;
        }
        static Bounds WorldBounds(Transform transform, Bounds local)
        {
            var matrix = transform.localToWorldMatrix;
            var center = matrix.MultiplyPoint3x4(local.center);
            var extents = local.extents;
            var x = matrix.MultiplyVector(new Vector3(extents.x, 0, 0));
            var y = matrix.MultiplyVector(new Vector3(0, extents.y, 0));
            var z = matrix.MultiplyVector(new Vector3(0, 0, extents.z));
            return new Bounds(center, 2 * new Vector3(
                Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
                Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y),
                Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z)));
        }
        public void Clear()
        {
            foreach (var state in states.Values) Release(state);
            states.Clear(); signatures.Clear(); stale.Clear();
        }
        static void Release(State state)
        {
            if (state.structure) state.structure.CellDestroyed -= state.destroyedHandler;
            if (state.exact != null) foreach (var renderer in state.exact) if (renderer) renderer.forceRenderingOff = false;
            if (state.proxy)
            {
                var root = state.proxy.gameObject;
                state.proxy.enabled = false;
                if (Application.isPlaying) UnityEngine.Object.Destroy(root); else UnityEngine.Object.DestroyImmediate(root);
            }
        }
        public static long ComputeGeometrySignature(VoxelAssetDefinition asset)
        {
            if (!asset) throw new ArgumentNullException(nameof(asset));
            unchecked
            {
                ulong hash = 14695981039346656037UL;
                Mix(ref hash, asset.cellSize.GetHashCode());
                Mix(ref hash, asset.cells.Count);
                foreach (var cell in asset.cells)
                {
                    Mix(ref hash, cell.position.x); Mix(ref hash, cell.position.y); Mix(ref hash, cell.position.z);
                    Mix(ref hash, cell.surface); Mix(ref hash, (int)cell.shape);
                    Mix(ref hash, cell.quarterTurns.x); Mix(ref hash, cell.quarterTurns.y); Mix(ref hash, cell.quarterTurns.z);
                    Mix(ref hash, cell.modelOffset.x); Mix(ref hash, cell.modelOffset.y); Mix(ref hash, cell.modelOffset.z);
                }
                return (long)hash;
            }
        }
        static void Mix(ref ulong hash, int value)
        {
            unchecked { hash ^= (uint)value; hash *= 1099511628211UL; }
        }
    }
}
