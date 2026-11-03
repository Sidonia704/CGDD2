using System;
using System.Collections.Generic;
using Starframe.Construction;
using Starframe.Maps;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Starframe.EditorTools
{
    sealed class VoxelPreviewInstances : IDisposable
    {
        // Native mesh combination keeps normal URP lighting/depth and avoids managed vertex copies.
        sealed class Geometry
        {
            public VoxelAssetDefinition asset;
            public int revision, users;
            public readonly List<Mesh> meshes = new List<Mesh>();
            public readonly List<int> slots = new List<int>();
            public bool Valid => meshes.TrueForAll(mesh => mesh);
        }
        static readonly Dictionary<VoxelAssetDefinition, Geometry> shared = new Dictionary<VoxelAssetDefinition, Geometry>();
        sealed class Batch { public Mesh mesh; public GameObject view; }
        readonly VoxelStructureRuntime runtime;
        List<Batch> batches = new List<Batch>();
        readonly Dictionary<string, Mesh> meshes = new Dictionary<string, Mesh>();
        readonly List<Mesh> ownedMeshes = new List<Mesh>();
        Geometry geometry;
        int alive = -1, renderRevision = -1;
        bool disposed;
        public int InstanceCount => alive;
        public int BatchCount => batches.Count;
        public bool Valid { get { foreach (var batch in batches) if (!batch.mesh || !batch.view) return false; return runtime; } }
        public VoxelPreviewInstances(VoxelStructureRuntime source)
        {
            runtime = source;
            try { Refresh(); }
            catch
            {
                // A failed constructor never reaches SpaceMapStudioWindow.previewItems, so it must release
                // every temporary mesh and any shared-geometry lease before the exception escapes.
                Dispose();
                throw;
            }
        }
        public bool Refresh()
        {
            if (disposed) throw new ObjectDisposedException(nameof(VoxelPreviewInstances));
            if (alive == runtime.AliveCount && renderRevision == runtime.RenderRevision && Valid) return false;
            ClearBatches();
            var palette = runtime.GetComponent<MeshRenderer>().sharedMaterials;
            bool intact = runtime.AliveCount == runtime.asset.cells.Count;
            Geometry nextGeometry = null;
            bool builtGeometry = false;
            bool acquiredLease = false;
            var nextBatches = new List<Batch>();
            try
            {
                if (intact && shared.TryGetValue(runtime.asset, out var cached) && cached.revision == runtime.asset.GeometryRevision && cached.Valid)
                {
                    // Acquire before publishing the reference locally or creating any child views. If view creation
                    // fails, the catch path owns exactly one lease and can safely release it.
                    Acquire(cached); acquiredLease = true; nextGeometry = cached;
                }
                else
                {
                    nextGeometry = new Geometry { asset = runtime.asset, revision = runtime.asset.GeometryRevision };
                    builtGeometry = true;
                    var groups = new Dictionary<Material, List<CombineInstance>>();
                    var slots = new Dictionary<Material, int>();
                    foreach (var cell in runtime.Cells.Values)
                    {
                        if (!cell.Alive) continue;
                        string key = cell.shape + ":" + cell.modelOffset;
                        if (!meshes.TryGetValue(key, out var mesh))
                        {
                            mesh = VoxelHullAppearance.CreateCellMesh(runtime.asset.hullModules, cell.shape, cell.modelOffset);
                            ownedMeshes.Add(mesh); meshes.Add(key, mesh);
                        }
                        int slot = Mathf.Clamp(cell.surface, 0, palette.Length / VoxelHullAppearance.ShapeCount - 1) * VoxelHullAppearance.ShapeCount + VoxelHullAppearance.ShapeIndex(cell.shape);
                        var original = palette[slot];
                        if (!groups.TryGetValue(original, out var instances)) { instances = new List<CombineInstance>(); groups.Add(original, instances); slots.Add(original, slot); }
                        instances.Add(new CombineInstance { mesh = mesh, subMeshIndex = 0, transform = Matrix4x4.TRS((Vector3)cell.position * runtime.asset.cellSize, cell.Rotation, Vector3.one * runtime.asset.cellSize) });
                    }
                    foreach (var group in groups)
                    {
                        var combined = new Mesh { name = "Native combined map preview", hideFlags = HideFlags.HideAndDontSave, indexFormat = IndexFormat.UInt32 };
                        nextGeometry.meshes.Add(combined);
                        combined.CombineMeshes(group.Value.ToArray(), true, true, false);
                        nextGeometry.slots.Add(slots[group.Key]);
                    }
                }

                for (int i = 0; i < nextGeometry.meshes.Count; i++)
                {
                    var combined = nextGeometry.meshes[i];
                    var batch = new Batch { mesh = combined };
                    nextBatches.Add(batch);
                    var view = new GameObject("Preview surface", typeof(MeshFilter), typeof(MeshRenderer));
                    batch.view = view;
                    view.transform.SetParent(runtime.transform, false);
                    view.GetComponent<MeshFilter>().sharedMesh = combined;
                    view.GetComponent<MeshRenderer>().sharedMaterial = palette[nextGeometry.slots[i]];
                }

                if (!acquiredLease)
                {
                    // A new geometry remains local until every child binding is complete. Only then can it become
                    // a cached entry and acquire this preview's lease.
                    if (intact) shared[runtime.asset] = nextGeometry;
                    Acquire(nextGeometry); acquiredLease = true;
                }
                geometry = nextGeometry;
                batches = nextBatches;
                alive = runtime.AliveCount; renderRevision = runtime.RenderRevision; return true;
            }
            catch
            {
                foreach (var batch in nextBatches) DestroyView(batch.view);
                if (acquiredLease) ReleaseGeometry(nextGeometry);
                else if (builtGeometry) DestroyGeometry(nextGeometry);
                // The instance may be under construction and therefore absent from the window's cleanup list.
                // Release its source-cell meshes here as well as in the constructor guard.
                foreach (var mesh in ownedMeshes) if (mesh) Object.DestroyImmediate(mesh);
                ownedMeshes.Clear(); meshes.Clear();
                throw;
            }
        }
        void ClearBatches()
        {
            foreach (var batch in batches) DestroyView(batch.view);
            batches.Clear();
            if (geometry != null) ReleaseGeometry(geometry);
            geometry = null;
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            ClearBatches();
            foreach (var mesh in ownedMeshes) if (mesh) Object.DestroyImmediate(mesh);
            meshes.Clear(); ownedMeshes.Clear();
        }
        static void Acquire(Geometry value) { value.users++; }
        static void ReleaseGeometry(Geometry value)
        {
            if (value.users <= 0) throw new InvalidOperationException("Map preview geometry lease underflow.");
            value.users--;
            if (value.users == 0) DestroyGeometry(value);
        }
        static void DestroyGeometry(Geometry value)
        {
            if (value == null) return;
            if (shared.TryGetValue(value.asset, out var current) && ReferenceEquals(current, value)) shared.Remove(value.asset);
            foreach (var mesh in value.meshes) if (mesh) Object.DestroyImmediate(mesh);
            value.meshes.Clear(); value.slots.Clear();
        }
        static void DestroyView(GameObject view)
        {
            if (!view) return;
            var filter = view.GetComponent<MeshFilter>();
            if (filter) filter.sharedMesh = null;
            Object.DestroyImmediate(view);
        }
    }
}
